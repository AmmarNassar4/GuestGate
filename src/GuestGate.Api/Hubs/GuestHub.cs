using Microsoft.AspNetCore.SignalR;

namespace GuestGate.Api.Hubs
{
    public class GuestHub : Hub
    {
        public static bool TryParseKid(string? kid, out int value)
        {
            value = 0;
            var text = (kid ?? string.Empty).Trim();
            return int.TryParse(text, out value) && value > 0;
        }

        public static string KioskGroup(int kid) => $"kiosk:{kid}";

        public override async Task OnConnectedAsync()
        {
            if (TryParseKid(Context.GetHttpContext()?.Request.Query["kid"].ToString(), out var kid))
                await Groups.AddToGroupAsync(Context.ConnectionId, KioskGroup(kid));
            await base.OnConnectedAsync();
        }
    }
}
