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

        app.MapGet("/api/mobile/form-config", async Task<IResult> (Guid et, AppDb db) =>
        {
            var s = await db.KioskSessions.FirstOrDefaultAsync(x => x.EditToken == et && x.Status == SessionStatus.Active);
            if (s is null) return Results.NotFound(new { error = "Session not found" });

            var tplId = s.TemplateId ?? "T1";
            var t = await db.Templates.AsNoTracking().FirstOrDefaultAsync(x => x.Id == tplId);
            if (t is null) return Results.NotFound(new { error = $"Template '{tplId}' not found" });

            var prefill = string.IsNullOrWhiteSpace(s.PrefillJson) ? "{}" : s.PrefillJson;
            return Results.Content(GuestFormBuilder.BuildGuestFormConfigJson(tplId, t.DataJson, prefill), "application/json");
        });

        app.MapGet("/tablet/{kid}/form-config", async Task<IResult> (string kid, AppDb db) =>
        {
            var normalizedKid = GuestHub.NormalizeKid(kid);
            if (string.IsNullOrWhiteSpace(normalizedKid)) return Results.BadRequest(new { error = "kid is required" });

            var s = await db.KioskSessions
                .Where(x => x.Kid.ToUpper() == normalizedKid && x.Status == SessionStatus.Active)
                .OrderByDescending(x => x.Id)
                .FirstOrDefaultAsync();

            if (s is null) return Results.NotFound(new { error = "Active session not found" });

            var tplId = s.TemplateId ?? "T1";
            var t = await db.Templates.AsNoTracking().FirstOrDefaultAsync(x => x.Id == tplId);
            if (t is null) return Results.NotFound(new { error = $"Template '{tplId}' not found" });

            var prefill = string.IsNullOrWhiteSpace(s.PrefillJson) ? "{}" : s.PrefillJson;
            return Results.Content(GuestFormBuilder.BuildGuestFormConfigJson(tplId, t.DataJson, prefill), "application/json");
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
                await db.SaveChangesAsync();
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
}
