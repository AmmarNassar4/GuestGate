using GuestGate.Api.Data;
using GuestGate.Api.Hubs;
using GuestGate.Api.Models;
using GuestGate.Api.Services;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace GuestGate.Api.Endpoints;

internal static class GuestFlowEndpoints
{
    public static IEndpointRouteBuilder MapGuestFlowEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api");

        app.MapGet("/api/mobile/form-config", async Task<IResult> (Guid et, AppDb db, IHubContext<GuestHub> hub) =>
        {
            var s = await db.KioskSessions.FirstOrDefaultAsync(x => x.EditToken == et && x.Status == SessionStatus.Active);
            if (s is null) return Results.NotFound(new { error = "Session not found" });
            if (await ExpireIfNeededAsync(s, db, hub)) return Results.StatusCode(StatusCodes.Status410Gone);

            var tplId = s.TemplateId ?? "T1";
            var t = await db.Templates.AsNoTracking().FirstOrDefaultAsync(x => x.Id == tplId);
            if (t is null) return Results.NotFound(new { error = $"Template '{tplId}' not found" });

            var prefill = string.IsNullOrWhiteSpace(s.PrefillJson) ? "{}" : s.PrefillJson;
            return Results.Content(GuestFormBuilder.BuildGuestFormConfigJson(tplId, t.DataJson, prefill), "application/json");
        });

        app.MapGet("/tablet/{kid:int}/form-config", async Task<IResult> (int kid, AppDb db, IHubContext<GuestHub> hub) =>
        {
            if (kid <= 0) return Results.BadRequest(new { error = "kid must be a positive integer" });

            var s = await db.KioskSessions
                .Where(x => x.Kid == kid && x.Status == SessionStatus.Active)
                .OrderByDescending(x => x.Id)
                .FirstOrDefaultAsync();

            if (s is null) return Results.NotFound(new { error = "Active session not found" });
            if (await ExpireIfNeededAsync(s, db, hub)) return Results.StatusCode(StatusCodes.Status410Gone);

            var tplId = s.TemplateId ?? "T1";
            var t = await db.Templates.AsNoTracking().FirstOrDefaultAsync(x => x.Id == tplId);
            if (t is null) return Results.NotFound(new { error = $"Template '{tplId}' not found" });

            var prefill = string.IsNullOrWhiteSpace(s.PrefillJson) ? "{}" : s.PrefillJson;
            return Results.Content(GuestFormBuilder.BuildGuestFormConfigJson(tplId, t.DataJson, prefill), "application/json");
        });

        // Guest-side cancel: hard-deletes the session identified by its edit token,
        // together with any guest data already linked to it, then tells the kiosk.
        // Idempotent: repeating the call (or racing another cancel) returns 204 too.
        api.MapDelete("/mobile/session", async Task<IResult> (Guid et, AppDb db, IHubContext<GuestHub> hub, CancellationToken ct) =>
        {
            if (et == Guid.Empty) return Results.BadRequest(new { error = "et is required" });

            var s = await db.KioskSessions
                .AsNoTracking()
                .Where(x => x.EditToken == et)
                .Select(x => new { x.Id, x.Kid, x.GuestId })
                .FirstOrDefaultAsync(ct);

            if (s is null) return Results.NoContent();

            await db.KioskSessions
                .Where(x => x.Id == s.Id)
                .ExecuteDeleteAsync(ct);

            if (s.GuestId.HasValue)
            {
                await db.Guests
                    .Where(g => g.Id == s.GuestId.Value)
                    .ExecuteDeleteAsync(ct);
            }

            await NotifySessionEndedAsync(hub, s.Kid, s.Id, "cancelled");
            return Results.NoContent();
        });

        api.MapGet("/sessions/{id:int}/result", async Task<IResult> (int id, AppDb db) =>
        {
            var s = await db.KioskSessions.AsNoTracking()
                .Include(x => x.Guest)
                .FirstOrDefaultAsync(x => x.Id == id);
            if (s is null) return Results.NotFound(new { error = "Session not found" });

            object guestObj = new { };
            if (!string.IsNullOrWhiteSpace(s.Guest?.DataJson))
                guestObj = JsonSerializer.Deserialize<object>(s.Guest!.DataJson!) ?? new { };

            object prefillObj = new { };
            if (!string.IsNullOrWhiteSpace(s.PrefillJson))
                prefillObj = JsonSerializer.Deserialize<object>(s.PrefillJson!) ?? new { };

            return Results.Ok(new
            {
                sessionId = s.Id,
                kid = s.Kid,
                status = s.Status,
                guestId = s.GuestId,
                guest = guestObj,
                prefill = prefillObj
            });
        });

        api.MapGet("/sessions/by-token", async Task<IResult> (Guid et, AppDb db) =>
        {
            var s = await db.KioskSessions.AsNoTracking()
                .Include(x => x.Guest)
                .FirstOrDefaultAsync(x => x.EditToken == et);
            if (s is null) return Results.NotFound(new { error = "Session not found" });

            object guestObj = new { };
            if (!string.IsNullOrWhiteSpace(s.Guest?.DataJson))
                guestObj = JsonSerializer.Deserialize<object>(s.Guest!.DataJson!) ?? new { };

            object prefillObj = new { };
            if (!string.IsNullOrWhiteSpace(s.PrefillJson))
                prefillObj = JsonSerializer.Deserialize<object>(s.PrefillJson!) ?? new { };

            return Results.Ok(new
            {
                sessionId = s.Id,
                kid = s.Kid,
                status = s.Status,
                guestId = s.GuestId,
                guest = guestObj,
                prefill = prefillObj
            });
        });

        api.MapPost("/mobile/save", async Task<IResult> (MobileSaveDto body, AppDb db, IHubContext<GuestHub> hub) =>
        {
            if (body is null || body.et == Guid.Empty) return Results.BadRequest(new { error = "Invalid payload" });

            var now = DateTime.UtcNow;
            var s = await db.KioskSessions.FirstOrDefaultAsync(x => x.EditToken == body.et);
            if (s is null) return Results.NotFound(new { error = "Session not found" });
            if (s.Status != SessionStatus.Active) return Results.Conflict(new { error = "Session not active" });
            if (s.ExpiresAt <= now)
            {
                s.Status = SessionStatus.Expired;
                s.UpdatedAt = now;
                await db.SaveChangesAsync();
                await NotifySessionEndedAsync(hub, s.Kid, s.Id, "expired");
                return Results.StatusCode(410);
            }

            var saveTplId = s.TemplateId ?? "T1";
            var saveTemplate = await db.Templates.AsNoTracking().FirstOrDefaultAsync(x => x.Id == saveTplId);
            if (saveTemplate is null) return Results.NotFound(new { error = $"Template '{saveTplId}' not found" });

            var guestDataJson = GuestFormBuilder.FilterSubmittedGuestData(body.data, saveTemplate.DataJson);
            var guest = new Guest { DataJson = guestDataJson };
            db.Guests.Add(guest);

            s.Guest = guest;
            s.Status = SessionStatus.Completed;
            s.UpdatedAt = now;
            await db.SaveChangesAsync();

            await hub.Clients.Group(GuestHub.KioskGroup(s.Kid)).SendAsync("sessionCompleted", new
            {
                kid = s.Kid,
                sessionId = s.Id,
                guest = JsonSerializer.Deserialize<object>(guest.DataJson) ?? new { }
            });

            return Results.Ok(new { ok = true, guestId = guest.Id, sessionId = s.Id, kid = s.Kid });
        })
        .Accepts<MobileSaveDto>("application/json")
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status409Conflict)
        .Produces(StatusCodes.Status410Gone);

        return app;
    }

    private static async Task<bool> ExpireIfNeededAsync(KioskSession session, AppDb db, IHubContext<GuestHub> hub)
    {
        var now = DateTime.UtcNow;
        if (session.ExpiresAt > now) return false;

        // Conditional atomic update: only this caller (or none, if a concurrent
        // request already expired/cancelled the row) flips the status and notifies.
        var expired = await db.KioskSessions
            .Where(x => x.Id == session.Id && x.Status == SessionStatus.Active)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.Status, SessionStatus.Expired)
                .SetProperty(x => x.UpdatedAt, now));

        if (expired > 0) await NotifySessionEndedAsync(hub, session.Kid, session.Id, "expired");
        return true;
    }

    private static Task NotifySessionEndedAsync(IHubContext<GuestHub> hub, int kid, int sessionId, string reason)
    {
        return hub.Clients.Group(GuestHub.KioskGroup(kid)).SendAsync("sessionEnded", new { kid, sessionId, reason });
    }
}
