using GuestGate.Api.Data;
using GuestGate.Api.Hubs;
using GuestGate.Api.Models;
using GuestGate.Api.Services;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Text.Json;
using System.Text;
using Serilog;
using Serilog.AspNetCore;
using Serilog.Debugging;
ConfigureSerilogSelfLog();

AppDomain.CurrentDomain.UnhandledException += (_, e) =>
{
    if (e.ExceptionObject is Exception ex) StartupDiagnostics.WriteFatal(ex);
};

TaskScheduler.UnobservedTaskException += (_, e) =>
{
    StartupDiagnostics.WriteFatal(e.Exception);
    e.SetObserved();
};

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<KioskOptions>(builder.Configuration.GetSection("Kiosk"));
builder.Services.AddDbContext<AppDb>(o => o.UseSqlServer(builder.Configuration.GetConnectionString("Default")));
builder.Services.AddSignalR();
builder.Services.AddCors(o =>
{
    var origins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? Array.Empty<string>();
    o.AddDefaultPolicy(p => p.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod().AllowCredentials());
});
builder.Host.UseSerilog((ctx, services, lg) =>
{
    lg.ReadFrom.Configuration(ctx.Configuration)
      .ReadFrom.Services(services)
      .Enrich.FromLogContext()
      .Enrich.WithMachineName()
      .Enrich.WithProperty("Application", "GuestGate.Api");
});
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddHostedService<GuestGate.Api.Services.SessionExpiryWorker>();
builder.Services.AddScoped<IConsentPdfWriter, ConsentPdfWriter>();
if (builder.Configuration.GetValue<bool>("SqlDependency:Enabled"))
{
    builder.Services.AddHostedService<ConsentWatcher>();
}

var app = builder.Build();

app.UseSerilogRequestLogging(opts =>
{
    opts.EnrichDiagnosticContext = (IDiagnosticContext diag, HttpContext http) =>
    {
        diag.Set("ClientIP", http.Connection.RemoteIpAddress?.ToString() ?? string.Empty);
        diag.Set("UserAgent", http.Request.Headers.UserAgent.ToString() ?? string.Empty);
        diag.Set("RequestId", http.TraceIdentifier ?? string.Empty);
    };
});

//using (var scope = app.Services.CreateScope())
//{
//    var db = scope.ServiceProvider.GetRequiredService<AppDb>();
//    Ensure DB exists; attempt to create tables if DB exists without schema
//   var creator = db.Database.GetService<IRelationalDatabaseCreator>();
//    if (!creator.Exists())
//    {
//        creator.Create();
//        creator.CreateTables();
//    }
//    else
//    {
//        try { var _ = await db.Templates.AsNoTracking().AnyAsync(); }
//        catch { creator.CreateTables(); }
//    }
//}
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDb>();
    await db.Database.EnsureCreatedAsync();
    await EnsureConsentRequestsTableAsync(db);
}

app.UseSwagger();
app.UseSwaggerUI();
app.UseCors();
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapGet("/consent", (HttpContext http) => Results.Redirect("/index.html" + http.Request.QueryString));

app.MapGet("/diag/health", (IConfiguration cfg) => Results.Ok(new
{
    ok = true,
    at = DateTime.UtcNow,
    environment = app.Environment.EnvironmentName,
    sqlDependencyEnabled = cfg.GetValue<bool>("SqlDependency:Enabled")
}));

app.MapGet("/diag/test-log", (ILoggerFactory lf, IConfiguration cfg) =>
{
    var eventId = Guid.NewGuid().ToString("N");
    var log = lf.CreateLogger("Diag");
    var seqUrl = ResolveSeqSetting(cfg, "serverUrl");
    var seqApiKey = ResolveSeqSetting(cfg, "apiKey");

    log.LogInformation(
        "Diagnostic test log {EventId} at {UtcNow}. SeqUrlConfigured={SeqUrlConfigured}",
        eventId,
        DateTime.UtcNow,
        !string.IsNullOrWhiteSpace(seqUrl));

    Log.ForContext("EventId", eventId)
        .ForContext("SeqUrlConfigured", !string.IsNullOrWhiteSpace(seqUrl))
        .Information("Seq diagnostic test log {EventId} at {UtcNow}", eventId, DateTime.UtcNow);

    return Results.Ok(new
    {
        ok = true,
        eventId,
        at = DateTime.UtcNow,
        seqUrlConfigured = !string.IsNullOrWhiteSpace(seqUrl),
        seqApiKeyConfigured = !string.IsNullOrWhiteSpace(seqApiKey),
        selfLogPath = StartupDiagnostics.SerilogSelfLogPath
    });
});

app.MapGet("/diag/test-seq", async Task<IResult> (IConfiguration cfg, ILoggerFactory lf, CancellationToken ct) =>
{
    var seqUrl = ResolveSeqSetting(cfg, "serverUrl");
    var seqApiKey = ResolveSeqSetting(cfg, "apiKey");
    if (string.IsNullOrWhiteSpace(seqUrl))
    {
        return Results.BadRequest(new { ok = false, error = "Seq URL is not configured." });
    }

    var eventId = Guid.NewGuid().ToString("N");
    var at = DateTimeOffset.UtcNow;
    lf.CreateLogger("Diag.Seq")
        .LogInformation("Seq sink diagnostic event {EventId} at {UtcNow}", eventId, at);

    using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
    using var request = new HttpRequestMessage(HttpMethod.Post, $"{seqUrl.TrimEnd('/')}/api/events/raw?clef");

    if (!string.IsNullOrWhiteSpace(seqApiKey))
    {
        request.Headers.Add("X-Seq-ApiKey", seqApiKey);
    }

    var clef = JsonSerializer.Serialize(new Dictionary<string, object?>
    {
        ["@t"] = at.ToString("O"),
        ["@mt"] = "Seq direct diagnostic event {EventId} from GuestGate",
        ["EventId"] = eventId,
        ["Application"] = "GuestGate.Api",
        ["SourceContext"] = "Diag.SeqDirect"
    });
    request.Content = new StringContent(clef + Environment.NewLine, Encoding.UTF8, "application/vnd.serilog.clef");

    using var response = await http.SendAsync(request, ct);
    var responseBody = await response.Content.ReadAsStringAsync(ct);

    return Results.Ok(new
    {
        ok = response.IsSuccessStatusCode,
        eventId,
        at,
        seqUrlConfigured = true,
        seqApiKeyConfigured = !string.IsNullOrWhiteSpace(seqApiKey),
        statusCode = (int)response.StatusCode,
        reason = response.ReasonPhrase,
        response = responseBody.Length > 500 ? responseBody[..500] : responseBody,
        selfLogPath = StartupDiagnostics.SerilogSelfLogPath
    });
});

app.MapGet("/api/kiosk/screensaver", (IConfiguration cfg) =>
{
    var s = cfg.GetSection("Kiosk:Screensaver");
    var enabled = s.GetValue<bool>("Enabled");
    var interval = s.GetValue<int?>("IntervalMs") ?? Math.Max(1000, (s.GetValue<int?>("IdleSeconds") ?? 8) * 1000);
    var images = s.GetSection("Images").Get<string[]>() ?? Array.Empty<string>();
    if (images.Length == 0)
    {
        images = new[] { "/slides/1.jpg", "/slides/2.jpg", "/slides/3.jpg" };
    }
    var idle = cfg.GetValue<int?>("Kiosk:IdleToScreensaverMs") ?? Math.Max(1, s.GetValue<int?>("IdleSeconds") ?? 30) * 1000;
    return Results.Ok(new { enabled, interval, images, idleMs = idle });
});

app.MapHub<GuestHub>("/hubs/guest");

var api = app.MapGroup("/api");

app.MapGet("/admin/templates", async (AppDb db) =>
{
    var ids = await db.Templates.AsNoTracking().Select(t => t.Id).OrderBy(x => x).ToListAsync();
    return Results.Ok(ids);
});

app.MapGet("/admin/templates/{id}", async (string id, AppDb db) =>
{
    var t = await db.Templates.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
    if (t is null) return Results.NotFound(new { error = "Template not found" });
    return Results.Content(t.DataJson, "application/json");
});

app.MapPost("/admin/templates", async (AppDb db, HttpRequest req) =>
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

api.MapPost("/sessions/start", async (
    HttpRequest req,
    string? kid, string? templateId,
    AppDb db, IOptions<KioskOptions> opt, IHubContext<GuestHub> hub) =>
{
    SessionStartDto? body = null;
    try { body = await req.ReadFromJsonAsync<SessionStartDto>(); } catch { /* ignore */ }

    var KID = GuestHub.NormalizeKid(body?.kid ?? kid);
    var TPL = (body?.templateId ?? templateId)?.Trim();
    var prefillJson = body?.prefill is JsonElement p ? p.GetRawText() : null;

    if (string.IsNullOrWhiteSpace(KID))
        return Results.BadRequest(new { error = "kid is required" });

    if (!string.IsNullOrWhiteSpace(TPL))
    {
        var exists = await db.Templates.AsNoTracking().AnyAsync(x => x.Id == TPL);
        if (!exists) return Results.NotFound(new { error = $"Template '{TPL}' not found" });
    }

    var now = DateTime.UtcNow;
    var active = await db.KioskSessions
        .Where(s => s.Kid.ToUpper() == KID && s.Status == SessionStatus.Active)
        .OrderByDescending(s => s.Id).FirstOrDefaultAsync();

    if (active is not null && active.ExpiresAt <= now)
    {
        active.Status = SessionStatus.Expired;
        active.UpdatedAt = now;
        await db.SaveChangesAsync();
        active = null;
    }

    if (active is null)
    {
        active = new KioskSession
        {
            Kid = KID,
            EditToken = Guid.NewGuid(),
            Status = SessionStatus.Active,
            ExpiresAt = now.AddMinutes(opt.Value.SessionMinutes),
            TemplateId = TPL,
            PrefillJson = prefillJson,
            CreatedAt = now,
            UpdatedAt = now
        };
        db.KioskSessions.Add(active);
        await db.SaveChangesAsync();
    }
    else
    {
        // If the desktop re-sends Start while the tablet missed the first realtime message,
        // refresh the active session and broadcast it again.
        active.Kid = GuestHub.NormalizeKid(active.Kid);
        if (!string.IsNullOrWhiteSpace(TPL)) active.TemplateId = TPL;
        if (!string.IsNullOrWhiteSpace(prefillJson)) active.PrefillJson = prefillJson;
        active.UpdatedAt = now;
        await db.SaveChangesAsync();
    }

    var scanUrl = BuildScanUrl(opt.Value.MobileBaseUrl, active.EditToken, KID);
    await NotifySessionStartedAsync(hub, active, KID, scanUrl);

    return Results.Ok(new { sessionId = active.Id, kid = KID, et = active.EditToken, scanUrl, expiresAt = active.ExpiresAt });
});

api.MapGet("/sessions/active", async (string kid, AppDb db, IOptions<KioskOptions> opt) =>
{
    var KID = GuestHub.NormalizeKid(kid);
    if (string.IsNullOrWhiteSpace(KID))
        return Results.BadRequest(new { error = "kid is required" });

    var now = DateTime.UtcNow;
    var s = await db.KioskSessions.Where(x => x.Kid.ToUpper() == KID && x.Status == SessionStatus.Active)
                                  .OrderByDescending(x => x.Id).FirstOrDefaultAsync();
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

api.MapDelete("/sessions/active", async (string kid, AppDb db, IHubContext<GuestHub> hub) =>
{
    var KID = GuestHub.NormalizeKid(kid);
    if (string.IsNullOrWhiteSpace(KID))
        return Results.BadRequest(new { error = "kid is required" });

    var s = await db.KioskSessions
        .Where(x => x.Kid.ToUpper() == KID && x.Status == SessionStatus.Active)
        .OrderByDescending(x => x.Id)
        .FirstOrDefaultAsync();
    if (s is null) return Results.NoContent();
    s.Status = SessionStatus.Cancelled;
    s.UpdatedAt = DateTime.UtcNow;
    await db.SaveChangesAsync();
    await NotifySessionEndedAsync(hub, KID, s.Id, "cancelled");
    return Results.NoContent();
});

api.MapPut("/sessions/prefill", async (Guid et, HttpRequest req, AppDb db) =>
{
    var s = await db.KioskSessions.FirstOrDefaultAsync(x => x.EditToken == et && x.Status == SessionStatus.Active);
    if (s is null) return Results.NotFound(new { error = "Active session not found" });
    using var doc = await JsonDocument.ParseAsync(req.Body);
    s.PrefillJson = doc.RootElement.GetRawText();
    s.UpdatedAt = DateTime.UtcNow;
    await db.SaveChangesAsync();
    return Results.Ok(new { ok = true });
});

app.MapGet("/api/mobile/form-config", async (Guid et, AppDb db) =>
{
    var s = await db.KioskSessions.FirstOrDefaultAsync(x => x.EditToken == et && x.Status == SessionStatus.Active);
    if (s is null) return Results.NotFound(new { error = "Session not found" });

    var tplId = s.TemplateId ?? "T1";
    var t = await db.Templates.AsNoTracking().FirstOrDefaultAsync(x => x.Id == tplId);
    if (t is null) return Results.NotFound(new { error = $"Template '{tplId}' not found" });

    var prefill = string.IsNullOrWhiteSpace(s.PrefillJson) ? "{}" : s.PrefillJson;
    return Results.Content(BuildGuestFormConfigJson(tplId, t.DataJson, prefill), "application/json");
});


api.MapPost("/consents", async (
    ConsentCreateDto body,
    AppDb db,
    IWebHostEnvironment env,
    IHubContext<GuestHub> hub,
    CancellationToken ct) =>
{
    if (body is null) return Results.BadRequest(new { error = "Invalid payload" });

    var KID = GuestHub.NormalizeKid(body.kid);
    if (string.IsNullOrWhiteSpace(KID)) return Results.BadRequest(new { error = "kid is required" });

    var checkInTime = (body.checkInTime ?? string.Empty).Trim();
    if (string.IsNullOrWhiteSpace(checkInTime)) return Results.BadRequest(new { error = "checkInTime is required" });

    var language = NormalizeConsentLanguage(body.language);
    var now = DateTime.UtcNow;
    var request = new ConsentRequest
    {
        Kid = KID,
        GuestName = (body.guestName ?? string.Empty).Trim(),
        IdentityNumber = (body.identityNumber ?? string.Empty).Trim(),
        CheckInTime = checkInTime,
        Language = language,
        TermsEn = await LoadConsentTermsAsync(env, "en", checkInTime, ct),
        TermsAr = await LoadConsentTermsAsync(env, "ar", checkInTime, ct),
        Status = "waiting",
        CreatedAt = now,
        UpdatedAt = now
    };

    db.ConsentRequests.Add(request);
    await db.SaveChangesAsync(ct);
    await NotifyConsentChangedAsync(hub, request.Kid, request.Id, "waiting");

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

    if (!string.Equals(request.Status, "signed", StringComparison.OrdinalIgnoreCase) ||
        string.IsNullOrWhiteSpace(request.SignatureImageDataUrl))
    {
        return Results.NotFound(new { error = "Signature is not available yet" });
    }

    return Results.Ok(new
    {
        id = request.Id,
        status = request.Status,
        pdfPath = request.PdfPath,
        signatureImage = request.SignatureImageDataUrl,
        signedAt = request.SignedAt
    });
});

api.MapGet("/consents/active", async (string kid, AppDb db) =>
{
    var KID = GuestHub.NormalizeKid(kid);
    if (string.IsNullOrWhiteSpace(KID)) return Results.BadRequest(new { error = "kid is required" });

    var request = await db.ConsentRequests
        .Where(x => x.Kid.ToUpper() == KID && (x.Status == "waiting" || x.Status == "assigned"))
        .OrderBy(x => x.Id)
        .FirstOrDefaultAsync();

    if (request is null) return Results.NoContent();
    if (request.Status == "waiting")
    {
        request.Status = "assigned";
        request.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }

    return Results.Ok(ToConsentDto(request));
});

api.MapDelete("/consents/active", CancelActiveConsentsForKidAsync);
app.MapPost("/api/consents/cancel", CancelActiveConsentsForKidAsync);

api.MapPost("/consents/{id:int}/sign", async (int id, ConsentSignDto body, AppDb db, IConsentPdfWriter pdfWriter, IHubContext<GuestHub> hub, CancellationToken ct) =>
{
    if (body is null) return Results.BadRequest(new { error = "Invalid payload" });
    if (!body.accepted) return Results.BadRequest(new { error = "The terms must be accepted before signing." });
    if (string.IsNullOrWhiteSpace(body.signatureImage)) return Results.BadRequest(new { error = "signatureImage is required" });

    var request = await db.ConsentRequests.FirstOrDefaultAsync(x => x.Id == id, ct);
    if (request is null) return Results.NotFound(new { error = "Consent request not found" });
    if (request.Status == "signed") return Results.Conflict(new { error = "Consent request is already signed", pdfPath = request.PdfPath });

    request.Accepted = true;
    request.Language = NormalizeConsentLanguage(body.language ?? request.Language);
    request.SignatureImageDataUrl = body.signatureImage;
    request.SignedAt = DateTime.UtcNow;
    request.Status = "signed";
    request.PdfPath = await pdfWriter.WriteAsync(request, ct);
    await db.SaveChangesAsync(ct);

    await NotifyConsentChangedAsync(hub, request.Kid, request.Id, "signed", request.PdfPath);
    return Results.Ok(new { ok = true, id = request.Id, pdfPath = request.PdfPath });
});

app.MapGet("/tablet/{kid}/form-config", async (string kid, AppDb db) =>
{
    var KID = GuestHub.NormalizeKid(kid);
    if (string.IsNullOrWhiteSpace(KID))
        return Results.BadRequest(new { error = "kid is required" });

    var s = await db.KioskSessions.Where(x => x.Kid.ToUpper() == KID && x.Status == SessionStatus.Active)
                                  .OrderByDescending(x => x.Id).FirstOrDefaultAsync();
    if (s is null) return Results.NotFound(new { error = "Active session not found" });

    var tplId = s.TemplateId ?? "T1";
    var t = await db.Templates.AsNoTracking().FirstOrDefaultAsync(x => x.Id == tplId);
    if (t is null) return Results.NotFound(new { error = $"Template '{tplId}' not found" });

    var prefill = string.IsNullOrWhiteSpace(s.PrefillJson) ? "{}" : s.PrefillJson;
    return Results.Content(BuildGuestFormConfigJson(tplId, t.DataJson, prefill), "application/json");
});
// GET /api/sessions/{id}/result  -> returns the session + guest JSON (if completed)
api.MapGet("/sessions/{id:int}/result", async (int id, AppDb db) =>
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
        guest = guestObj,     // JSON (object)
        prefill = prefillObj  // JSON (object)
    });
});

// GET /api/sessions/by-token?et=...  -> same as above but by EditToken
api.MapGet("/sessions/by-token", async (Guid et, AppDb db) =>
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


app.MapPost("/api/sessions/cancel", async (AppDb db, string kid, IHubContext<GuestHub> hub) =>
{
    var KID = GuestHub.NormalizeKid(kid);
    if (string.IsNullOrWhiteSpace(KID))
        return Results.BadRequest(new { error = "kid is required" });

    var s = await db.KioskSessions
        .Where(x => x.Kid.ToUpper() == KID && x.Status == SessionStatus.Active)
        .OrderByDescending(x => x.Id)
        .FirstOrDefaultAsync();

    if (s is null) return Results.NotFound(new { error = "No active session to cancel" });

    s.Status = SessionStatus.Cancelled;
    s.UpdatedAt = DateTime.UtcNow;
    await db.SaveChangesAsync();
    await NotifySessionEndedAsync(hub, KID, s.Id, "cancelled");

    return Results.NoContent();
});

api.MapPost("/mobile/save", async (MobileSaveDto body, AppDb db, IHubContext<GuestHub> hub) =>
{
    if (body is null || body.et == Guid.Empty)
        return Results.BadRequest(new { error = "Invalid payload" });

    var now = DateTime.UtcNow;
    var s = await db.KioskSessions.FirstOrDefaultAsync(x => x.EditToken == body.et);
    if (s is null) return Results.NotFound(new { error = "Session not found" });
    if (s.Status != SessionStatus.Active) return Results.Conflict(new { error = "Session not active" });
    if (s.ExpiresAt <= now) { s.Status = SessionStatus.Expired; await db.SaveChangesAsync(); return Results.StatusCode(410); }

    var saveTplId = s.TemplateId ?? "T1";
    var saveTemplate = await db.Templates.AsNoTracking().FirstOrDefaultAsync(x => x.Id == saveTplId);
    if (saveTemplate is null) return Results.NotFound(new { error = $"Template '{saveTplId}' not found" });
    var guestDataJson = FilterSubmittedGuestData(body.data, saveTemplate.DataJson);

    Guest? guest = null;

    guest = new Guest { DataJson = guestDataJson, CreatedAt = now, UpdatedAt = now };
    db.Guests.Add(guest);

    await db.SaveChangesAsync();

    s.GuestId = guest.Id;
    s.Status = SessionStatus.Completed;
    s.UpdatedAt = now;
    await db.SaveChangesAsync();

    await hub.Clients.Group(GuestHub.KioskGroup(s.Kid)).SendAsync("sessionCompleted", new
    {
        kid = s.Kid,
        sessionId = s.Id,
        guest = System.Text.Json.JsonSerializer.Deserialize<object>(guest.DataJson) ?? new { }
    });

    return Results.Ok(new { ok = true, guestId = guest.Id, sessionId = s.Id, kid = s.Kid });
})
.Accepts<MobileSaveDto>("application/json")
.Produces(StatusCodes.Status200OK).Produces(StatusCodes.Status404NotFound).Produces(StatusCodes.Status409Conflict).Produces(StatusCodes.Status410Gone);
app.Run();




static string BuildGuestFormConfigJson(string tplId, string templateJson, string prefillJson)
{
    var safeTemplate = FilterTemplateForGuest(templateJson);
    var safePrefill = FilterJsonObjectByGuestVisibleFields(prefillJson, templateJson);
    return $"{{\"templateId\":{JsonSerializer.Serialize(tplId)},\"template\":{safeTemplate},\"prefill\":{safePrefill}}}";
}

static string FilterTemplateForGuest(string templateJson)
{
    try
    {
        using var doc = JsonDocument.Parse(templateJson);
        if (doc.RootElement.ValueKind != JsonValueKind.Object) return "{}";

        using var ms = new MemoryStream();
        using (var writer = new Utf8JsonWriter(ms))
        {
            writer.WriteStartObject();
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (prop.NameEquals("fields") && prop.Value.ValueKind == JsonValueKind.Array)
                {
                    writer.WritePropertyName(prop.Name);
                    writer.WriteStartArray();
                    foreach (var field in prop.Value.EnumerateArray())
                    {
                        if (field.ValueKind == JsonValueKind.Object && IsStartFormField(field) && IsGuestVisibleField(field))
                        {
                            field.WriteTo(writer);
                        }
                    }
                    writer.WriteEndArray();
                }
                else
                {
                    prop.WriteTo(writer);
                }
            }
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(ms.ToArray());
    }
    catch
    {
        // Fail closed for guest-facing form config so hidden template fields are not leaked.
        return "{\"fields\":[]}";
    }
}

static string FilterJsonObjectByGuestVisibleFields(string json, string templateJson)
{
    try
    {
        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json);
        return FilterJsonElementByGuestVisibleFields(doc.RootElement, templateJson);
    }
    catch
    {
        return "{}";
    }
}

static string FilterSubmittedGuestData(JsonElement submittedData, string templateJson)
{
    return FilterJsonElementByGuestVisibleFields(submittedData, templateJson);
}

static string FilterJsonElementByGuestVisibleFields(JsonElement data, string templateJson)
{
    try
    {
        var visibleKeys = GetGuestVisibleKeys(templateJson);
        using var ms = new MemoryStream();
        using (var writer = new Utf8JsonWriter(ms))
        {
            writer.WriteStartObject();
            if (data.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in data.EnumerateObject())
                {
                    if (visibleKeys.Contains(prop.Name))
                    {
                        prop.WriteTo(writer);
                    }
                }
            }
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(ms.ToArray());
    }
    catch
    {
        // Fail closed: never store manually-submitted fields that are not allowed by the template.
        return "{}";
    }
}

static HashSet<string> GetGuestVisibleKeys(string templateJson)
{
    var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    using var doc = JsonDocument.Parse(templateJson);
    if (doc.RootElement.ValueKind != JsonValueKind.Object) return keys;
    if (!doc.RootElement.TryGetProperty("fields", out var fields) || fields.ValueKind != JsonValueKind.Array) return keys;

    foreach (var field in fields.EnumerateArray())
    {
        if (field.ValueKind != JsonValueKind.Object) continue;
        if (!IsStartFormField(field) || !IsGuestVisibleField(field)) continue;
        var key = GetFieldKey(field);
        if (!string.IsNullOrWhiteSpace(key)) keys.Add(key);
    }

    return keys;
}

static bool IsStartFormField(JsonElement field)
{
    if (!field.TryGetProperty("scope", out var scope) || scope.ValueKind != JsonValueKind.String) return true;
    var value = scope.GetString();
    return string.IsNullOrWhiteSpace(value) || string.Equals(value, "StartForm", StringComparison.OrdinalIgnoreCase);
}

static bool IsGuestVisibleField(JsonElement field)
{
    if (field.TryGetProperty("visible", out var rootVisible) && IsJsonFalse(rootVisible)) return false;

    if (!field.TryGetProperty("guest", out var guest) || guest.ValueKind != JsonValueKind.Object) return true;
    if (guest.TryGetProperty("hide", out var hide) && IsJsonTrue(hide)) return false;
    if (guest.TryGetProperty("visible", out var visible) && IsJsonFalse(visible)) return false;
    return true;
}

static bool IsJsonFalse(JsonElement value)
{
    return value.ValueKind == JsonValueKind.False ||
           (value.ValueKind == JsonValueKind.String && string.Equals(value.GetString(), "false", StringComparison.OrdinalIgnoreCase));
}

static bool IsJsonTrue(JsonElement value)
{
    return value.ValueKind == JsonValueKind.True ||
           (value.ValueKind == JsonValueKind.String && string.Equals(value.GetString(), "true", StringComparison.OrdinalIgnoreCase));
}

static string? GetFieldKey(JsonElement field)
{
    foreach (var propertyName in new[] { "key", "name", "id" })
    {
        if (field.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String)
        {
            var text = value.GetString();
            if (!string.IsNullOrWhiteSpace(text)) return text;
        }
    }
    return null;
}

static async Task EnsureConsentRequestsTableAsync(AppDb db)
{
    if (db.Database.IsSqlServer())
    {
        await db.Database.ExecuteSqlRawAsync(@"
IF OBJECT_ID(N'[dbo].[ConsentRequests]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[ConsentRequests]
    (
        [Id] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_ConsentRequests] PRIMARY KEY,
        [Kid] nvarchar(50) NOT NULL,
        [GuestName] nvarchar(200) NOT NULL CONSTRAINT [DF_ConsentRequests_GuestName] DEFAULT N'',
        [IdentityNumber] nvarchar(80) NOT NULL CONSTRAINT [DF_ConsentRequests_IdentityNumber] DEFAULT N'',
        [CheckInTime] nvarchar(50) NOT NULL CONSTRAINT [DF_ConsentRequests_CheckInTime] DEFAULT N'',
        [Language] nvarchar(5) NOT NULL CONSTRAINT [DF_ConsentRequests_Language] DEFAULT N'en',
        [TermsEn] nvarchar(max) NOT NULL CONSTRAINT [DF_ConsentRequests_TermsEn] DEFAULT N'',
        [TermsAr] nvarchar(max) NOT NULL CONSTRAINT [DF_ConsentRequests_TermsAr] DEFAULT N'',
        [Status] nvarchar(20) NOT NULL CONSTRAINT [DF_ConsentRequests_Status] DEFAULT N'waiting',
        [Accepted] bit NOT NULL CONSTRAINT [DF_ConsentRequests_Accepted] DEFAULT CONVERT(bit, 0),
        [SignatureImageDataUrl] nvarchar(max) NULL,
        [PdfPath] nvarchar(max) NULL,
        [SignedAt] datetime2 NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NOT NULL
    );

    CREATE INDEX [IX_ConsentRequests_Kid_Status] ON [dbo].[ConsentRequests] ([Kid], [Status]);
END
");


    if (db.Database.IsSqlServer())
    {
        await db.Database.ExecuteSqlRawAsync(@"
IF COL_LENGTH(N'dbo.ConsentRequests', N'IdentityNumber') IS NULL
BEGIN
    ALTER TABLE [dbo].[ConsentRequests]
    ADD [IdentityNumber] nvarchar(80) NOT NULL CONSTRAINT [DF_ConsentRequests_IdentityNumber] DEFAULT N'';
END

IF COL_LENGTH(N'dbo.ConsentRequests', N'CheckInTime') IS NULL
BEGIN
    ALTER TABLE [dbo].[ConsentRequests]
    ADD [CheckInTime] nvarchar(50) NOT NULL CONSTRAINT [DF_ConsentRequests_CheckInTime] DEFAULT N'';
END
");
    }
    }
}

static async Task<string> LoadConsentTermsAsync(IWebHostEnvironment env, string language, string checkInTime, CancellationToken ct)
{
    var fileName = NormalizeConsentLanguage(language) == "ar" ? "ar.txt" : "en.txt";
    var candidates = new[]
    {
        Path.Combine(env.ContentRootPath ?? AppContext.BaseDirectory, "terms", fileName),
        Path.Combine(AppContext.BaseDirectory, "terms", fileName),
        Path.Combine(env.WebRootPath ?? Path.Combine(AppContext.BaseDirectory, "wwwroot"), "terms", fileName)
    };

    foreach (var path in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
    {
        if (!File.Exists(path)) continue;
        var template = await File.ReadAllTextAsync(path, Encoding.UTF8, ct);
        return template.Replace("<time>", checkInTime, StringComparison.OrdinalIgnoreCase);
    }

    var fallback = NormalizeConsentLanguage(language) == "ar"
        ? ConsentDefaults.TermsAr
        : ConsentDefaults.TermsEn;

    return fallback.Replace("<time>", checkInTime, StringComparison.OrdinalIgnoreCase);
}

static string NormalizeConsentLanguage(string? language)
{
    return string.Equals((language ?? string.Empty).Trim(), "ar", StringComparison.OrdinalIgnoreCase) ? "ar" : "en";
}

static object ToConsentDto(ConsentRequest request)
{
    return new
    {
        id = request.Id,
        kid = GuestHub.NormalizeKid(request.Kid),
        guestName = request.GuestName,
        identityNumber = request.IdentityNumber,
        checkInTime = request.CheckInTime,
        language = NormalizeConsentLanguage(request.Language),
        termsEn = request.TermsEn,
        termsAr = request.TermsAr,
        status = request.Status,
        accepted = request.Accepted,
        signedAt = request.SignedAt,
        pdfPath = request.PdfPath
    };
}

static Task NotifyConsentChangedAsync(IHubContext<GuestHub> hub, string kid, int consentId, string status, string? pdfPath = null)
{
    var normalizedKid = GuestHub.NormalizeKid(kid);
    return hub.Clients.Group(GuestHub.KioskGroup(normalizedKid)).SendAsync("consentChanged", new
    {
        kid = normalizedKid,
        consentId,
        status,
        pdfPath
    });
}

static async Task<IResult> CancelActiveConsentsForKidAsync(string kid, AppDb db, IHubContext<GuestHub> hub)
{
    var KID = GuestHub.NormalizeKid(kid);
    if (string.IsNullOrWhiteSpace(KID))
        return Results.BadRequest(new { error = "kid is required" });

    var requests = await db.ConsentRequests
        .Where(x => x.Kid.ToUpper() == KID && (x.Status == "waiting" || x.Status == "assigned"))
        .OrderBy(x => x.Id)
        .ToListAsync();

    if (requests.Count == 0)
        return Results.NoContent();

    var now = DateTime.UtcNow;
    foreach (var request in requests)
    {
        request.Status = "cancelled";
        request.UpdatedAt = now;
    }

    await db.SaveChangesAsync();

    foreach (var request in requests)
    {
        await NotifyConsentChangedAsync(hub, request.Kid, request.Id, "cancelled");
    }

    return Results.Ok(new { ok = true, cancelledCount = requests.Count });
}


static string BuildScanUrl(string mobileBaseUrl, Guid editToken, string kid)
{
    var separator = mobileBaseUrl.Contains('?') ? "&" : "?";
    return $"{mobileBaseUrl}{separator}et={editToken}&kid={Uri.EscapeDataString(GuestHub.NormalizeKid(kid))}";
}

static Task NotifySessionStartedAsync(IHubContext<GuestHub> hub, KioskSession session, string kid, string scanUrl)
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

static Task NotifySessionEndedAsync(IHubContext<GuestHub> hub, string kid, int sessionId, string reason)
{
    var normalizedKid = GuestHub.NormalizeKid(kid);
    return hub.Clients.Group(GuestHub.KioskGroup(normalizedKid)).SendAsync("sessionEnded", new
    {
        kid = normalizedKid,
        sessionId,
        reason
    });
}

static void ConfigureSerilogSelfLog()
{
    SelfLog.Enable(message =>
    {
        try
        {
            Directory.CreateDirectory(StartupDiagnostics.LogDirectory);
            File.AppendAllText(StartupDiagnostics.SerilogSelfLogPath, message);
            Console.Error.Write(message);
        }
        catch
        {
            // SelfLog must never throw into application startup.
        }
    });
}

static string? ResolveSeqSetting(IConfiguration cfg, string key)
{
    var directKey = key.Equals("serverUrl", StringComparison.OrdinalIgnoreCase) ? "Seq:Url" : "Seq:ApiKey";
    var direct = cfg[directKey];
    if (!string.IsNullOrWhiteSpace(direct)) return direct;

    foreach (var sink in cfg.GetSection("Serilog:WriteTo").GetChildren())
    {
        if (!string.Equals(sink["Name"], "Seq", StringComparison.OrdinalIgnoreCase)) continue;
        var value = sink[$"Args:{key}"];
        if (!string.IsNullOrWhiteSpace(value)) return value;
    }

    return null;
}

internal static class StartupDiagnostics
{
    public static string LogDirectory => Path.Combine(AppContext.BaseDirectory, "logs");
    public static string SerilogSelfLogPath => Path.Combine(LogDirectory, "serilog-selflog.log");

    public static void WriteFatal(Exception exception)
    {
        try
        {
            Directory.CreateDirectory(LogDirectory);
            var message = $"[{DateTimeOffset.UtcNow:O}] GuestGate startup failure{Environment.NewLine}{exception}{Environment.NewLine}";
            File.AppendAllText(Path.Combine(LogDirectory, "startup-errors.log"), message);
            Console.Error.WriteLine(message);
        }
        catch
        {
            // Avoid masking the original startup exception if file logging is unavailable.
        }
    }
}
