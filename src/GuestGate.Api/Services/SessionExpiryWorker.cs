using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using GuestGate.Api.Data;
using GuestGate.Api.Hubs;
using GuestGate.Api.Models;
using Microsoft.AspNetCore.SignalR;

namespace GuestGate.Api.Services
{
    public sealed class SessionExpiryWorker(
        IServiceScopeFactory scopeFactory,
        IHubContext<GuestHub> hub,
        ILogger<SessionExpiryWorker> logger
    ) : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory = scopeFactory;
        private readonly IHubContext<GuestHub> _hub = hub;
        private readonly ILogger<SessionExpiryWorker> _logger = logger;

        private static readonly TimeSpan Interval = TimeSpan.FromSeconds(600);

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            using var timer = new PeriodicTimer(Interval);
            _logger.LogInformation("SessionExpiryWorker started. Interval: {Interval}", Interval);

            try
            {
                while (await timer.WaitForNextTickAsync(stoppingToken))
                {
                    try
                    {
                        using var scope = _scopeFactory.CreateScope();
                        var db = scope.ServiceProvider.GetRequiredService<AppDb>();
                        var now = DateTime.UtcNow;

                        var expiredSessions = await db.KioskSessions
                            .Where(x => x.Status == SessionStatus.Active && x.ExpiresAt <= now)
                            .OrderBy(x => x.Id)
                            .ToListAsync(stoppingToken);

                        foreach (var session in expiredSessions)
                        {
                            session.Status = SessionStatus.Expired;
                            session.UpdatedAt = now;
                        }

                        if (expiredSessions.Count > 0)
                        {
                            await db.SaveChangesAsync(stoppingToken);

                            foreach (var session in expiredSessions)
                            {
                                await _hub.Clients.Group(GuestHub.KioskGroup(session.Kid)).SendAsync("sessionEnded", new
                                {
                                    kid = session.Kid,
                                    sessionId = session.Id,
                                    reason = "expired"
                                }, stoppingToken);
                            }
                        }

                        _logger.LogInformation("Expired {Count} sessions.", expiredSessions.Count);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        _logger.LogError(ex, "Error while expiring sessions.");
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                _logger.LogInformation("SessionExpiryWorker cancellation requested.");
            }

            _logger.LogInformation("SessionExpiryWorker stopped.");
        }
    }
}
