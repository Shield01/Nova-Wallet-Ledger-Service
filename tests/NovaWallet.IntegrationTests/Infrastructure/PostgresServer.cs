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
    public const string OwnerRole = "novawallet_owner";

    /// <summary>
    /// Test classes run in parallel, each with its own API host and connection pool, against this
    /// one server. The default of 100 connections runs out on slow CI runners (PostgreSQL then
    /// refuses clients and the API answers 503).
    /// </summary>
    public const int MaxConnections = 300;
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromSeconds(60);
    private static readonly Lazy<Task<ServerInfo>> Server = new(StartAsync);

    public static Task<ServerInfo> GetAsync() => Server.Value;

    private static async Task<ServerInfo> StartAsync()
    {
        var external = Environment.GetEnvironmentVariable(ExternalServerVariable);
        var adminConnectionString = string.IsNullOrWhiteSpace(external)
            ? await StartContainerAsync()
            : external;

        var ownerPassword = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        var runtimePassword = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        await EnsureRoleAsync(adminConnectionString, OwnerRole, ownerPassword);
        await EnsureRoleAsync(adminConnectionString, RuntimeRole, runtimePassword);
        return new ServerInfo(adminConnectionString, ownerPassword, runtimePassword);
    }

    private static async Task<string> StartContainerAsync()
    {
        try
        {
            var container = new PostgreSqlBuilder("postgres:16-alpine")
                .WithCommand("-c", $"max_connections={MaxConnections}")
                .Build();
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

    // Mirrors deploy/db/init/01-create-roles.sh. The password is hex generated per run, so it is
    // safe to embed; format(%L) quotes it anyway.
    private static async Task EnsureRoleAsync(string adminConnectionString, string role, string password)
    {
        await using var connection = new NpgsqlConnection(adminConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            DO $$
            BEGIN
                IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = '{role}') THEN
                    EXECUTE format('CREATE ROLE {role} LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE PASSWORD %L', '{password}');
                ELSE
                    EXECUTE format('ALTER ROLE {role} LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE PASSWORD %L', '{password}');
                END IF;
            END
            $$;
            """;
        await command.ExecuteNonQueryAsync();
    }

    internal sealed record ServerInfo(string AdminConnectionString, string OwnerPassword, string RuntimePassword);
}
