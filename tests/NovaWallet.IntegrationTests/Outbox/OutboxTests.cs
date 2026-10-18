using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using NovaWallet.Api.Outbox;
using NovaWallet.Application.Audit;
using NovaWallet.Application.Outbox;
using NovaWallet.Infrastructure.Outbox;
using NovaWallet.Infrastructure.Persistence;
using NovaWallet.IntegrationTests.Infrastructure;
using Npgsql;

namespace NovaWallet.IntegrationTests.Outbox;

public class OutboxTests(NovaWalletApiFactory factory) : IClassFixture<NovaWalletApiFactory>, IAsyncLifetime
{
    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync() => factory.AssertHoldAsync();

    [Fact]
    public async Task Successful_transfer_records_one_TransferCompleted_with_the_documented_payload()
    {
        var (payer, source, destination) = await ArrangeAsync();
        var client = factory.CustomerClient(payer);
        client.DefaultRequestHeaders.Add("X-Correlation-ID", "corr-outbox-1");

        var response = await client.TransferAsync(source, destination, 2_500);
        var transfer = await response.Content.ReadFromJsonAsync<JsonElement>();

        var row = Assert.Single(await RowsForSourceAsync(source));
        Assert.Equal("TransferCompleted", row.Type);
        Assert.Null(row.PublishedAt);
        Assert.Equal(0, row.Attempts);
        var payload = JsonDocument.Parse(row.Payload).RootElement;
        Assert.Equal(row.Id, payload.GetProperty("eventId").GetGuid());
        Assert.Equal(transfer.GetProperty("transferId").GetGuid(), payload.GetProperty("transferId").GetGuid());
        Assert.Equal(source, payload.GetProperty("sourceWalletId").GetGuid());
        Assert.Equal(destination, payload.GetProperty("destinationWalletId").GetGuid());
        Assert.Equal(2_500, payload.GetProperty("amountKobo").GetInt64());
        Assert.Equal(transfer.GetProperty("createdAt").GetDateTimeOffset(), payload.GetProperty("occurredAt").GetDateTimeOffset());
        Assert.Equal("corr-outbox-1", payload.GetProperty("correlationId").GetString());
    }

    [Fact]
    public async Task Runtime_role_may_read_append_and_record_delivery_only()
    {
        Assert.Equal("INSERT,SELECT", await ScalarAsync<string>(
            "SELECT string_agg(privilege_type, ',' ORDER BY privilege_type) FROM information_schema.role_table_grants WHERE grantee = 'novawallet_app' AND table_name = 'outbox_messages'"));
        Assert.Equal("attempts,last_error,published_at", await ScalarAsync<string>(
            "SELECT string_agg(column_name, ',' ORDER BY column_name) FROM information_schema.column_privileges WHERE grantee = 'novawallet_app' AND table_name = 'outbox_messages' AND privilege_type = 'UPDATE'"));
    }

    [Fact]
    public async Task Refused_transfers_record_nothing()
    {
        var (payer, source, destination) = await ArrangeAsync(balance: 100);

        var refused = await factory.CustomerClient(payer).TransferAsync(source, destination, 5_000);

        await refused.AssertProblemAsync(HttpStatusCode.UnprocessableEntity, "INSUFFICIENT_FUNDS");
        Assert.Empty(await RowsForSourceAsync(source));
    }

    [Fact]
    public async Task Replayed_transfers_record_nothing_more()
    {
        var (payer, source, destination) = await ArrangeAsync();
        var key = Guid.NewGuid().ToString();
        var client = factory.CustomerClient(payer);

        (await client.TransferAsync(source, destination, 300, key)).EnsureSuccessStatusCode();
        var replay = await client.TransferAsync(source, destination, 300, key);

        Assert.True(replay.Headers.Contains("Idempotent-Replayed"));
        Assert.Single(await RowsForSourceAsync(source));
    }

    [Fact]
    public async Task Rolled_back_transfers_record_nothing()
    {
        var (payer, source, destination) = await ArrangeAsync();
        using var failing = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddScoped<IAuditTrail>(sp => new FailingAuditTrail(sp.GetRequiredService<LedgerDbContext>()))));

        var response = await failing.CreateClient().WithBearer(factory.Tokens.For(payer, TestTokens.WalletScope)).TransferAsync(source, destination, 300);

        await response.AssertProblemAsync(HttpStatusCode.InternalServerError, "INTERNAL_ERROR");
        Assert.Empty(await RowsForSourceAsync(source));
    }

    [Fact]
    public async Task Dispatch_publishes_through_the_port_and_marks_the_message_published()
    {
        var eventId = await TransferEventAsync();
        var publisher = new RecordingPublisher();
        using var host = Host(publisher);

        await DispatchAsync(host);

        Assert.Single(publisher.Published, m => m.Id == eventId);
        var row = await RowAsync(eventId);
        Assert.NotNull(row.PublishedAt);
        Assert.Equal(0, row.Attempts);
    }

    [Fact]
    public async Task Default_publisher_writes_one_structured_log_line()
    {
        var eventId = await TransferEventAsync();
        var transferId = JsonDocument.Parse((await RowAsync(eventId)).Payload).RootElement.GetProperty("transferId").GetString();
        var logs = new CapturingLoggerProvider();
        using var host = factory.WithWebHostBuilder(builder => builder.ConfigureLogging(logging =>
        {
            logging.AddProvider(logs);
            logging.SetMinimumLevel(LogLevel.Information);
        }));

        await DispatchAsync(host);

        var line = Assert.Single(logs.Entries, e => e.Category == typeof(LoggingEventPublisher).FullName && e.Message.Contains(eventId.ToString()));
        Assert.Equal(LogLevel.Information, line.Level);
        Assert.Contains("TransferCompleted", line.Message);
        Assert.Contains(transferId!, line.Message);
    }

    [Fact]
    public async Task Two_dispatchers_publish_each_message_exactly_once()
    {
        var eventIds = new List<Guid>();
        for (var i = 0; i < 30; i++)
        {
            eventIds.Add(await TransferEventAsync());
        }

        var publisher = new RecordingPublisher { Delay = TimeSpan.FromMilliseconds(5) };
        using var host = Host(publisher);

        for (var round = 0; round < 10 && await UnpublishedAsync() > 0; round++)
        {
            await Task.WhenAll(DispatchAsync(host), DispatchAsync(host));
        }

        var published = publisher.Published.Select(m => m.Id).ToList();
        Assert.Equal(published.Count, published.Distinct().Count());
        Assert.All(eventIds, id => Assert.Contains(id, published));
        Assert.Equal(0L, await UnpublishedAsync());
    }

    [Fact]
    public async Task Failed_publish_is_retried_on_the_next_poll_and_only_failures_count()
    {
        var eventId = await TransferEventAsync();
        var failures = 0;
        var publisher = new RecordingPublisher { FailWhen = m => m.Id == eventId && Interlocked.Increment(ref failures) == 1 };
        using var host = Host(publisher);

        await DispatchAsync(host);
        var afterFailure = await RowAsync(eventId);
        await DispatchAsync(host);
        var afterRetry = await RowAsync(eventId);

        Assert.Null(afterFailure.PublishedAt);
        Assert.Equal(1, afterFailure.Attempts);
        Assert.Contains(RecordingPublisher.FailureMessage, afterFailure.LastError);
        Assert.NotNull(afterRetry.PublishedAt);
        Assert.Equal(1, afterRetry.Attempts);
    }

    [Fact]
    public async Task Gives_up_after_the_maximum_attempts_and_logs_an_error()
    {
        var eventId = await TransferEventAsync();
        var publisher = new RecordingPublisher { FailWhen = m => m.Id == eventId };
        var logs = new CapturingLoggerProvider();
        using var host = Host(publisher, new() { ["Outbox:MaxAttempts"] = "3" }, logs);

        for (var poll = 0; poll < 6; poll++)
        {
            await DispatchAsync(host);
        }

        var row = await RowAsync(eventId);
        Assert.Null(row.PublishedAt);
        Assert.Equal(3, row.Attempts);
        Assert.Single(logs.Entries, e => e.Level == LogLevel.Error && e.Message.Contains(eventId.ToString()) && e.Message.Contains("giving up"));
    }

    [Fact]
    public async Task Poller_runs_on_the_configured_interval()
    {
        var fastPublisher = new RecordingPublisher();
        using (var fast = Host(fastPublisher, new() { ["Outbox:PollIntervalMilliseconds"] = "50" }, poller: true))
        {
            _ = fast.Server;
            var eventId = await TransferEventAsync();

            // The publisher sees the message before the poll commits, so wait for the committed row.
            await WaitUntilAsync(async () => (await RowAsync(eventId)).PublishedAt is not null, TimeSpan.FromSeconds(10));

            Assert.Contains(fastPublisher.Published, m => m.Id == eventId);
        }

        // A message already waiting is published by the start-up poll; once it is, that poll has
        // claimed its batch, and anything written afterwards waits for the next interval.
        var early = await TransferEventAsync();
        var slowPublisher = new RecordingPublisher();
        using var slow = Host(slowPublisher, new() { ["Outbox:PollIntervalMilliseconds"] = "60000" }, poller: true);
        _ = slow.Server;
        await WaitUntilAsync(() => Task.FromResult(slowPublisher.Published.Any(m => m.Id == early)), TimeSpan.FromSeconds(10));
        var late = await TransferEventAsync();
        await Task.Delay(TimeSpan.FromSeconds(1.5));

        Assert.DoesNotContain(slowPublisher.Published, m => m.Id == late);
        Assert.Null((await RowAsync(late)).PublishedAt);
    }

    [Fact]
    public async Task Poller_keeps_draining_while_batches_come_back_full()
    {
        var eventIds = new List<Guid>();
        for (var i = 0; i < 5; i++)
        {
            eventIds.Add(await TransferEventAsync());
        }

        var publisher = new RecordingPublisher();
        using var host = Host(publisher, new() { ["Outbox:PollIntervalMilliseconds"] = "60000", ["Outbox:BatchSize"] = "2" }, poller: true);
        _ = host.Server;

        // One tick, three batches (2, 2, 1+): nothing waits a minute for the next interval.
        await WaitUntilAsync(async () => (await Task.WhenAll(eventIds.Select(RowAsync))).All(r => r.PublishedAt is not null), TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task A_publisher_timeout_counts_as_a_failed_attempt()
    {
        var eventId = await TransferEventAsync();
        var publisher = new RecordingPublisher { FailWhen = m => m.Id == eventId, Failure = () => new TaskCanceledException("publisher timed out") };
        using var host = Host(publisher);

        await DispatchAsync(host);

        var row = await RowAsync(eventId);
        Assert.Null(row.PublishedAt);
        Assert.Equal(1, row.Attempts);
        Assert.Contains("TaskCanceledException", row.LastError);
    }

    private WebApplicationFactory<Program> Host(
        RecordingPublisher publisher,
        Dictionary<string, string?>? settings = null,
        CapturingLoggerProvider? logs = null,
        bool poller = false) =>
        factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(settings ?? []));
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IEventPublisher>();
                services.AddSingleton<IEventPublisher>(publisher);
                if (poller)
                {
                    services.AddHostedService<OutboxPollingService>();
                }
            });
            if (logs is not null)
            {
                builder.ConfigureLogging(logging => logging.AddProvider(logs));
            }
        });

    private static async Task<OutboxDispatcher.OutboxPoll> DispatchAsync(WebApplicationFactory<Program> host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<OutboxDispatcher>().DispatchOnceAsync(CancellationToken.None);
    }

    private static async Task WaitUntilAsync(Func<Task<bool>> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!await condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new Xunit.Sdk.XunitException("Timed out waiting for the condition.");
            }

            await Task.Delay(25);
        }
    }

    private async Task<(string Payer, Guid Source, Guid Destination)> ArrangeAsync(long balance = 10_000)
    {
        var payer = LedgerApi.NewCustomerId();
        var source = await factory.FundedWalletAsync(payer, balance);
        var destination = await factory.CreateWalletAsync(LedgerApi.NewCustomerId());
        return (payer, source, destination);
    }

    private async Task<Guid> TransferEventAsync()
    {
        var (payer, source, destination) = await ArrangeAsync();
        (await factory.CustomerClient(payer).TransferAsync(source, destination, 100)).EnsureSuccessStatusCode();
        return Assert.Single(await RowsForSourceAsync(source)).Id;
    }

    private Task<List<Row>> RowsForSourceAsync(Guid source) =>
        QueryRowsAsync("WHERE payload->>'sourceWalletId' = @value", source.ToString());

    private async Task<Row> RowAsync(Guid eventId) => Assert.Single(await QueryRowsAsync("WHERE id = @value::uuid", eventId.ToString()));

    private Task<long> UnpublishedAsync() => ScalarAsync<long>("SELECT count(*) FROM outbox_messages WHERE published_at IS NULL AND attempts = 0");

    private async Task<List<Row>> QueryRowsAsync(string where, string value)
    {
        await using var connection = new NpgsqlConnection(factory.OwnerConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"SELECT id, type, payload::text, published_at, attempts, last_error FROM outbox_messages {where}", connection);
        command.Parameters.AddWithValue("value", value);
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<Row>();
        while (await reader.ReadAsync())
        {
            rows.Add(new Row(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetFieldValue<DateTimeOffset>(3),
                reader.GetInt32(4),
                reader.IsDBNull(5) ? null : reader.GetString(5)));
        }

        return rows;
    }

    private async Task<T> ScalarAsync<T>(string sql)
    {
        await using var connection = new NpgsqlConnection(factory.OwnerConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        return (T)(await command.ExecuteScalarAsync())!;
    }

    private sealed record Row(Guid Id, string Type, string Payload, DateTimeOffset? PublishedAt, int Attempts, string? LastError);
}
