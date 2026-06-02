using Microsoft.AspNetCore.SignalR;

namespace GuestGate.Api.Hubs
{
    public class GuestHub : Hub
    {
        public static string NormalizeKid(string? kid)
        {
            var value = (kid ?? string.Empty).Trim();
            return value.Length > 0 && value.All(char.IsDigit) ? value : string.Empty;
        }

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
