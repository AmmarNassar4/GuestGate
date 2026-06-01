using GuestGate.Api.Models;
using System.Text;

namespace GuestGate.Api.Services;

internal static class ConsentTermsLoader
{
    public static async Task<string> LoadAsync(IWebHostEnvironment env, string language, string checkInTime)
    {
        var fileName = NormalizeLanguage(language) == "ar" ? "ar.txt" : "en.txt";
        var candidates = new[]
        {
            Path.Combine(env.ContentRootPath ?? AppContext.BaseDirectory, "terms", fileName),
            Path.Combine(AppContext.BaseDirectory, "terms", fileName),
            Path.Combine(env.WebRootPath ?? Path.Combine(AppContext.BaseDirectory, "wwwroot"), "terms", fileName)
        };

        foreach (var path in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!File.Exists(path)) continue;
            var template = await File.ReadAllTextAsync(path, Encoding.UTF8);
            return template.Replace("<time>", checkInTime, StringComparison.OrdinalIgnoreCase);
        }

        var fallback = NormalizeLanguage(language) == "ar"
            ? ConsentDefaults.TermsAr
            : ConsentDefaults.TermsEn;

        return fallback.Replace("<time>", checkInTime, StringComparison.OrdinalIgnoreCase);
    }

    public static string NormalizeLanguage(string? language)
    {
        return string.Equals((language ?? string.Empty).Trim(), "ar", StringComparison.OrdinalIgnoreCase) ? "ar" : "en";
    }
}
