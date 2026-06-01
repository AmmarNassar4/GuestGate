using GuestGate.Api.Services;
using Serilog;
using System.Text;
using System.Text.Json;

namespace GuestGate.Api.Endpoints;

internal static class DiagnosticsEndpoints
{
    public static IEndpointRouteBuilder MapDiagnosticsEndpoints(this IEndpointRouteBuilder app, IWebHostEnvironment environment)
    {
        app.MapGet("/diag/health", (IConfiguration cfg) => Results.Ok(new
        {
            ok = true,
            at = DateTime.UtcNow,
            environment = environment.EnvironmentName,
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

        return app;
    }

    private static string? ResolveSeqSetting(IConfiguration cfg, string key)
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
}
