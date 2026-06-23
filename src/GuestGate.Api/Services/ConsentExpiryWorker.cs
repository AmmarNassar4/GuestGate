using GuestGate.Api.Data;
using GuestGate.Api.Hubs;
using GuestGate.Api.Models;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;

namespace GuestGate.Api.Services;

public sealed class ConsentExpiryWorker(
    IServiceScopeFactory scopeFactory,
    IHubContext<GuestHub> hub,
    IOptions<ConsentRequestOptions> consentOptions,
    ILogger<ConsentExpiryWorker> logger
) : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory = scopeFactory;
    private readonly IHubContext<GuestHub> _hub = hub;
    private readonly IOptions<ConsentRequestOptions> _consentOptions = consentOptions;
    private readonly ILogger<ConsentExpiryWorker> _logger = logger;

    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        _logger.LogInformation("ConsentExpiryWorker started. Interval: {Interval}", Interval);

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await CancelExpiredConsentsAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("ConsentExpiryWorker cancellation requested.");
        }

        _logger.LogInformation("ConsentExpiryWorker stopped.");
    }

    private async Task CancelExpiredConsentsAsync(CancellationToken stoppingToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDb>();
            var now = DateTime.UtcNow;
            var expiredRequests = await ConsentRequestMaintenance.MarkExpiredActiveRequestsAsync(
                db,
                now,
                _consentOptions.Value.ActiveLifetime,
                cancellationToken: stoppingToken);

            if (expiredRequests.Count == 0)
            {
                return;
            }

            await db.SaveChangesAsync(stoppingToken);

            foreach (var request in expiredRequests)
            {
                await ConsentRequestMaintenance.NotifyConsentChangedAsync(_hub, request, stoppingToken);
            }

            _logger.LogInformation("Cancelled {Count} expired consent requests.", expiredRequests.Count);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Error while cancelling expired consent requests.");
        }
    }
}
