using System;
using System.Text.Json;
namespace GuestGate.Api.Models
{
    public record SessionStartDto(string kid, string? templateId, JsonElement? prefill);
    public record MobileSaveDto(Guid et, JsonElement data);
}