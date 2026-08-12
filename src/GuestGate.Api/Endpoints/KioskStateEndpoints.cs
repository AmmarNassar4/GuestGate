using GuestGate.Api.Data;
using GuestGate.Api.Hubs;
using GuestGate.Api.Models;
using GuestGate.Api.Services;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace GuestGate.Api.Endpoints;

internal static class KioskStateEndpoints
{
    public static IEndpointRouteBuilder MapKioskStateEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/kiosk/state", async Task<IResult> (int kid, AppDb db, IOptions<KioskOptions> opt, IOptions<ConsentRequestOptions> consentOptions, IHubContext<GuestHub> hub, CancellationToken ct) =>
        {
            if (kid <= 0) return Results.BadRequest(new { error = "kid must be a positive integer" });

            var options = opt.Value;
            var activeLifetime = consentOptions.Value.ActiveLifetime;
            var idlePollMs = Math.Clamp(options.IdlePollMs, 2000, 60000);
            var activePollMs = Math.Clamp(options.ActivePollMs, 1000, 30000);
            var consentPollMs = Math.Clamp(options.ConsentPollMs, 500, 10000);

            var now = DateTime.UtcNow;
            var cancelledConsents = await ConsentRequestMaintenance.CancelExpiredActiveRequestsAsync(db, now, activeLifetime, kid, ct);

            var activeConsents = await db.ConsentRequests
                .AsNoTracking()
                .Where(x => x.Kid == kid && (x.Status == ConsentStatus.Waiting || x.Status == ConsentStatus.Assigned))
                .OrderByDescending(x => x.Id)
                .ToListAsync(ct);

            var consent = activeConsents.FirstOrDefault();
            var supersededConsents = activeConsents.Skip(1).ToList();
            if (supersededConsents.Count > 0)
            {
                var supersededIds = supersededConsents.Select(x => x.Id).ToList();
                await db.ConsentRequests
                    .Where(x => supersededIds.Contains(x.Id) && (x.Status == ConsentStatus.Waiting || x.Status == ConsentStatus.Assigned))
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(x => x.Status, ConsentStatus.Cancelled)
                        .SetProperty(x => x.UpdatedAt, now), ct);
                cancelledConsents.AddRange(supersededConsents.Select(x => new CancelledConsent(x.Id, x.Kid, x.PdfPath)));
            }

            if (consent is not null && consent.Status == ConsentStatus.Waiting)
            {
                // Conditional claim: only flips waiting -> assigned; a concurrent poll
                // that already claimed or cancelled the row makes this a no-op.
                await db.ConsentRequests
                    .Where(x => x.Id == consent.Id && x.Status == ConsentStatus.Waiting)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(x => x.Status, ConsentStatus.Assigned)
                        .SetProperty(x => x.UpdatedAt, now), ct);
                consent.Status = ConsentStatus.Assigned;
            }

            await ConsentRequestMaintenance.NotifyCancelledAsync(hub, cancelledConsents, ct);

            if (consent is not null)
            {
                return Results.Ok(new
                {
                    hasWork = true,
                    nextPollMs = consentPollMs,
                    consent = ToConsentDto(consent, activeLifetime),
                    session = (object?)null
                });
            }

            var session = await db.KioskSessions
                .AsNoTracking()
                .Where(x => x.Kid == kid && x.Status == SessionStatus.Active)
                .OrderByDescending(x => x.Id)
                .Select(x => new
                {
                    x.Id,
                    x.Kid,
                    x.EditToken,
                    x.ExpiresAt,
                    x.TemplateId
                })
                .FirstOrDefaultAsync();

            if (session is null)
            {
                return Results.Ok(new
                {
                    hasWork = false,
                    nextPollMs = idlePollMs,
                    consent = (object?)null,
                    session = (object?)null
                });
            }

            if (session.ExpiresAt <= now)
            {
                await db.Database.ExecuteSqlInterpolatedAsync($@"
                    UPDATE dbo.KioskSessions
                       SET Status = {(byte)SessionStatus.Expired}, UpdatedAt = SYSUTCDATETIME()
                     WHERE Id = {session.Id} AND Status = {(byte)SessionStatus.Active};", ct);

                await NotifySessionEndedAsync(hub, session.Kid, session.Id, "expired", ct);

                return Results.Ok(new
                {
                    hasWork = false,
                    nextPollMs = idlePollMs,
                    consent = (object?)null,
                    session = (object?)null
                });
            }

            var scanUrl = BuildScanUrl(options.MobileBaseUrl, session.EditToken, session.Kid);
            return Results.Ok(new
            {
                hasWork = true,
                nextPollMs = activePollMs,
                consent = (object?)null,
                session = new
                {
                    sessionId = session.Id,
                    kid = session.Kid,
                    et = session.EditToken,
                    scanUrl,
                    expiresAt = session.ExpiresAt,
                    templateId = session.TemplateId
                }
            });
        });

        return app;
    }

    private static object ToConsentDto(ConsentRequest request, TimeSpan activeLifetime)
    {
        return new
        {
            id = request.Id,
            kid = request.Kid,
            guestName = request.GuestName,
            identityNumber = request.IdentityNumber,
            checkInTime = request.CheckInTime,
            language = request.Language,
            termsEn = request.TermsEn,
            termsAr = request.TermsAr,
            status = request.Status.ToString(),
            accepted = request.Accepted,
            signedAt = request.SignedAt,
            expiresAt = ConsentRequestMaintenance.GetExpiresAtUtc(request, activeLifetime),
            pdfPath = request.PdfPath
        };
    }

    private static string BuildScanUrl(string mobileBaseUrl, Guid editToken, int kid)
    {
        var separator = mobileBaseUrl.Contains('?') ? "&" : "?";
        return $"{mobileBaseUrl}{separator}et={editToken}&kid={kid}";
    }

    private static Task NotifySessionEndedAsync(IHubContext<GuestHub> hub, int kid, int sessionId, string reason, CancellationToken cancellationToken)
    {
        return hub.Clients.Group(GuestHub.KioskGroup(kid)).SendAsync("sessionEnded", new { kid, sessionId, reason }, cancellationToken);
    }
}
