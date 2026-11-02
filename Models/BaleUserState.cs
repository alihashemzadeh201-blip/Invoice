using System;
using System.ComponentModel.DataAnnotations;

namespace Invoice.Models
{
    public class BaleUserState
    {
        [Key]
        [MaxLength(100)]
        public string ChatId { get; set; } = string.Empty;

        [MaxLength(100)]
        public string? Username { get; set; }

        [MaxLength(100)]
        public string CurrentStep { get; set; } = "None";

        public int? ActiveProfileId { get; set; }

        public string? StateDataJson { get; set; }

        public DateTime LastUpdated { get; set; } = DateTime.Now;
    }
}
