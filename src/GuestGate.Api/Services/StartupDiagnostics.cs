using Serilog.Debugging;

namespace GuestGate.Api.Services;

internal static class StartupDiagnostics
{
    public static string LogDirectory => Path.Combine(AppContext.BaseDirectory, "logs");
    public static string SerilogSelfLogPath => Path.Combine(LogDirectory, "serilog-selflog.log");

    public static void ConfigureSerilogSelfLog()
    {
        SelfLog.Enable(message =>
        {
            try
            {
                Directory.CreateDirectory(LogDirectory);
                File.AppendAllText(SerilogSelfLogPath, message);
                Console.Error.Write(message);
            }
            catch
            {
                // SelfLog must never throw into application startup.
            }
        });
    }

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
