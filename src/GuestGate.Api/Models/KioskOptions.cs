namespace GuestGate.Api.Models
{
    public class KioskOptions
    {
        public string MobileBaseUrl { get; set; } = default!;
        public int SessionMinutes { get; set; }
        public int IdleToScreensaverMs { get; set; } = 120000;
        public ScreensaverOptions Screensaver { get; set; } = new();
    }

    public class ScreensaverOptions
    {
        public bool Enabled { get; set; }
        public int IdleSeconds { get; set; } = 30;
        public int IntervalMs { get; set; } = 8000;
        public string[] Images { get; set; } = System.Array.Empty<string>();
    }
}
