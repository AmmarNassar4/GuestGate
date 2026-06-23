using GuestGate.Api.Data;
using GuestGate.Api.Hubs;
using GuestGate.Api.Models;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace GuestGate.Api.Services;

internal static class ConsentRequestMaintenance
{
    public static bool IsActive(ConsentStatus status)
    {
        return status is ConsentStatus.Waiting or ConsentStatus.Assigned;
    }

    public static bool IsExpired(ConsentRequest request, DateTime nowUtc, TimeSpan activeLifetime)
    {
        return IsActive(request.Status) && request.CreatedAt <= nowUtc.Subtract(activeLifetime);
    }

    public static DateTime GetExpiresAtUtc(ConsentRequest request, TimeSpan activeLifetime)
    {
        return request.CreatedAt.Add(activeLifetime);
    }

    public static async Task<List<ConsentRequest>> MarkExpiredActiveRequestsAsync(
        AppDb db,
        DateTime nowUtc,
        TimeSpan activeLifetime,
        int? kid = null,
        CancellationToken cancellationToken = default)
    {
        var cutoffUtc = nowUtc.Subtract(activeLifetime);
        var query = db.ConsentRequests
            .Where(x => (x.Status == ConsentStatus.Waiting || x.Status == ConsentStatus.Assigned) && x.CreatedAt <= cutoffUtc);

        if (kid.HasValue)
        {
            query = query.Where(x => x.Kid == kid.Value);
        }

        var requests = await query
            .OrderBy(x => x.Id)
            .ToListAsync(cancellationToken);

        MarkCancelled(requests, nowUtc);
        return requests;
    }

    public static void MarkCancelled(IEnumerable<ConsentRequest> requests, DateTime nowUtc)
    {
        foreach (var request in requests)
        {
            request.Status = ConsentStatus.Cancelled;
            request.UpdatedAt = nowUtc;
        }
    }

    public static async Task NotifyConsentChangedAsync(
        IHubContext<GuestHub> hub,
        ConsentRequest request,
        CancellationToken cancellationToken = default)
    {
        await NotifyConsentChangedAsync(hub, request.Kid, request.Id, request.Status, request.PdfPath, cancellationToken);
    }

    public static Task NotifyConsentChangedAsync(
        IHubContext<GuestHub> hub,
        int kid,
        int consentId,
        ConsentStatus status,
        string? pdfPath = null,
        CancellationToken cancellationToken = default)
    {
        return hub.Clients.Group(GuestHub.KioskGroup(kid)).SendAsync("consentChanged", new
        {
            kid = kid.ToString(),
            consentId,
            status = status.ToString(),
            pdfPath
        }, cancellationToken);
    }
}
