using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using NovaWallet.IntegrationTests.Infrastructure;
using Npgsql;

namespace NovaWallet.IntegrationTests.Idempotency;

public class IdempotencyGuardTests(NovaWalletApiFactory factory) : IClassFixture<NovaWalletApiFactory>
{
    private const string Path = "/test/idempotency";

    [Fact]
    public async Task New_key_runs_the_operation_once_and_stores_its_outcome_in_the_same_transaction()
    {
        var (client, key, walletId) = await ArrangeAsync();

        var response = await Send(client, key, new { walletId, amountKobo = 700 });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.False(response.Headers.Contains("Idempotent-Replayed"));
        Assert.Equal(1, Invocations(key));
        Assert.Equal(("SUCCESS", 64), await StoredAsync(key));
        Assert.Equal(1, await AuditRowsAsync(walletId));
    }

    [Fact]
    public async Task Same_key_and_request_replays_the_stored_outcome_without_running_again()
    {
        var (client, key, walletId) = await ArrangeAsync();
        var first = await Send(client, key, new { walletId, amountKobo = 700 });

        var replay = await Send(client, key, new { walletId, amountKobo = 700 });

        Assert.Equal(HttpStatusCode.Created, replay.StatusCode);
        Assert.Equal("true", Assert.Single(replay.Headers.GetValues("Idempotent-Replayed")));
        Assert.Equal(await first.Content.ReadAsStringAsync(), await replay.Content.ReadAsStringAsync());
        Assert.Equal(1, Invocations(key));
        Assert.Equal(1, await AuditRowsAsync(walletId));
    }

    [Fact]
    public async Task Same_key_with_a_different_request_is_refused_without_running()
    {
        var (client, key, walletId) = await ArrangeAsync();
        await Send(client, key, new { walletId, amountKobo = 700 });

        var reused = await Send(client, key, new { walletId, amountKobo = 701 });

        await reused.AssertProblemAsync(HttpStatusCode.UnprocessableEntity, "IDEMPOTENCY_KEY_REUSED");
        Assert.Equal(1, Invocations(key));
    }

    [Fact]
    public async Task Same_key_while_the_first_request_is_in_flight_is_refused_immediately()
    {
        var (client, key, walletId) = await ArrangeAsync();
        var slow = Send(client, key, new { walletId, amountKobo = 700, delayMs = 2000 });
        await Task.Delay(400);

        var timer = Stopwatch.StartNew();
        var concurrent = await Send(client, key, new { walletId, amountKobo = 700 });
        timer.Stop();

        await concurrent.AssertProblemAsync(HttpStatusCode.Conflict, "IDEMPOTENCY_REQUEST_IN_PROGRESS");
        Assert.True(timer.ElapsedMilliseconds < 1000, $"took {timer.ElapsedMilliseconds} ms; it must not wait for the lock");
        Assert.Equal(HttpStatusCode.Created, (await slow).StatusCode);
        Assert.Equal(1, Invocations(key));
    }

    [Fact]
    public async Task Failed_operation_stores_nothing_and_the_key_can_be_retried()
    {
        var (client, key, walletId) = await ArrangeAsync();

        var failed = await Send(client, key, new { walletId, amountKobo = 700, failAfterWrite = true });

        await failed.AssertProblemAsync(HttpStatusCode.InternalServerError, "INTERNAL_ERROR");
        Assert.Null(await StoredOrNullAsync(key));
        Assert.Equal(0, await AuditRowsAsync(walletId));

        var retry = await Send(client, key, new { walletId, amountKobo = 700 });

        Assert.Equal(HttpStatusCode.Created, retry.StatusCode);
        Assert.Equal(2, Invocations(key));
        Assert.Equal(1, await AuditRowsAsync(walletId));
    }

    [Fact]
    public async Task Different_callers_use_the_same_key_independently()
    {
        var walletId = await factory.CreateWalletAsync(LedgerApi.NewCustomerId());
        var key = Guid.NewGuid().ToString();

        var a = await Send(Client("caller-a"), key, new { walletId, amountKobo = 1 });
        var b = await Send(Client("caller-b"), key, new { walletId, amountKobo = 2 });

        Assert.Equal(HttpStatusCode.Created, a.StatusCode);
        Assert.Equal(HttpStatusCode.Created, b.StatusCode);
        Assert.False(b.Headers.Contains("Idempotent-Replayed"));
    }

    [Fact]
    public async Task Json_property_order_does_not_change_the_request_fingerprint()
    {
        var (client, key, walletId) = await ArrangeAsync();
        await SendRaw(client, key, $$"""{"walletId":"{{walletId}}","amountKobo":700}""");

        var reordered = await SendRaw(client, key, $$"""{ "amountKobo": 700, "walletId": "{{walletId}}" }""");

        Assert.Equal(HttpStatusCode.Created, reordered.StatusCode);
        Assert.True(reordered.Headers.Contains("Idempotent-Replayed"));
    }

    [Fact]
    public async Task Final_refusals_are_stored_and_replayed_with_this_requests_correlation_id()
    {
        var (client, key, walletId) = await ArrangeAsync();
        var firstRequest = Request(key, new { walletId, amountKobo = 700, refuse = true }, "corr-first");
        var first = await client.SendAsync(firstRequest);

        var replay = await client.SendAsync(Request(key, new { walletId, amountKobo = 700, refuse = true }, "corr-replay"));

        var firstProblem = await first.AssertProblemAsync(HttpStatusCode.UnprocessableEntity, "INSUFFICIENT_FUNDS");
        var replayProblem = await replay.AssertProblemAsync(HttpStatusCode.UnprocessableEntity, "INSUFFICIENT_FUNDS");
        Assert.True(replay.Headers.Contains("Idempotent-Replayed"));
        Assert.Equal(5, replayProblem.GetProperty("remainingKobo").GetInt64());
        Assert.Equal(firstProblem.GetProperty("detail").GetString(), replayProblem.GetProperty("detail").GetString());
        Assert.Equal("corr-replay", replayProblem.GetProperty("correlationId").GetString());
        Assert.Equal(1, Invocations(key));
        Assert.Equal(("INSUFFICIENT_FUNDS", 64), await StoredAsync(key));
    }

    [Theory]
    [InlineData(null, "IDEMPOTENCY_KEY_REQUIRED")]
    [InlineData("", "IDEMPOTENCY_KEY_REQUIRED")]
    [InlineData("has space", "IDEMPOTENCY_KEY_INVALID")]
    [InlineData("tab\there", "IDEMPOTENCY_KEY_INVALID")]
    public async Task Missing_or_invalid_keys_are_rejected_before_anything_runs(string? key, string code)
    {
        var walletId = await factory.CreateWalletAsync(LedgerApi.NewCustomerId());
        var request = new HttpRequestMessage(HttpMethod.Post, Path) { Content = JsonContent.Create(new { walletId, amountKobo = 700 }) };
        if (key is not null)
        {
            request.Headers.TryAddWithoutValidation("Idempotency-Key", key);
        }

        var response = await Client("validator").SendAsync(request);

        await response.AssertProblemAsync(HttpStatusCode.BadRequest, code);
        Assert.Equal(0, await AuditRowsAsync(walletId));
    }

    [Fact]
    public async Task Keys_longer_than_128_characters_are_invalid_and_128_is_allowed()
    {
        var (client, _, walletId) = await ArrangeAsync();

        var tooLong = await Send(client, new string('k', 129), new { walletId, amountKobo = 1 });
        var longest = await Send(client, new string('k', 128), new { walletId, amountKobo = 1 });

        await tooLong.AssertProblemAsync(HttpStatusCode.BadRequest, "IDEMPOTENCY_KEY_INVALID");
        Assert.Equal(HttpStatusCode.Created, longest.StatusCode);
    }

    [Fact]
    public async Task Runtime_role_may_only_read_and_insert_records()
    {
        await using var connection = new NpgsqlConnection(factory.OwnerConnectionString);
        await connection.OpenAsync();
        await using var grants = new NpgsqlCommand(
            "SELECT string_agg(privilege_type, ',' ORDER BY privilege_type) FROM information_schema.role_table_grants WHERE grantee = 'novawallet_app' AND table_name = 'idempotency_records'",
            connection);

        Assert.Equal("INSERT,SELECT", (string?)await grants.ExecuteScalarAsync());
    }

    private async Task<(HttpClient Client, string Key, Guid WalletId)> ArrangeAsync()
    {
        var walletId = await factory.CreateWalletAsync(LedgerApi.NewCustomerId());
        return (Client("idem-caller"), Guid.NewGuid().ToString(), walletId);
    }

    private HttpClient Client(string subject) => factory.CreateClient().WithBearer(factory.Tokens.For(subject, TestTokens.WalletScope));

    private static HttpRequestMessage Request(string key, object body, string? correlationId = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, Path) { Content = JsonContent.Create(body) };
        request.Headers.TryAddWithoutValidation("Idempotency-Key", key);
        if (correlationId is not null)
        {
            request.Headers.Add("X-Correlation-ID", correlationId);
        }

        return request;
    }

    private static Task<HttpResponseMessage> Send(HttpClient client, string key, object body) => client.SendAsync(Request(key, body));

    private static Task<HttpResponseMessage> SendRaw(HttpClient client, string key, string json)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, Path) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        request.Headers.TryAddWithoutValidation("Idempotency-Key", key);
        return client.SendAsync(request);
    }

    private static int Invocations(string key) =>
        TestIdempotencyController.Invocations.Where(p => p.Key.EndsWith(":" + key, StringComparison.Ordinal)).Sum(p => p.Value);

    private async Task<(string Outcome, int HashLength)> StoredAsync(string key) =>
        await StoredOrNullAsync(key) ?? throw new Xunit.Sdk.XunitException("No idempotency record was stored.");

    private async Task<(string Outcome, int HashLength)?> StoredOrNullAsync(string key)
    {
        await using var connection = new NpgsqlConnection(factory.OwnerConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT outcome, length(request_hash) FROM idempotency_records WHERE idempotency_key = @k", connection);
        command.Parameters.AddWithValue("k", key);
        await using var reader = await command.ExecuteReaderAsync();
        return await reader.ReadAsync() ? (reader.GetString(0), reader.GetInt32(1)) : null;
    }

    private async Task<long> AuditRowsAsync(Guid walletId)
    {
        await using var connection = new NpgsqlConnection(factory.OwnerConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT count(*) FROM audit_log WHERE wallet_id = @w", connection);
        command.Parameters.AddWithValue("w", walletId);
        return (long)(await command.ExecuteScalarAsync())!;
    }
}
