using GuestGate.Api.Data;
using GuestGate.Api.Hubs;
using GuestGate.Api.Models;
using GuestGate.Api.Services;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Data;
using System.Text.Json;

namespace GuestGate.Api.Endpoints;

internal static class SessionManagementEndpoints
{
    public static IEndpointRouteBuilder MapSessionManagementEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api");

        api.MapPost("/sessions/start", async Task<IResult> (HttpRequest req, int? kid, string? templateId, AppDb db, IOptions<KioskOptions> opt, IHubContext<GuestHub> hub, CancellationToken ct) =>
        {
            SessionStartDto? body = null;
            try { body = await req.ReadFromJsonAsync<SessionStartDto>(); } catch { }

            var kioskId = body is not null && body.kid > 0 ? body.kid : kid.GetValueOrDefault();
            var tpl = (body?.templateId ?? templateId)?.Trim();
            var prefillJson = body?.prefill is JsonElement p ? p.GetRawText() : null;

            if (kioskId <= 0) return Results.BadRequest(new { error = "kid must be a positive integer" });

            if (!string.IsNullOrWhiteSpace(tpl))
            {
                var exists = await db.Templates.AsNoTracking().AnyAsync(x => x.Id == tpl);
                if (!exists) return Results.NotFound(new { error = $"Template '{tpl}' not found" });
            }

            var now = DateTime.UtcNow;
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);

            var activeSessions = await db.KioskSessions
                .Where(s => s.Kid == kioskId && s.Status == SessionStatus.Active)
                .OrderByDescending(s => s.Id)
                .ToListAsync(ct);

            foreach (var session in activeSessions)
            {
                session.Status = SessionStatus.Cancelled;
                session.UpdatedAt = now;
            }

            var activeConsents = await db.ConsentRequests
                .Where(x => x.Kid == kioskId && (x.Status == ConsentStatus.Waiting || x.Status == ConsentStatus.Assigned))
                .OrderByDescending(x => x.Id)
                .ToListAsync(ct);

            ConsentRequestMaintenance.MarkCancelled(activeConsents, now);

            var active = new KioskSession
            {
                Kid = kioskId,
                EditToken = Guid.NewGuid(),
                Status = SessionStatus.Active,
                ExpiresAt = now.AddMinutes(opt.Value.SessionMinutes),
                TemplateId = tpl,
                PrefillJson = prefillJson,
                CreatedAt = now,
                UpdatedAt = now
            };

            db.KioskSessions.Add(active);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            foreach (var session in activeSessions)
            {
                await NotifySessionEndedAsync(hub, kioskId, session.Id, "cancelled");
            }

            foreach (var consent in activeConsents)
            {
                await ConsentRequestMaintenance.NotifyConsentChangedAsync(hub, consent, ct);
            }

            var scanUrl = BuildScanUrl(opt.Value.MobileBaseUrl, active.EditToken, kioskId);
            await NotifySessionStartedAsync(hub, active, kioskId, scanUrl);
            return Results.Ok(new { sessionId = active.Id, kid = kioskId, et = active.EditToken, scanUrl, expiresAt = active.ExpiresAt });
        });

        api.MapGet("/sessions/active", async Task<IResult> (int kid, AppDb db, IOptions<KioskOptions> opt, IHubContext<GuestHub> hub) =>
        {
            if (kid <= 0) return Results.BadRequest(new { error = "kid must be a positive integer" });

            var now = DateTime.UtcNow;
            var s = await db.KioskSessions
                .Where(x => x.Kid == kid && x.Status == SessionStatus.Active)
                .OrderByDescending(x => x.Id)
                .FirstOrDefaultAsync();

            if (s is null) return Results.NoContent();
            if (s.ExpiresAt <= now)
            {
                s.Status = SessionStatus.Expired;
                s.UpdatedAt = now;
                await db.SaveChangesAsync();
                await NotifySessionEndedAsync(hub, kid, s.Id, "expired");
                return Results.NoContent();
            }

            var scanUrl = BuildScanUrl(opt.Value.MobileBaseUrl, s.EditToken, s.Kid);
            return Results.Ok(new { sessionId = s.Id, kid = s.Kid, et = s.EditToken, scanUrl, expiresAt = s.ExpiresAt });
        });

        api.MapDelete("/sessions/active", async Task<IResult> (int kid, AppDb db, IHubContext<GuestHub> hub) =>
        {
            if (kid <= 0) return Results.BadRequest(new { error = "kid must be a positive integer" });

            var s = await db.KioskSessions
                .Where(x => x.Kid == kid && x.Status == SessionStatus.Active)
                .OrderByDescending(x => x.Id)
                .FirstOrDefaultAsync();

            if (s is null) return Results.NoContent();
            s.Status = SessionStatus.Cancelled;
            s.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
            await NotifySessionEndedAsync(hub, kid, s.Id, "cancelled");
            return Results.NoContent();
        });

        app.MapPost("/api/sessions/cancel", async Task<IResult> (AppDb db, int kid, IHubContext<GuestHub> hub) =>
        {
            if (kid <= 0) return Results.BadRequest(new { error = "kid must be a positive integer" });

            var s = await db.KioskSessions
                .Where(x => x.Kid == kid && x.Status == SessionStatus.Active)
                .OrderByDescending(x => x.Id)
                .FirstOrDefaultAsync();

            if (s is null) return Results.NotFound(new { error = "No active session to cancel" });
            s.Status = SessionStatus.Cancelled;
            s.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
            await NotifySessionEndedAsync(hub, kid, s.Id, "cancelled");
            return Results.NoContent();
        });

        api.MapPut("/sessions/prefill", async Task<IResult> (Guid et, HttpRequest req, AppDb db) =>
        {
            var s = await db.KioskSessions.FirstOrDefaultAsync(x => x.EditToken == et && x.Status == SessionStatus.Active);
            if (s is null) return Results.NotFound(new { error = "Active session not found" });
            using var doc = await JsonDocument.ParseAsync(req.Body);
            s.PrefillJson = doc.RootElement.GetRawText();
            s.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
            return Results.Ok(new { ok = true });
        });

        return app;
    }

    private static string BuildScanUrl(string mobileBaseUrl, Guid editToken, int kid)
    {
        var separator = mobileBaseUrl.Contains('?') ? "&" : "?";
        return $"{mobileBaseUrl}{separator}et={editToken}&kid={kid}";
    }

    private static Task NotifySessionStartedAsync(IHubContext<GuestHub> hub, KioskSession session, int kid, string scanUrl)
    {
        return hub.Clients.Group(GuestHub.KioskGroup(kid)).SendAsync("sessionStarted", new
        {
            sessionId = session.Id,
            kid,
            et = session.EditToken,
            scanUrl,
            expiresAt = session.ExpiresAt,
            templateId = session.TemplateId
        });
    }

    private static Task NotifySessionEndedAsync(IHubContext<GuestHub> hub, int kid, int sessionId, string reason)
    {
        return hub.Clients.Group(GuestHub.KioskGroup(kid)).SendAsync("sessionEnded", new { kid, sessionId, reason });
    }
}
