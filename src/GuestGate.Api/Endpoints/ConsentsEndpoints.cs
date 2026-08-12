using GuestGate.Api.Data;
using GuestGate.Api.Hubs;
using GuestGate.Api.Models;
using GuestGate.Api.Services;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace GuestGate.Api.Endpoints;

internal static class ConsentsEndpoints
{
    public static IEndpointRouteBuilder MapConsentsEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api");

        api.MapPost("/consents", async Task<IResult> (ConsentCreateDto body, AppDb db, IWebHostEnvironment env, IHubContext<GuestHub> hub, IOptions<ConsentRequestOptions> consentOptions, CancellationToken ct) =>
        {
            if (body is null) return Results.BadRequest(new { error = "Invalid payload" });
            if (!TryParseKid(body.kid, out var kid)) return Results.BadRequest(new { error = "kid must be a positive integer" });

            var checkInTime = (body.checkInTime ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(checkInTime)) return Results.BadRequest(new { error = "checkInTime is required" });

            var language = ConsentTermsLoader.NormalizeLanguage(body.language);
            var now = DateTime.UtcNow;
            var activeLifetime = consentOptions.Value.ActiveLifetime;

            // The new consent supersedes everything: cancel all active consents and
            // sessions for this kiosk with atomic set-based updates (no transaction,
            // no range locks — see ConsentRequestMaintenance).
            await db.ConsentRequests
                .Where(x => x.Kid == kid && (x.Status == ConsentStatus.Waiting || x.Status == ConsentStatus.Assigned))
                .ExecuteUpdateAsync(s => s
                    .SetProperty(x => x.Status, ConsentStatus.Cancelled)
                    .SetProperty(x => x.UpdatedAt, now), ct);

            await db.KioskSessions
                .Where(s => s.Kid == kid && s.Status == SessionStatus.Active)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(x => x.Status, SessionStatus.Cancelled)
                    .SetProperty(x => x.UpdatedAt, now), ct);

            var request = new ConsentRequest
            {
                Kid = kid,
                GuestName = (body.guestName ?? string.Empty).Trim(),
                IdentityNumber = (body.identityNumber ?? string.Empty).Trim(),
                CheckInTime = checkInTime,
                Language = language,
                TermsEn = !string.IsNullOrWhiteSpace(body.termsEn)
                    ? body.termsEn.Trim().Replace("<time>", checkInTime, StringComparison.OrdinalIgnoreCase)
                    : await ConsentTermsLoader.LoadAsync(env, "en", checkInTime),
                TermsAr = !string.IsNullOrWhiteSpace(body.termsAr)
                    ? body.termsAr.Trim().Replace("<time>", checkInTime, StringComparison.OrdinalIgnoreCase)
                    : await ConsentTermsLoader.LoadAsync(env, "ar", checkInTime),
                Status = ConsentStatus.Waiting,
                CreatedAt = now,
                UpdatedAt = now
            };

            db.ConsentRequests.Add(request);
            await db.SaveChangesAsync(ct);

            await ConsentRequestMaintenance.NotifyConsentChangedAsync(hub, request.Kid, request.Id, ConsentStatus.Waiting, cancellationToken: ct);
            return Results.Ok(ToConsentDto(request, activeLifetime));
        });

        api.MapGet("/consents/{id:int}", async Task<IResult> (int id, AppDb db, IOptions<ConsentRequestOptions> consentOptions) =>
        {
            var request = await db.ConsentRequests.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
            if (request is null) return Results.NotFound(new { error = "Consent request not found" });
            return Results.Ok(ToConsentDto(request, consentOptions.Value.ActiveLifetime));
        });

        api.MapGet("/consents/{id:int}/signature", async Task<IResult> (int id, AppDb db) =>
        {
            var request = await db.ConsentRequests.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
            if (request is null) return Results.NotFound(new { error = "Consent request not found" });

            if (request.Status != ConsentStatus.Signed || string.IsNullOrWhiteSpace(request.SignatureImageDataUrl))
            {
                return Results.NotFound(new { error = "Signature is not available yet" });
            }

            return Results.Ok(new
            {
                id = request.Id,
                status = request.Status.ToString(),
                pdfPath = request.PdfPath,
                signatureImage = request.SignatureImageDataUrl,
                signedAt = request.SignedAt
            });
        });

        api.MapGet("/consents/active", async Task<IResult> (string kid, AppDb db, IHubContext<GuestHub> hub, IOptions<ConsentRequestOptions> consentOptions, CancellationToken ct) =>
        {
            if (!TryParseKid(kid, out var parsedKid)) return Results.BadRequest(new { error = "kid must be a positive integer" });

            var now = DateTime.UtcNow;
            var activeLifetime = consentOptions.Value.ActiveLifetime;
            var cancelledConsents = await ConsentRequestMaintenance.CancelExpiredActiveRequestsAsync(db, now, activeLifetime, parsedKid, ct);

            var activeRequests = await db.ConsentRequests
                .AsNoTracking()
                .Where(x => x.Kid == parsedKid && (x.Status == ConsentStatus.Waiting || x.Status == ConsentStatus.Assigned))
                .OrderByDescending(x => x.Id)
                .ToListAsync(ct);

            var request = activeRequests.FirstOrDefault();
            var supersededRequests = activeRequests.Skip(1).ToList();
            if (supersededRequests.Count > 0)
            {
                var supersededIds = supersededRequests.Select(x => x.Id).ToList();
                await db.ConsentRequests
                    .Where(x => supersededIds.Contains(x.Id) && (x.Status == ConsentStatus.Waiting || x.Status == ConsentStatus.Assigned))
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(x => x.Status, ConsentStatus.Cancelled)
                        .SetProperty(x => x.UpdatedAt, now), ct);
                cancelledConsents.AddRange(supersededRequests.Select(x => new CancelledConsent(x.Id, x.Kid, x.PdfPath)));
            }

            if (request is not null && request.Status == ConsentStatus.Waiting)
            {
                await db.ConsentRequests
                    .Where(x => x.Id == request.Id && x.Status == ConsentStatus.Waiting)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(x => x.Status, ConsentStatus.Assigned)
                        .SetProperty(x => x.UpdatedAt, now), ct);
                request.Status = ConsentStatus.Assigned;
            }

            await ConsentRequestMaintenance.NotifyCancelledAsync(hub, cancelledConsents, ct);

            if (request is null) return Results.NoContent();

            return Results.Ok(ToConsentDto(request, activeLifetime));
        });

        api.MapDelete("/consents/active", CancelActiveConsentsForKidAsync);
        app.MapPost("/api/consents/cancel", async Task<IResult> (string kid, bool? force, AppDb db, IHubContext<GuestHub> hub, ILoggerFactory loggerFactory, CancellationToken ct) =>
        {
            if (force != true)
            {
                loggerFactory
                    .CreateLogger("GuestGate.Api.Consents")
                    .LogWarning("Ignored legacy POST consent cancel request for kiosk {Kid}. Use DELETE /api/consents/active or append force=true for an explicit cancel.", kid);

                return Results.Ok(new
                {
                    ok = true,
                    skipped = true,
                    cancelledCount = 0,
                    reason = "POST /api/consents/cancel requires force=true. Use DELETE /api/consents/active for explicit cancellation."
                });
            }

            return await CancelActiveConsentsForKidAsync(kid, db, hub, loggerFactory, ct);
        });

        api.MapPost("/consents/{id:int}/sign", async Task<IResult> (int id, ConsentSignDto body, AppDb db, IConsentPdfWriter pdfWriter, IHubContext<GuestHub> hub, IOptions<ConsentRequestOptions> consentOptions, CancellationToken ct) =>
        {
            if (body is null) return Results.BadRequest(new { error = "Invalid payload" });
            if (!body.accepted) return Results.BadRequest(new { error = "The terms must be accepted before signing." });
            if (string.IsNullOrWhiteSpace(body.signatureImage)) return Results.BadRequest(new { error = "signatureImage is required" });

            var request = await db.ConsentRequests.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (request is null) return Results.NotFound(new { error = "Consent request not found" });
            if (request.Status == ConsentStatus.Signed) return Results.Conflict(new { error = "Consent request is already signed", pdfPath = request.PdfPath });
            if (ConsentRequestMaintenance.IsExpired(request, DateTime.UtcNow, consentOptions.Value.ActiveLifetime))
            {
                request.Status = ConsentStatus.Cancelled;
                request.UpdatedAt = DateTime.UtcNow;
                await db.SaveChangesAsync(ct);
                await ConsentRequestMaintenance.NotifyConsentChangedAsync(hub, request, ct);
                return Results.Conflict(new { error = "Consent request expired" });
            }

            if (!ConsentRequestMaintenance.IsActive(request.Status))
            {
                return Results.Conflict(new { error = "Consent request is no longer active" });
            }

            request.Accepted = true;
            request.Language = ConsentTermsLoader.NormalizeLanguage(body.language ?? request.Language);
            request.SignatureImageDataUrl = body.signatureImage;
            request.SignedAt = DateTime.UtcNow;
            request.Status = ConsentStatus.Signed;
            request.PdfPath = await pdfWriter.WriteAsync(request, ct);
            await db.SaveChangesAsync(ct);

            await ConsentRequestMaintenance.NotifyConsentChangedAsync(hub, request.Kid, request.Id, ConsentStatus.Signed, request.PdfPath, ct);
            return Results.Ok(new { ok = true, id = request.Id, pdfPath = request.PdfPath });
        });

        return app;
    }

    private static object ToConsentDto(ConsentRequest request, TimeSpan activeLifetime)
    {
        return new
        {
            id = request.Id,
            kid = request.Kid.ToString(),
            guestName = request.GuestName,
            identityNumber = request.IdentityNumber,
            checkInTime = request.CheckInTime,
            language = ConsentTermsLoader.NormalizeLanguage(request.Language),
            termsEn = request.TermsEn,
            termsAr = request.TermsAr,
            status = request.Status.ToString(),
            accepted = request.Accepted,
            signedAt = request.SignedAt,
            expiresAt = ConsentRequestMaintenance.GetExpiresAtUtc(request, activeLifetime),
            pdfPath = request.PdfPath
        };
    }

    private static async Task<IResult> CancelActiveConsentsForKidAsync(string kid, AppDb db, IHubContext<GuestHub> hub, ILoggerFactory loggerFactory, CancellationToken ct)
    {
        if (!TryParseKid(kid, out var parsedKid)) return Results.BadRequest(new { error = "kid must be a positive integer" });

        var requests = await db.ConsentRequests
            .Where(x => x.Kid == parsedKid && (x.Status == ConsentStatus.Waiting || x.Status == ConsentStatus.Assigned))
            .OrderBy(x => x.Id)
            .ToListAsync(ct);

        if (requests.Count == 0) return Results.NoContent();

        var now = DateTime.UtcNow;
        ConsentRequestMaintenance.MarkCancelled(requests, now);

        loggerFactory
            .CreateLogger("GuestGate.Api.Consents")
            .LogInformation("Cancelling {Count} active consent request(s) for kiosk {Kid}: {ConsentIds}", requests.Count, parsedKid, string.Join(",", requests.Select(x => x.Id)));

        await db.SaveChangesAsync(ct);

        foreach (var request in requests)
        {
            await ConsentRequestMaintenance.NotifyConsentChangedAsync(hub, request, ct);
        }

        return Results.Ok(new { ok = true, cancelledCount = requests.Count });
    }

    private static Task NotifySessionEndedAsync(IHubContext<GuestHub> hub, int kid, int sessionId, string reason, CancellationToken cancellationToken)
    {
        return hub.Clients.Group(GuestHub.KioskGroup(kid)).SendAsync("sessionEnded", new { kid, sessionId, reason }, cancellationToken);
    }

    private static bool TryParseKid(JsonElement value, out int kid)
    {
        kid = 0;

        if (value.ValueKind == JsonValueKind.Number)
        {
            return value.TryGetInt32(out kid) && kid > 0;
        }

        if (value.ValueKind == JsonValueKind.String)
        {
            return TryParseKid(value.GetString(), out kid);
        }

        return false;
    }

    private static bool TryParseKid(string? value, out int kid)
    {
        kid = 0;
        var text = (value ?? string.Empty).Trim();
        if (text.Length == 0) return false;

        if (int.TryParse(text, out kid) && kid > 0) return true;

        if (text.StartsWith("KIOSK-", StringComparison.OrdinalIgnoreCase))
        {
            return int.TryParse(text[6..], out kid) && kid > 0;
        }

        if (text.StartsWith("KIOSK", StringComparison.OrdinalIgnoreCase))
        {
            return int.TryParse(text[5..], out kid) && kid > 0;
        }

        if (text.StartsWith("K", StringComparison.OrdinalIgnoreCase))
        {
            return int.TryParse(text[1..], out kid) && kid > 0;
        }

        return false;
    }
}
