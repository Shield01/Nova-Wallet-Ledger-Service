using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using NovaWallet.IntegrationTests.Infrastructure;
using Npgsql;

namespace NovaWallet.IntegrationTests.Audit;

public class AuditTrailTests(NovaWalletApiFactory factory) : IClassFixture<NovaWalletApiFactory>
{
    private const string SystemAccountId = "00000000-0000-0000-0000-000000000001";

    [Theory]
    [InlineData("UPDATE audit_log SET amount_kobo = 0")]
    [InlineData("DELETE FROM audit_log")]
    [InlineData("TRUNCATE audit_log")]
    public async Task Database_rejects_changes_to_the_audit_log_even_from_the_owner(string statement)
    {
        var walletId = await SeedAsync(1);
        var before = await CountAsync(walletId);

        await using var connection = new NpgsqlConnection(factory.OwnerConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(statement, connection);
        var error = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());

        Assert.Contains("audit_log is append-only", error.MessageText);
        Assert.Equal(before, await CountAsync(walletId));
    }

    [Fact]
    public async Task Owner_cannot_switch_the_triggers_off_with_replication_role()
    {
        await using var connection = new NpgsqlConnection(factory.OwnerConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SET session_replication_role = replica", connection);

        await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
    }

    [Fact]
    public async Task Runtime_role_may_only_read_and_append()
    {
        await using var connection = new NpgsqlConnection(factory.OwnerConnectionString);
        await connection.OpenAsync();
        await using var grants = new NpgsqlCommand(
            "SELECT string_agg(privilege_type, ',' ORDER BY privilege_type) FROM information_schema.role_table_grants WHERE grantee = 'novawallet_app' AND table_name = 'audit_log'",
            connection);

        Assert.Equal("INSERT,SELECT", (string?)await grants.ExecuteScalarAsync());
    }

    [Fact]
    public async Task Records_appended_in_a_rolled_back_unit_of_work_do_not_persist()
    {
        var walletId = await factory.CreateWalletAsync(LedgerApi.NewCustomerId());

        var response = await Seeder().PostAsJsonAsync("/test/audit/seed-then-fail", new { walletId, count = 3 });

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal(0, await CountAsync(walletId));
    }

    [Fact]
    public async Task Records_carry_the_callers_subject_and_the_request_correlation_id()
    {
        var walletId = await factory.CreateWalletAsync(LedgerApi.NewCustomerId());
        var seeder = Seeder("auditor-7");
        seeder.DefaultRequestHeaders.Add("X-Correlation-ID", "corr-audit-1");
        (await seeder.PostAsJsonAsync("/test/audit/seed", new { walletId, count = 1 })).EnsureSuccessStatusCode();

        var page = await Auditor().GetFromJsonAsync<JsonElement>($"/api/v1/audit?walletId={walletId}");

        var entry = Assert.Single(page.GetProperty("items").EnumerateArray());
        Assert.Equal("auditor-7", entry.GetProperty("actor").GetString());
        Assert.Equal("corr-audit-1", entry.GetProperty("correlationId").GetString());
        Assert.Equal("CREDIT", entry.GetProperty("action").GetString());
        Assert.Equal(100, entry.GetProperty("amountKobo").GetInt64());
    }

    [Fact]
    public async Task Pages_walk_newest_first_with_an_opaque_cursor()
    {
        var walletId = await SeedAsync(25);

        var first = await Auditor().GetFromJsonAsync<JsonElement>($"/api/v1/audit?walletId={walletId}");
        var firstItems = first.GetProperty("items").EnumerateArray().ToList();
        var cursor = first.GetProperty("nextCursor").GetString();
        var second = await Auditor().GetFromJsonAsync<JsonElement>($"/api/v1/audit?walletId={walletId}&cursor={cursor}");
        var secondItems = second.GetProperty("items").EnumerateArray().ToList();

        Assert.Equal(20, firstItems.Count);
        Assert.Equal(5, secondItems.Count);
        Assert.Equal(JsonValueKind.Null, second.GetProperty("nextCursor").ValueKind);
        var all = firstItems.Concat(secondItems).Select(e => e.GetProperty("occurredAt").GetDateTimeOffset()).ToList();
        Assert.Equal(all.OrderByDescending(t => t), all);
        Assert.Equal(25, firstItems.Concat(secondItems).Select(e => e.GetProperty("id").GetInt64()).Distinct().Count());
    }

    [Fact]
    public async Task Equal_timestamps_are_ordered_by_id_descending_without_repeats()
    {
        var walletId = await factory.CreateWalletAsync(LedgerApi.NewCustomerId());
        (await Seeder().PostAsJsonAsync("/test/audit/seed", new { walletId, count = 7, sameInstant = true })).EnsureSuccessStatusCode();

        var first = await Auditor().GetFromJsonAsync<JsonElement>($"/api/v1/audit?walletId={walletId}&limit=4");
        var cursor = first.GetProperty("nextCursor").GetString();
        var second = await Auditor().GetFromJsonAsync<JsonElement>($"/api/v1/audit?walletId={walletId}&limit=4&cursor={cursor}");
        var ids = first.GetProperty("items").EnumerateArray().Concat(second.GetProperty("items").EnumerateArray())
            .Select(e => e.GetProperty("id").GetInt64()).ToList();

        Assert.Equal(7, ids.Count);
        Assert.Equal(ids.OrderByDescending(i => i), ids);
    }

    [Fact]
    public async Task System_account_may_be_inspected()
    {
        var response = await Auditor().GetAsync($"/api/v1/audit?walletId={SystemAccountId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Unknown_wallet_filter_is_not_found()
    {
        var response = await Auditor().GetAsync($"/api/v1/audit?walletId={Guid.NewGuid()}");

        await response.AssertProblemAsync(HttpStatusCode.NotFound, "WALLET_NOT_FOUND");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(100)]
    public async Task Limit_boundaries_are_accepted(int limit)
    {
        var walletId = await SeedAsync(3);

        var page = await Auditor().GetFromJsonAsync<JsonElement>($"/api/v1/audit?walletId={walletId}&limit={limit}");

        Assert.Equal(Math.Min(limit, 3), page.GetProperty("items").GetArrayLength());
    }

    [Theory]
    [InlineData("walletId=not-a-guid")]
    [InlineData("limit=0")]
    [InlineData("limit=101")]
    [InlineData("limit=ten")]
    [InlineData("cursor=garbage!!")]
    public async Task Invalid_query_is_rejected(string query)
    {
        var response = await Auditor().GetAsync($"/api/v1/audit?{query}");

        await response.AssertProblemAsync(HttpStatusCode.BadRequest, "VALIDATION_FAILED");
    }

    [Fact]
    public async Task Customers_and_anonymous_callers_cannot_read_the_trail()
    {
        var customer = await factory.CustomerClient("cust-001").GetAsync("/api/v1/audit");
        var anonymous = await factory.CreateClient().GetAsync("/api/v1/audit");

        await customer.AssertProblemAsync(HttpStatusCode.Forbidden, "FORBIDDEN");
        await anonymous.AssertProblemAsync(HttpStatusCode.Unauthorized, "UNAUTHENTICATED");
    }

    private HttpClient Auditor() => factory.CreateClient().WithBearer(factory.Tokens.For("auditor-1", TestTokens.AuditScope));

    private HttpClient Seeder(string subject = "seeder") =>
        factory.CreateClient().WithBearer(factory.Tokens.For(subject, TestTokens.AuditScope));

    private async Task<Guid> SeedAsync(int count)
    {
        var walletId = await factory.CreateWalletAsync(LedgerApi.NewCustomerId());
        (await Seeder().PostAsJsonAsync("/test/audit/seed", new { walletId, count })).EnsureSuccessStatusCode();
        return walletId;
    }

    private async Task<long> CountAsync(Guid walletId)
    {
        await using var connection = new NpgsqlConnection(factory.OwnerConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT count(*) FROM audit_log WHERE wallet_id = @w", connection);
        command.Parameters.AddWithValue("w", walletId);
        return (long)(await command.ExecuteScalarAsync())!;
    }
}
