using GuestGate.Api.Data;
using GuestGate.Api.Models;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace GuestGate.Api.Endpoints;

internal static class TemplatesEndpoints
{
    public static IEndpointRouteBuilder MapTemplatesEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/admin/templates", async (AppDb db) =>
        {
            var ids = await db.Templates.AsNoTracking().Select(t => t.Id).OrderBy(x => x).ToListAsync();
            return Results.Ok(ids);
        });

        app.MapGet("/admin/templates/{id}", async Task<IResult> (string id, AppDb db) =>
        {
            var t = await db.Templates.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
            if (t is null) return Results.NotFound(new { error = "Template not found" });
            return Results.Content(t.DataJson, "application/json");
        });

        app.MapPost("/admin/templates", async Task<IResult> (AppDb db, HttpRequest req) =>
        {
            using var doc = await JsonDocument.ParseAsync(req.Body);
            var root = doc.RootElement;
            if (!root.TryGetProperty("templateId", out var idEl)) return Results.BadRequest(new { error = "templateId is required" });
            var id = idEl.GetString();
            if (string.IsNullOrWhiteSpace(id)) return Results.BadRequest(new { error = "templateId is required" });
            if (!root.TryGetProperty("data", out var dataEl)) return Results.BadRequest(new { error = "data is required" });
            var data = dataEl.GetRawText();
            var now = DateTime.UtcNow;

            var t = await db.Templates.FindAsync(id);
            if (t is null) db.Templates.Add(new Template { Id = id!, DataJson = data, CreatedAt = now, UpdatedAt = now });
            else { t.DataJson = data; t.UpdatedAt = now; }
            await db.SaveChangesAsync();
            return Results.Ok(new { ok = true, templateId = id });
        });

        return app;
    }
}
