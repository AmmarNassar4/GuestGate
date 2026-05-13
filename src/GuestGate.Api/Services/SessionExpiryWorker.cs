
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using GuestGate.Api.Data;

using StatusEnum = GuestGate.Api.Models.SessionStatus;

namespace GuestGate.Api.Services
{
  
    public sealed class SessionExpiryWorker(
        IServiceScopeFactory scopeFactory,
        ILogger<SessionExpiryWorker> logger
    ) : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory = scopeFactory;
        private readonly ILogger<SessionExpiryWorker> _logger = logger;

       
        private static readonly TimeSpan Interval = TimeSpan.FromSeconds(600);

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
           
            using var timer = new PeriodicTimer(Interval);
            _logger.LogInformation("SessionExpiryWorker started. Interval: {Interval}", Interval);

            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<AppDb>();

                    
                    var affected = await db.Database.ExecuteSqlInterpolatedAsync($@"
                        UPDATE dbo.KioskSessions
                           SET Status = {(byte)StatusEnum.Expired}, UpdatedAt = SYSUTCDATETIME()
                         WHERE Status = {(byte)StatusEnum.Active} AND ExpiresAt <= SYSUTCDATETIME();",
                        stoppingToken);

                    _logger.LogInformation("Expired {Count} sessions.", affected);
                }
                catch (OperationCanceledException)
                {
                    
                    _logger.LogInformation("SessionExpiryWorker cancellation requested.");
                    break;
                }
                catch (Exception ex)
                {
                    
                    _logger.LogError(ex, "Error while expiring sessions.");
                }
            }

            _logger.LogInformation("SessionExpiryWorker stopped.");
        }
    }
}
