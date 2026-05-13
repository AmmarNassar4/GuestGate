using System;
namespace GuestGate.Api.Models
{
    public class Template
    {
        public string Id { get; set; } = default!;
        public string DataJson { get; set; } = "{}";
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}