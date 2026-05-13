using System;
using System.Text.Json;
namespace GuestGate.Api.Models
{
    public record SessionStartDto(string kid, string? templateId, JsonElement? prefill);
    public record MobileSaveDto(Guid et, JsonElement data);
    public record ConsentCreateDto(string kid, string? guestName, string? language, string? termsEn, string? termsAr);
    public record ConsentSignDto(bool accepted, string signatureImage, string? language);
}
