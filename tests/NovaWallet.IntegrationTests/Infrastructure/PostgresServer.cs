using System.Security.Cryptography;
using Npgsql;
using Testcontainers.PostgreSql;

namespace NovaWallet.IntegrationTests.Infrastructure;

/// <summary>
/// One PostgreSQL server per test run: the server named by NOVAWALLET_TEST_DB if set, otherwise a
/// postgres:16-alpine container. Every test class creates its own database on it.
/// </summary>
internal static class PostgresServer
{
    public const string ExternalServerVariable = "NOVAWALLET_TEST_DB";
    public const string RuntimeRole = "novawallet_app";
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromSeconds(60);
    private static readonly Lazy<Task<ServerInfo>> Server = new(StartAsync);

    public static Task<ServerInfo> GetAsync() => Server.Value;

    private static async Task<ServerInfo> StartAsync()
    {
        var external = Environment.GetEnvironmentVariable(ExternalServerVariable);
        var adminConnectionString = string.IsNullOrWhiteSpace(external)
            ? await StartContainerAsync()
            : external;

        var runtimePassword = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        await EnsureRuntimeRoleAsync(adminConnectionString, runtimePassword);
        return new ServerInfo(adminConnectionString, runtimePassword);
    }

    private static async Task<string> StartContainerAsync()
    {
        try
        {
            var container = new PostgreSqlBuilder("postgres:16-alpine").Build();
            using var timeout = new CancellationTokenSource(StartupTimeout);
            await container.StartAsync(timeout.Token);
            AppDomain.CurrentDomain.ProcessExit += (_, _) => container.DisposeAsync().AsTask().GetAwaiter().GetResult();
            return container.GetConnectionString();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                "Integration tests need PostgreSQL. Either start Docker (tests will run postgres:16-alpine), " +
                $"or set {ExternalServerVariable} to a connection string for a PostgreSQL server whose user can create databases and roles.",
                ex);
        }
    }

    private static async Task EnsureRuntimeRoleAsync(string adminConnectionString, string password)
    {
        await using var connection = new NpgsqlConnection(adminConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        // The password is generated per run; quote_literal keeps it safe inside the dynamic SQL.
        command.CommandText = $"""
            DO $$
            BEGIN
                IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = '{RuntimeRole}') THEN
                    EXECUTE format('CREATE ROLE {RuntimeRole} LOGIN PASSWORD %L', '{password}');
                ELSE
                    EXECUTE format('ALTER ROLE {RuntimeRole} LOGIN PASSWORD %L', '{password}');
                END IF;
            END
            $$;
            """;
        await command.ExecuteNonQueryAsync();
    }

    internal sealed record ServerInfo(string AdminConnectionString, string RuntimePassword);
}
