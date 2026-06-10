using System;
using System.Text.Json;
namespace GuestGate.Api.Models
{
    public record SessionStartDto(int kid, string? templateId, JsonElement? prefill);
    public record MobileSaveDto(Guid et, JsonElement data);
    public record ConsentCreateDto(JsonElement kid, string? guestName, string? identityNumber, string? language, string? checkInTime, string? termsEn, string? termsAr);
    public record ConsentSignDto(bool accepted, string signatureImage, string? language);
}
