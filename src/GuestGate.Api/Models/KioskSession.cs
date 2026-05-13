using System;
namespace GuestGate.Api.Models
{
    public class KioskSession
    {
        public int Id { get; set; }
        public string Kid { get; set; } = default!;
        public Guid EditToken { get; set; }
        public int? GuestId { get; set; }
        public Guest? Guest { get; set; }
        public GuestGate.Api.Models.SessionStatus Status { get; set; }
        public DateTime ExpiresAt { get; set; }
        public string? TemplateId { get; set; }
        public string? PrefillJson { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}