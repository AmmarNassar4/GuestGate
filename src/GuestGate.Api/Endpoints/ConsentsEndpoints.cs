using GuestGate.Api.Data;
using GuestGate.Api.Hubs;
using GuestGate.Api.Models;
using GuestGate.Api.Services;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace GuestGate.Api.Endpoints;

internal static class ConsentsEndpoints
{
    public static IEndpointRouteBuilder MapConsentsEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api");

        api.MapPost("/consents", async Task<IResult> (ConsentCreateDto body, AppDb db, IWebHostEnvironment env, IHubContext<GuestHub> hub, CancellationToken ct) =>
        {
            if (body is null) return Results.BadRequest(new { error = "Invalid payload" });
            if (!TryParseKid(body.kid, out var kid)) return Results.BadRequest(new { error = "kid must be a positive integer" });

            var checkInTime = (body.checkInTime ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(checkInTime)) return Results.BadRequest(new { error = "checkInTime is required" });

            var language = ConsentTermsLoader.NormalizeLanguage(body.language);
            var now = DateTime.UtcNow;
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
            await NotifyConsentChangedAsync(hub, request.Kid, request.Id, ConsentStatus.Waiting);
            return Results.Ok(ToConsentDto(request));
        });

        api.MapGet("/consents/{id:int}", async Task<IResult> (int id, AppDb db) =>
        {
            var request = await db.ConsentRequests.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
            if (request is null) return Results.NotFound(new { error = "Consent request not found" });
            return Results.Ok(ToConsentDto(request));
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

        api.MapGet("/consents/active", async Task<IResult> (string kid, AppDb db) =>
        {
            if (!TryParseKid(kid, out var parsedKid)) return Results.BadRequest(new { error = "kid must be a positive integer" });

            var request = await db.ConsentRequests
                .Where(x => x.Kid == parsedKid && (x.Status == ConsentStatus.Waiting || x.Status == ConsentStatus.Assigned))
                .OrderBy(x => x.Id)
                .FirstOrDefaultAsync();

            if (request is null) return Results.NoContent();
            if (request.Status == ConsentStatus.Waiting)
            {
                request.Status = ConsentStatus.Assigned;
                request.UpdatedAt = DateTime.UtcNow;
                await db.SaveChangesAsync();
            }

            return Results.Ok(ToConsentDto(request));
        });

        api.MapDelete("/consents/active", CancelActiveConsentsForKidAsync);
        app.MapPost("/api/consents/cancel", CancelActiveConsentsForKidAsync);

        api.MapPost("/consents/{id:int}/sign", async Task<IResult> (int id, ConsentSignDto body, AppDb db, IConsentPdfWriter pdfWriter, IHubContext<GuestHub> hub, CancellationToken ct) =>
        {
            if (body is null) return Results.BadRequest(new { error = "Invalid payload" });
            if (!body.accepted) return Results.BadRequest(new { error = "The terms must be accepted before signing." });
            if (string.IsNullOrWhiteSpace(body.signatureImage)) return Results.BadRequest(new { error = "signatureImage is required" });

            var request = await db.ConsentRequests.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (request is null) return Results.NotFound(new { error = "Consent request not found" });
            if (request.Status == ConsentStatus.Signed) return Results.Conflict(new { error = "Consent request is already signed", pdfPath = request.PdfPath });

            request.Accepted = true;
            request.Language = ConsentTermsLoader.NormalizeLanguage(body.language ?? request.Language);
            request.SignatureImageDataUrl = body.signatureImage;
            request.SignedAt = DateTime.UtcNow;
            request.Status = ConsentStatus.Signed;
            request.PdfPath = await pdfWriter.WriteAsync(request, ct);
            await db.SaveChangesAsync(ct);

            await NotifyConsentChangedAsync(hub, request.Kid, request.Id, ConsentStatus.Signed, request.PdfPath);
            return Results.Ok(new { ok = true, id = request.Id, pdfPath = request.PdfPath });
        });

        return app;
    }

    private static object ToConsentDto(ConsentRequest request)
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
            pdfPath = request.PdfPath
        };
    }

    private static Task NotifyConsentChangedAsync(IHubContext<GuestHub> hub, int kid, int consentId, ConsentStatus status, string? pdfPath = null)
    {
        return hub.Clients.Group(GuestHub.KioskGroup(kid)).SendAsync("consentChanged", new
        {
            kid = kid.ToString(),
            consentId,
            status = status.ToString(),
            pdfPath
        });
    }

    private static async Task<IResult> CancelActiveConsentsForKidAsync(string kid, AppDb db, IHubContext<GuestHub> hub)
    {
        if (!TryParseKid(kid, out var parsedKid)) return Results.BadRequest(new { error = "kid must be a positive integer" });

        var requests = await db.ConsentRequests
            .Where(x => x.Kid == parsedKid && (x.Status == ConsentStatus.Waiting || x.Status == ConsentStatus.Assigned))
            .OrderBy(x => x.Id)
            .ToListAsync();

        if (requests.Count == 0) return Results.NoContent();

        var now = DateTime.UtcNow;
        foreach (var request in requests)
        {
            request.Status = ConsentStatus.Cancelled;
            request.UpdatedAt = now;
        }

        await db.SaveChangesAsync();

        foreach (var request in requests)
        {
            await NotifyConsentChangedAsync(hub, request.Kid, request.Id, ConsentStatus.Cancelled);
        }

        return Results.Ok(new { ok = true, cancelledCount = requests.Count });
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
