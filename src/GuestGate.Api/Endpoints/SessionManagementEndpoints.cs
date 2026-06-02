using GuestGate.Api.Data;
using GuestGate.Api.Hubs;
using GuestGate.Api.Models;
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

        api.MapPost("/sessions/start", async Task<IResult> (HttpRequest req, string? kid, string? templateId, AppDb db, IOptions<KioskOptions> opt, IHubContext<GuestHub> hub) =>
        {
            SessionStartDto? body = null;
            try { body = await req.ReadFromJsonAsync<SessionStartDto>(); } catch { }

            var normalizedKid = GuestHub.NormalizeKid(body?.kid ?? kid);
            var tpl = (body?.templateId ?? templateId)?.Trim();
            var prefillJson = body?.prefill is JsonElement p ? p.GetRawText() : null;

            if (string.IsNullOrWhiteSpace(normalizedKid)) return Results.BadRequest(new { error = "kid is required" });

            if (!string.IsNullOrWhiteSpace(tpl))
            {
                var exists = await db.Templates.AsNoTracking().AnyAsync(x => x.Id == tpl);
                if (!exists) return Results.NotFound(new { error = $"Template '{tpl}' not found" });
            }

            var now = DateTime.UtcNow;
            var active = await db.KioskSessions
                .Where(s => s.Kid == normalizedKid && s.Status == SessionStatus.Active)
                .OrderByDescending(s => s.Id)
                .FirstOrDefaultAsync();

            if (active is not null && active.ExpiresAt <= now)
            {
                active.Status = SessionStatus.Expired;
                active.UpdatedAt = now;
                active = null;
            }

            if (active is null)
            {
                active = new KioskSession
                {
                    Kid = normalizedKid,
                    EditToken = Guid.NewGuid(),
                    Status = SessionStatus.Active,
                    ExpiresAt = now.AddMinutes(opt.Value.SessionMinutes),
                    TemplateId = tpl,
                    PrefillJson = prefillJson,
                    CreatedAt = now,
                    UpdatedAt = now
                };
                db.KioskSessions.Add(active);
            }
            else
            {
                active.Kid = GuestHub.NormalizeKid(active.Kid);
                if (!string.IsNullOrWhiteSpace(tpl)) active.TemplateId = tpl;
                if (!string.IsNullOrWhiteSpace(prefillJson)) active.PrefillJson = prefillJson;
                active.UpdatedAt = now;
            }

            await db.SaveChangesAsync();
            var scanUrl = BuildScanUrl(opt.Value.MobileBaseUrl, active.EditToken, normalizedKid);
            await NotifySessionStartedAsync(hub, active, normalizedKid, scanUrl);
            return Results.Ok(new { sessionId = active.Id, kid = normalizedKid, et = active.EditToken, scanUrl, expiresAt = active.ExpiresAt });
        });

        api.MapGet("/sessions/active", async Task<IResult> (string kid, AppDb db, IOptions<KioskOptions> opt) =>
        {
            var normalizedKid = GuestHub.NormalizeKid(kid);
            if (string.IsNullOrWhiteSpace(normalizedKid)) return Results.BadRequest(new { error = "kid is required" });

            var now = DateTime.UtcNow;
            var s = await db.KioskSessions
                .Where(x => x.Kid == normalizedKid && x.Status == SessionStatus.Active)
                .OrderByDescending(x => x.Id)
                .FirstOrDefaultAsync();

            if (s is null) return Results.NoContent();
            if (s.ExpiresAt <= now)
            {
                s.Status = SessionStatus.Expired;
                s.UpdatedAt = now;
                await db.SaveChangesAsync();
                return Results.NoContent();
            }

            var responseKid = GuestHub.NormalizeKid(s.Kid);
            var scanUrl = BuildScanUrl(opt.Value.MobileBaseUrl, s.EditToken, responseKid);
            return Results.Ok(new { sessionId = s.Id, kid = responseKid, et = s.EditToken, scanUrl, expiresAt = s.ExpiresAt });
        });

        api.MapDelete("/sessions/active", async Task<IResult> (string kid, AppDb db, IHubContext<GuestHub> hub) =>
        {
            var normalizedKid = GuestHub.NormalizeKid(kid);
            if (string.IsNullOrWhiteSpace(normalizedKid)) return Results.BadRequest(new { error = "kid is required" });

            var s = await db.KioskSessions
                .Where(x => x.Kid == normalizedKid && x.Status == SessionStatus.Active)
                .OrderByDescending(x => x.Id)
                .FirstOrDefaultAsync();

            if (s is null) return Results.NoContent();
            s.Status = SessionStatus.Cancelled;
            s.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
            await NotifySessionEndedAsync(hub, normalizedKid, s.Id, "cancelled");
            return Results.NoContent();
        });

        app.MapPost("/api/sessions/cancel", async Task<IResult> (AppDb db, string kid, IHubContext<GuestHub> hub) =>
        {
            var normalizedKid = GuestHub.NormalizeKid(kid);
            if (string.IsNullOrWhiteSpace(normalizedKid)) return Results.BadRequest(new { error = "kid is required" });

            var s = await db.KioskSessions
                .Where(x => x.Kid == normalizedKid && x.Status == SessionStatus.Active)
                .OrderByDescending(x => x.Id)
                .FirstOrDefaultAsync();

            if (s is null) return Results.NotFound(new { error = "No active session to cancel" });
            s.Status = SessionStatus.Cancelled;
            s.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
            await NotifySessionEndedAsync(hub, normalizedKid, s.Id, "cancelled");
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

    private static string BuildScanUrl(string mobileBaseUrl, Guid editToken, string kid)
    {
        var separator = mobileBaseUrl.Contains('?') ? "&" : "?";
        return $"{mobileBaseUrl}{separator}et={editToken}&kid={Uri.EscapeDataString(GuestHub.NormalizeKid(kid))}";
    }

    private static Task NotifySessionStartedAsync(IHubContext<GuestHub> hub, KioskSession session, string kid, string scanUrl)
    {
        var normalizedKid = GuestHub.NormalizeKid(kid);
        return hub.Clients.Group(GuestHub.KioskGroup(normalizedKid)).SendAsync("sessionStarted", new
        {
            sessionId = session.Id,
            kid = normalizedKid,
            et = session.EditToken,
            scanUrl,
            expiresAt = session.ExpiresAt,
            templateId = session.TemplateId
        });
    }

    private static Task NotifySessionEndedAsync(IHubContext<GuestHub> hub, string kid, int sessionId, string reason)
    {
        var normalizedKid = GuestHub.NormalizeKid(kid);
        return hub.Clients.Group(GuestHub.KioskGroup(normalizedKid)).SendAsync("sessionEnded", new { kid = normalizedKid, sessionId, reason });
    }
}
