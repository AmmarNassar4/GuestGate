using Microsoft.AspNetCore.SignalR;

namespace GuestGate.Api.Hubs
{
    public class GuestHub : Hub
    {
        public static string NormalizeKid(string? kid) => (kid ?? string.Empty).Trim().ToUpperInvariant();

        public static string KioskGroup(string? kid) => $"kiosk:{NormalizeKid(kid)}";

        public override async Task OnConnectedAsync()
        {
            var kid = NormalizeKid(Context.GetHttpContext()?.Request.Query["kid"].ToString());
            if (!string.IsNullOrWhiteSpace(kid))
                await Groups.AddToGroupAsync(Context.ConnectionId, KioskGroup(kid));
            await base.OnConnectedAsync();
        }
    }
}
