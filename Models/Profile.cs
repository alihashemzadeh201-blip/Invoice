using Invoices.Models;
using System.ComponentModel.DataAnnotations;
using System.Xml.Linq;

namespace Invoice.Models
{
    public class Profile
    {
        public int Id { get; set; }
        [Display(Name = "نام")]
        [Required(ErrorMessage = Messages.Required)]
        public string FirstName { get; set; }

        [Display(Name = "نام خانوادگی")]
        public string? Lastname { get; set; }
        [Display(Name = "کد اقتصادی (برای مودیان حقوقی شناسه ملی و برای اشخاص حقیقی کد ملی + 4 رقم)")]
        [StringLength(14,MinimumLength =11, ErrorMessage =Messages.Invalid)]
        [RegularExpression(@"^-?\d+$",ErrorMessage =Messages.Invalid)]
        public string? TinCode { get; set; }

        [Display(Name = "کلید خصوصی")]
        public string? PrivateKey { get; set; }

        [Display(Name = "کلید عمومی")]
        public string? PublicKey { get; set; }

        [Display(Name = "شناسه یکتای حافظه مالیاتی")]
        [RegularExpression(@"^[A-Z0-9]{6}$",ErrorMessage =Messages.Invalid)]
        public string? MemoryId { get; set; }

        [Display(Name = "الگوی فاکتور")]
        public InvoicePattern? InvoicePattern { get; set; }

        [Display(Name = "کد پستی")]
        [RegularExpression(@"^\d{10}$",ErrorMessage =Messages.Invalid)]

        public string? PostCode { get; set; }
        [Display(Name = "کد شعبه(در صورت وجود)")]
        [RegularExpression(@"^\d{4}$",ErrorMessage =Messages.Invalid)]
        public string? BranchCode { get; set; }
        [Display(Name = "کد معرف (چنانچه کسی ما را به شما معرفی کرده ) ")]
        public string? RepresentativeCode { get; set; }
        public string? Creator { get; set; }
        public string DisplayName { get
            {
                return $"{FirstName} {Lastname} ({TinCode})";
            }
        }
        public User? CreatorUser { get; set; }
        public DateTime? CreateDate { get; set; }

        public override string ToString()
        {
            return $"{FirstName} {Lastname}";
        }
    }
}
