using System.ComponentModel.DataAnnotations;
using System.Security.Principal;

namespace Invoices.Models
{
    public class KarpooshehRequest
    {
        public int Id { get; set; }
        [Display(Name ="نام و نام خانوادگی")]
        [Required(ErrorMessage =Messages.Required)]
        public string Name { get; set; }
        [Display(Name = "نام عمومی: (به انگلیسی و بدون فاصله)")]
        [RegularExpression(@"^[a-zA-Z]+$",ErrorMessage ="فیلد نام عمومی بایستی به انگلیسی وارد شود.")]
        public string? LatinName { get; set; }

        [Display(Name ="شماره ملی")]
        [Required(ErrorMessage =Messages.Required)]
        public string NationalId { get; set; }
        [Display(Name = "رمز عبور سامانه مودیان")]
        public string? TaxPassword { get; set; }

        [Display(Name ="ایمیل")]
        [EmailAddress(ErrorMessage =Messages.Invalid)]
        public string? Email { get; set; }

        [Display(Name ="عنوان شغل")]
        [Required(ErrorMessage =Messages.Required)]
        public string JobTitle { get; set; }

        [Display(Name ="شماره تماس")]
        [Required(ErrorMessage =Messages.Required)]
        [Phone(ErrorMessage =Messages.Invalid)]
        public string Phone { get; set; }

        [Display(Name ="توضیحات")]
        public string? Description { get; set; }

        [Display(Name ="نوع شخص")]
        public PersonType PersonType { get; set; }
        public DateTime RequestDate { get; set; }
        public DateTime? ReceiveDate { get; set; }
        public DateTime? FinishDate { get; set; }
        public DateTime? CancelDate { get; set; }

    }
}
