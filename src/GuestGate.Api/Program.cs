using GuestGate.Api.Data;
using GuestGate.Api.Endpoints;
using GuestGate.Api.Hubs;
using GuestGate.Api.Models;
using GuestGate.Api.Services;
using Microsoft.EntityFrameworkCore;
using Serilog;
using Serilog.AspNetCore;

StartupDiagnostics.ConfigureSerilogSelfLog();

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
builder.Services.AddHostedService<SessionExpiryWorker>();
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

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDb>();
    await db.Database.EnsureCreatedAsync();
    await ConsentSchemaInitializer.EnsureConsentRequestsTableAsync(db);
}

app.UseSwagger();
app.UseSwaggerUI();
app.UseCors();
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/consent", (HttpContext http) => Results.Redirect("/index.html" + http.Request.QueryString));
app.MapHub<GuestHub>("/hubs/guest");

app.MapDiagnosticsEndpoints(app.Environment);
app.MapTemplatesEndpoints();
app.MapSessionManagementEndpoints();
app.MapGuestFlowEndpoints();
app.MapConsentsEndpoints();

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

app.Run();
