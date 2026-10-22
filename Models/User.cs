using Invoice.Models;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Invoices.Models
{
    public class User
    {
        public User()
        {
            Role = "user";
        }
        [Key]
        [Display(Name = "شماره همراه")]
        [Required(ErrorMessage = Messages.Required)]
        [RegularExpression(@"^\d{11}$",ErrorMessage =Messages.Invalid)]
        [Phone(ErrorMessage =Messages.Invalid)]
        public string Username { get; set; }

        [Display(Name = "ایمیل")]
        [EmailAddress]
        public string? Email { get; set; }

        [StringLength(100, ErrorMessage = Messages.BetweenLength, MinimumLength = 6)]
        [DataType(DataType.Password)]
        [Display(Name = "رمز عبور")]
        public string? Password { get; set; }

        [NotMapped]
        [DataType(DataType.Password)]
        [Display(Name = "تایید رمز عبور")]
        [Compare("Password", ErrorMessage = Messages.PasswordConfirm)]
        public string? ConfirmPassword { get; set; }

        [StringLength(100, ErrorMessage = Messages.BetweenLength, MinimumLength = 6)]
        [DataType(DataType.Password)]
        [Display(Name = "رمز عبور قبلی")]
        public string? OldPassword { get; set; }
        public int? ProfileId { get; set; }
        public virtual Profile? Profile { get; set; }


        public int Vallet { get; set; }
        public DateTime RegisterDate { get; set; }

        public DateTime? LastChargeDate { get; set; }
        public DateTime? ExpireDate { get; set; }
        public int? MaxProfileCount { get; set; }
        public string Role { get; set; }
        public bool InvoiceSent { get; set; }

        [Display(Name = "هزینه هر فاکتور (ریال)")]
        public int PerInvoiceCost { get; set; }
        public string DisplayName
        {
            get
            {
                return $"{Profile?.FirstName} {Profile?.Lastname}";
            }
        }
    }
}
