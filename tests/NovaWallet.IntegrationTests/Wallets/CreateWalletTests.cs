using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using NovaWallet.IntegrationTests.Infrastructure;
using Npgsql;

namespace NovaWallet.IntegrationTests.Wallets;

public class CreateWalletTests(NovaWalletApiFactory factory) : IClassFixture<NovaWalletApiFactory>
{
    private const string Path = "/api/v1/wallets";

    [Fact]
    public async Task Customer_opens_their_own_wallet_with_zero_naira_balance()
    {
        var customerId = LedgerApi.NewCustomerId();

        var response = await factory.CustomerClient(customerId).PostAsJsonAsync(Path, new { customerId });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var walletId = body.GetProperty("walletId").GetGuid();
        Assert.Equal(customerId, body.GetProperty("customerId").GetString());
        Assert.Equal("NGN", body.GetProperty("currency").GetString());
        Assert.Equal(JsonValueKind.Number, body.GetProperty("balanceKobo").ValueKind);
        Assert.Equal(0, body.GetProperty("balanceKobo").GetInt64());
        Assert.Equal(factory.Clock.GetUtcNow(), body.GetProperty("createdAt").GetDateTimeOffset());
        Assert.Equal($"/api/v1/wallets/{walletId}/balance", response.Headers.Location?.OriginalString);
        Assert.Equal('4', walletId.ToString()[14]);
    }

    [Fact]
    public async Task Second_wallet_for_the_same_customer_is_refused_with_the_existing_id()
    {
        var customerId = LedgerApi.NewCustomerId();
        var existing = await factory.CreateWalletAsync(customerId);

        var response = await factory.CustomerClient(customerId).PostAsJsonAsync(Path, new { customerId });

        var problem = await response.AssertProblemAsync(HttpStatusCode.Conflict, "WALLET_ALREADY_EXISTS");
        Assert.Equal(existing, problem.GetProperty("existingWalletId").GetGuid());
        Assert.Equal(1, await CountWalletsAsync(customerId));
    }

    [Fact]
    public async Task Concurrent_creates_for_one_customer_produce_exactly_one_wallet()
    {
        var customerId = LedgerApi.NewCustomerId();
        var client = factory.CustomerClient(customerId);

        var responses = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => client.PostAsJsonAsync(Path, new { customerId })));

        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Created);
        Assert.Equal(9, responses.Count(r => r.StatusCode == HttpStatusCode.Conflict));
        Assert.Equal(1, await CountWalletsAsync(customerId));
    }

    [Fact]
    public async Task Opening_a_wallet_for_someone_else_is_forbidden()
    {
        var other = LedgerApi.NewCustomerId();

        var response = await factory.CustomerClient("cust-001").PostAsJsonAsync(Path, new { customerId = other });

        await response.AssertProblemAsync(HttpStatusCode.Forbidden, "FORBIDDEN");
        Assert.Equal(0, await CountWalletsAsync(other));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{"customerId":""}""")]
    [InlineData("""{"customerId":"has spaces"}""")]
    [InlineData("""{"customerId":"semi;colon"}""")]
    [InlineData("""{"customerId":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"}""")]
    public async Task Invalid_customer_id_is_rejected(string json)
    {
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await factory.CustomerClient("cust-001").PostAsync(Path, content);

        var problem = await response.AssertProblemAsync(HttpStatusCode.BadRequest, "VALIDATION_FAILED");
        Assert.True(problem.GetProperty("errors").TryGetProperty("customerId", out _), problem.GetRawText());
    }

    [Fact]
    public async Task Missing_token_and_missing_scope_are_refused()
    {
        var anonymous = await factory.CreateClient().PostAsJsonAsync(Path, new { customerId = "cust-001" });
        var auditor = await factory.CreateClient().WithBearer(factory.Tokens.For("cust-001", TestTokens.AuditScope))
            .PostAsJsonAsync(Path, new { customerId = "cust-001" });

        await anonymous.AssertProblemAsync(HttpStatusCode.Unauthorized, "UNAUTHENTICATED");
        await auditor.AssertProblemAsync(HttpStatusCode.Forbidden, "FORBIDDEN");
    }

    [Fact]
    public async Task System_account_is_seeded_and_runtime_role_can_only_open_wallets_and_change_balances()
    {
        await using var connection = new NpgsqlConnection(factory.OwnerConnectionString);
        await connection.OpenAsync();

        await using var seed = new NpgsqlCommand("SELECT kind, customer_id IS NULL, balance_kobo FROM wallets WHERE id = '00000000-0000-0000-0000-000000000001'", connection);
        await using (var reader = await seed.ExecuteReaderAsync())
        {
            Assert.True(await reader.ReadAsync());
            Assert.Equal("SYSTEM", reader.GetString(0));
            Assert.True(reader.GetBoolean(1));
        }

        await using var grants = new NpgsqlCommand(
            "SELECT string_agg(privilege_type, ',' ORDER BY privilege_type) FROM information_schema.role_table_grants WHERE grantee = 'novawallet_app' AND table_name = 'wallets'",
            connection);
        Assert.Equal("INSERT,SELECT", (string?)await grants.ExecuteScalarAsync());

        await using var columns = new NpgsqlCommand(
            "SELECT string_agg(column_name, ',' ORDER BY column_name) FROM information_schema.column_privileges WHERE grantee = 'novawallet_app' AND table_name = 'wallets' AND privilege_type = 'UPDATE'",
            connection);
        Assert.Equal("balance_kobo", (string?)await columns.ExecuteScalarAsync());
    }

    [Fact]
    public async Task Only_one_system_account_can_exist()
    {
        await using var connection = new NpgsqlConnection(factory.OwnerConnectionString);
        await connection.OpenAsync();
        await using var insert = new NpgsqlCommand(
            $"INSERT INTO wallets (id, customer_id, kind, balance_kobo, created_at) VALUES ('{Guid.NewGuid()}', NULL, 'SYSTEM', 0, now())",
            connection);

        var violation = await Assert.ThrowsAsync<PostgresException>(() => insert.ExecuteNonQueryAsync());
        Assert.Equal(PostgresErrorCodes.CheckViolation, violation.SqlState);
    }

    [Fact]
    public async Task Sixty_four_character_customer_id_is_accepted()
    {
        var customerId = new string('c', 64);

        var response = await factory.CustomerClient(customerId).PostAsJsonAsync(Path, new { customerId });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Database_rejects_a_negative_customer_balance_but_allows_one_on_the_system_account()
    {
        var walletId = await factory.CreateWalletAsync(LedgerApi.NewCustomerId());
        await using var connection = new NpgsqlConnection(factory.OwnerConnectionString);
        await connection.OpenAsync();

        await using var customer = new NpgsqlCommand($"UPDATE wallets SET balance_kobo = -1 WHERE id = '{walletId}'", connection);
        var violation = await Assert.ThrowsAsync<PostgresException>(() => customer.ExecuteNonQueryAsync());
        Assert.Equal(PostgresErrorCodes.CheckViolation, violation.SqlState);

        await using var system = new NpgsqlCommand(
            "UPDATE wallets SET balance_kobo = -1 WHERE id = '00000000-0000-0000-0000-000000000001'; UPDATE wallets SET balance_kobo = 0 WHERE id = '00000000-0000-0000-0000-000000000001';",
            connection);
        await system.ExecuteNonQueryAsync();
    }

    private async Task<long> CountWalletsAsync(string customerId)
    {
        await using var connection = new NpgsqlConnection(factory.OwnerConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT count(*) FROM wallets WHERE customer_id = @c", connection);
        command.Parameters.AddWithValue("c", customerId);
        return (long)(await command.ExecuteScalarAsync())!;
    }
}
