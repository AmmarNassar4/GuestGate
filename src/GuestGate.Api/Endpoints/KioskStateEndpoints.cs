using GuestGate.Api.Data;
using GuestGate.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace GuestGate.Api.Endpoints;

internal static class KioskStateEndpoints
{
    public static IEndpointRouteBuilder MapKioskStateEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/kiosk/state", async Task<IResult> (int kid, AppDb db, IOptions<KioskOptions> opt) =>
        {
            if (kid <= 0) return Results.BadRequest(new { error = "kid must be a positive integer" });

            var consent = await db.ConsentRequests
                .Where(x => x.Kid == kid && (x.Status == ConsentStatus.Waiting || x.Status == ConsentStatus.Assigned))
                .OrderBy(x => x.Id)
                .FirstOrDefaultAsync();

            if (consent is not null)
            {
                if (consent.Status == ConsentStatus.Waiting)
                {
                    consent.Status = ConsentStatus.Assigned;
                    consent.UpdatedAt = DateTime.UtcNow;
                    await db.SaveChangesAsync();
                }

                return Results.Ok(new
                {
                    hasWork = true,
                    nextPollMs = 1000,
                    consent = ToConsentDto(consent),
                    session = (object?)null
                });
            }

            var now = DateTime.UtcNow;
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
                    nextPollMs = 5000,
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
                    nextPollMs = 5000,
                    consent = (object?)null,
                    session = (object?)null
                });
            }

            var scanUrl = BuildScanUrl(opt.Value.MobileBaseUrl, session.EditToken, session.Kid);
            return Results.Ok(new
            {
                hasWork = true,
                nextPollMs = 3000,
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

    private static object ToConsentDto(ConsentRequest request)
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
            pdfPath = request.PdfPath
        };
    }

    private static string BuildScanUrl(string mobileBaseUrl, Guid editToken, int kid)
    {
        var separator = mobileBaseUrl.Contains('?') ? "&" : "?";
        return $"{mobileBaseUrl}{separator}et={editToken}&kid={kid}";
    }
}
