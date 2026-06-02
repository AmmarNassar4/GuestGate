using GuestGate.Api.Data;
using GuestGate.Api.Hubs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace GuestGate.Api.Services
{
    public class ConsentWatcher : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IHubContext<GuestHub> _hub;
        private readonly ILogger<ConsentWatcher> _logger;
        private DateTime _lastSeenUtc = DateTime.MinValue;
        private bool _firstPoll = true;

        public ConsentWatcher(IServiceScopeFactory scopeFactory, IHubContext<GuestHub> hub, ILogger<ConsentWatcher> logger)
        {
            _scopeFactory = scopeFactory;
            _hub = hub;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                while (!stoppingToken.IsCancellationRequested)
                {
                    try
                    {
                        using var scope = _scopeFactory.CreateScope();
                        var db = scope.ServiceProvider.GetRequiredService<AppDb>();
                        var changed = await db.ConsentRequests.AsNoTracking()
                            .Where(x => x.UpdatedAt > _lastSeenUtc)
                            .OrderBy(x => x.UpdatedAt)
                            .Select(x => new { x.Id, x.Kid, x.Status, x.UpdatedAt })
                            .ToListAsync(stoppingToken);

                        if (_firstPoll && changed.Count == 0)
                        {
                            // No consents yet; set watermark to current time so startup does not re-broadcast older rows forever.
                            _lastSeenUtc = DateTime.UtcNow;
                        }

                        foreach (var item in changed)
                        {
                            await _hub.Clients.Group(GuestHub.KioskGroup(item.Kid)).SendAsync("consentChanged", new
                            {
                                kid = GuestHub.NormalizeKid(item.Kid),
                                consentId = item.Id,
                                status = item.Status.ToString()
                            }, stoppingToken);
                            if (item.UpdatedAt > _lastSeenUtc) _lastSeenUtc = item.UpdatedAt;
                        }

                        _firstPoll = false;
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        _logger.LogWarning(ex, "ConsentWatcher polling failed.");
                    }

                    await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                _logger.LogInformation("ConsentWatcher cancellation requested.");
            }
        }
    }
}
