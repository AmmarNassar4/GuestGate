namespace GuestGate.Api.Models;

public class ConsentRequestOptions
{
    public int ActiveLifetimeMinutes { get; set; } = 7;

    public TimeSpan ActiveLifetime => TimeSpan.FromMinutes(Math.Clamp(ActiveLifetimeMinutes, 1, 1440));
}
