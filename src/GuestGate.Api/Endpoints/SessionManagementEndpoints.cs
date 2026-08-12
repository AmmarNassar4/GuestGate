using GuestGate.Api.Data;
using GuestGate.Api.Hubs;
using GuestGate.Api.Models;
using GuestGate.Api.Services;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
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

            // Atomic set-based supersede: cancel prior sessions and consents without
            // a Serializable transaction (the old pattern deadlocked under load).
            await db.KioskSessions
                .Where(s => s.Kid == kioskId && s.Status == SessionStatus.Active)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(x => x.Status, SessionStatus.Cancelled)
                    .SetProperty(x => x.UpdatedAt, now), ct);

            await db.ConsentRequests
                .Where(x => x.Kid == kioskId && (x.Status == ConsentStatus.Waiting || x.Status == ConsentStatus.Assigned))
                .ExecuteUpdateAsync(s => s
                    .SetProperty(x => x.Status, ConsentStatus.Cancelled)
                    .SetProperty(x => x.UpdatedAt, now), ct);

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

        // Cancel = purge: hard-deletes the active session row(s) and any guest data
        // linked to them, then broadcasts sessionEnded so kiosks reset immediately.
        api.MapDelete("/sessions/active", async Task<IResult> (int kid, AppDb db, IHubContext<GuestHub> hub, CancellationToken ct) =>
        {
            if (kid <= 0) return Results.BadRequest(new { error = "kid must be a positive integer" });

            await PurgeActiveSessionsAsync(db, kid, hub, ct);
            return Results.NoContent();
        });

        app.MapPost("/api/sessions/cancel", async Task<IResult> (AppDb db, int kid, IHubContext<GuestHub> hub, CancellationToken ct) =>
        {
            if (kid <= 0) return Results.BadRequest(new { error = "kid must be a positive integer" });

            var purged = await PurgeActiveSessionsAsync(db, kid, hub, ct);
            if (purged == 0) return Results.NotFound(new { error = "No active session to cancel" });
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

    internal static async Task<int> PurgeActiveSessionsAsync(AppDb db, int kid, IHubContext<GuestHub> hub, CancellationToken ct)
    {
        var sessions = await db.KioskSessions
            .AsNoTracking()
            .Where(x => x.Kid == kid && x.Status == SessionStatus.Active)
            .Select(x => new { x.Id, x.GuestId })
            .ToListAsync(ct);

        if (sessions.Count == 0) return 0;

        var sessionIds = sessions.Select(x => x.Id).ToList();
        await db.KioskSessions
            .Where(x => sessionIds.Contains(x.Id))
            .ExecuteDeleteAsync(ct);

        var guestIds = sessions.Where(x => x.GuestId.HasValue).Select(x => x.GuestId!.Value).ToList();
        if (guestIds.Count > 0)
        {
            await db.Guests
                .Where(g => guestIds.Contains(g.Id))
                .ExecuteDeleteAsync(ct);
        }

        foreach (var id in sessionIds)
        {
            await NotifySessionEndedAsync(hub, kid, id, "cancelled");
        }

        return sessions.Count;
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
