using GuestGate.Api.Data;
using GuestGate.Api.Hubs;
using GuestGate.Api.Models;
using GuestGate.Api.Services;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Data;

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

            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var now = DateTime.UtcNow;
            var cancelledRequests = await ConsentRequestMaintenance.MarkExpiredActiveRequestsAsync(db, now, activeLifetime, kid, ct);

            var activeConsents = (await db.ConsentRequests
                .Where(x => x.Kid == kid && (x.Status == ConsentStatus.Waiting || x.Status == ConsentStatus.Assigned))
                .OrderByDescending(x => x.Id)
                .ToListAsync(ct))
                .Where(x => ConsentRequestMaintenance.IsActive(x.Status))
                .ToList();

            var consent = activeConsents.FirstOrDefault();
            var supersededConsents = activeConsents.Skip(1).ToList();
            ConsentRequestMaintenance.MarkCancelled(supersededConsents, now);
            cancelledRequests.AddRange(supersededConsents);

            if (consent is not null)
            {
                if (consent.Status == ConsentStatus.Waiting)
                {
                    consent.Status = ConsentStatus.Assigned;
                    consent.UpdatedAt = now;
                }

                if (db.ChangeTracker.HasChanges()) await db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);

                foreach (var cancelled in cancelledRequests)
                {
                    await ConsentRequestMaintenance.NotifyConsentChangedAsync(hub, cancelled, ct);
                }

                return Results.Ok(new
                {
                    hasWork = true,
                    nextPollMs = consentPollMs,
                    consent = ToConsentDto(consent, activeLifetime),
                    session = (object?)null
                });
            }

            if (cancelledRequests.Count > 0)
            {
                await db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);

                foreach (var cancelled in cancelledRequests)
                {
                    await ConsentRequestMaintenance.NotifyConsentChangedAsync(hub, cancelled, ct);
                }
            }
            else
            {
                await tx.CommitAsync(ct);
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
                     WHERE Id = {session.Id} AND Status = {(byte)SessionStatus.Active};");

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
}
