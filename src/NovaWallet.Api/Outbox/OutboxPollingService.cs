using Microsoft.Extensions.Options;
using NovaWallet.Infrastructure.Outbox;

namespace NovaWallet.Api.Outbox;

/// <summary>Runs the outbox dispatcher every <c>Outbox:PollIntervalMilliseconds</c> for the life of the process.</summary>
public sealed partial class OutboxPollingService(
    IServiceScopeFactory scopes,
    IOptions<OutboxSettings> settings,
    ILogger<OutboxPollingService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(settings.Value.PollIntervalMilliseconds));
        do
        {
            try
            {
                // Keep draining while full batches publish cleanly, so a backlog clears at publish
                // speed rather than one batch per interval. Any failure waits for the next interval,
                // so a broken publisher doesn't burn through every message's attempts at once.
                OutboxDispatcher.OutboxPoll poll;
                do
                {
                    await using var scope = scopes.CreateAsyncScope();
                    poll = await scope.ServiceProvider.GetRequiredService<OutboxDispatcher>().DispatchOnceAsync(stoppingToken);
                }
                while (poll.Claimed >= settings.Value.BatchSize && poll.Published == poll.Claimed && !stoppingToken.IsCancellationRequested);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                // A database blip must not stop the poller for the rest of the process's life.
                LogPollFailed(logger, exception);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
    }

    [LoggerMessage(EventId = 4003, Level = LogLevel.Error, Message = "Outbox poll failed; retrying on the next interval")]
    private static partial void LogPollFailed(ILogger logger, Exception exception);
}
