using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Invoices.Models;

namespace Invoice.Models
{
    public class UserFavoriteStuff
    {
        public int Id { get; set; }

        [Required]
        [StringLength(100)]
        public string Username { get; set; } = string.Empty;

        [Required]
        [StringLength(100)]
        public string StuffCode { get; set; } = string.Empty;

        public DateTime AddedDate { get; set; } = DateTime.Now;

        [ForeignKey("StuffCode")]
        public virtual Stuff? Stuff { get; set; }

        [ForeignKey("Username")]
        public virtual User? User { get; set; }
    }
}
