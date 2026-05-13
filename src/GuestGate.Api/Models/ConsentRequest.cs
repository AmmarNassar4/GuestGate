using System;

namespace GuestGate.Api.Models
{
    public class ConsentRequest
    {
        public int Id { get; set; }
        public string Kid { get; set; } = default!;
        public string GuestName { get; set; } = string.Empty;
        public string Language { get; set; } = "en";
        public string TermsEn { get; set; } = string.Empty;
        public string TermsAr { get; set; } = string.Empty;
        public string Status { get; set; } = "waiting";
        public bool Accepted { get; set; }
        public string? SignatureImageDataUrl { get; set; }
        public string? PdfPath { get; set; }
        public DateTime? SignedAt { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}
