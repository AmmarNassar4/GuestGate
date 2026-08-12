using System.Data;
using System.Data.Common;
using GuestGate.Api.Data;
using GuestGate.Api.Hubs;
using GuestGate.Api.Models;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace GuestGate.Api.Services;

internal sealed record CancelledConsent(int Id, int Kid, string? PdfPath);

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

    // Single atomic UPDATE ... OUTPUT: takes only row-level X locks, so concurrent
    // callers (kiosk polls, /consents endpoints, ConsentExpiryWorker) cannot deadlock
    // the way the old Serializable read-then-update transaction did.
    public static async Task<List<CancelledConsent>> CancelExpiredActiveRequestsAsync(
        AppDb db,
        DateTime nowUtc,
        TimeSpan activeLifetime,
        int? kid = null,
        CancellationToken cancellationToken = default)
    {
        var cutoffUtc = nowUtc.Subtract(activeLifetime);
        var connection = db.Database.GetDbConnection();
        await db.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = @"
UPDATE [dbo].[ConsentRequests]
   SET [Status] = N'cancelled', [UpdatedAt] = @now
OUTPUT [inserted].[Id], [inserted].[Kid], [inserted].[PdfPath]
 WHERE [Status] IN (N'waiting', N'assigned') AND [CreatedAt] <= @cutoff"
                + (kid.HasValue ? " AND [Kid] = @kid;" : ";");

            AddParameter(command, "@now", DbType.DateTime2, nowUtc);
            AddParameter(command, "@cutoff", DbType.DateTime2, cutoffUtc);
            if (kid.HasValue) AddParameter(command, "@kid", DbType.Int32, kid.Value);

            var cancelled = new List<CancelledConsent>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                cancelled.Add(new CancelledConsent(
                    reader.GetInt32(0),
                    reader.GetInt32(1),
                    reader.IsDBNull(2) ? null : reader.GetString(2)));
            }

            return cancelled;
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }

    private static void AddParameter(DbCommand command, string name, DbType type, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.DbType = type;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    public static void MarkCancelled(IEnumerable<ConsentRequest> requests, DateTime nowUtc)
    {
        foreach (var request in requests)
        {
            request.Status = ConsentStatus.Cancelled;
            request.UpdatedAt = nowUtc;
        }
    }

    public static async Task NotifyCancelledAsync(
        IHubContext<GuestHub> hub,
        IEnumerable<CancelledConsent> cancelled,
        CancellationToken cancellationToken = default)
    {
        foreach (var consent in cancelled)
        {
            await NotifyConsentChangedAsync(hub, consent.Kid, consent.Id, ConsentStatus.Cancelled, consent.PdfPath, cancellationToken);
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
