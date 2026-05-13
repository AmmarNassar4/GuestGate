using System;
namespace GuestGate.Api.Models
{
    public class Guest
    {
        public int Id { get; set; }
        public string DataJson { get; set; } = "{}";
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
      }