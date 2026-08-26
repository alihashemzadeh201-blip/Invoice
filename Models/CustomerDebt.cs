using Invoice.Models;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Invoices.Models
{
    public class CustomerDebt
    {
        public int Id { get; set; }

        [Display(Name = "پروفایل")]
        [Required(ErrorMessage = "انتخاب پروفایل الزامی است")]
        public int ProfileId { get; set; }

        [Display(Name = "مشتری")]
        public virtual Profile? Profile { get; set; }

        [Display(Name = "مبلغ بدهی")]
        [Required(ErrorMessage = "مبلغ الزامی است")]
        [Range(1, long.MaxValue, ErrorMessage = "مبلغ باید بیشتر از صفر باشد")]
        public long Amount { get; set; }

        [Display(Name = "تاریخ ایجاد بدهی")]
        public DateTime CreatedDate { get; set; }

        [Display(Name = "تاریخ سررسید")]
        public DateTime? DueDate { get; set; }

        [Display(Name = "شرح")]
        [StringLength(500)]
        public string? Description { get; set; }

        [Display(Name = "وضعیت")]
        public DebtStatus Status { get; set; } = DebtStatus.Active;

        [NotMapped]
        [Display(Name = "تاریخ ایجاد")]
        public string? CreatedDatePersian
        {
            get
            {
                return MyDate.GetPersianDate(CreatedDate)?.ToShortDateString();
            }
            set
            {
                CreatedDate = MyDate.GetEnglishDate(value) ?? DateTime.Now;
            }
        }

        [NotMapped]
        [Display(Name = "تاریخ سررسید")]
        public string? DueDatePersian
        {
            get
            {
                return MyDate.GetPersianDate(DueDate)?.ToShortDateString();
            }
            set
            {
                DueDate = MyDate.GetEnglishDate(value);
            }
        }
    }

    public enum DebtStatus
    {
        [Display(Name = "فعال")]
        Active = 1,

        [Display(Name = "جزئی پرداخت شده")]
        PartiallyPaid = 2,

        [Display(Name = "کاملا پرداخت شده")]
        Paid = 3,

        [Display(Name = "لغو شده")]
        Cancelled = 4
    }
}
