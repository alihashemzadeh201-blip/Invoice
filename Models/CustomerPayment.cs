using Invoice.Models;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Invoices.Models
{
    public class CustomerPayment
    {
        public int Id { get; set; }

        [Display(Name = "پروفایل")]
        [Required(ErrorMessage = "انتخاب پروفایل الزامی است")]
        public int ProfileId { get; set; }

        [Display(Name = "پروفایل")]
        public virtual Profile? Profile { get; set; }

        [Display(Name = "مبلغ پرداخت")]
        [Required(ErrorMessage = "مبلغ الزامی است")]
        [Range(1, long.MaxValue, ErrorMessage = "مبلغ باید بیشتر از صفر باشد")]
        public long Amount { get; set; }

        [Display(Name = "تاریخ پرداخت")]
        public DateTime PaymentDate { get; set; }

        [Display(Name = "نوع پرداخت")]
        [Required(ErrorMessage = "نوع پرداخت الزامی است")]
        public PaymentMethod PaymentMethod { get; set; }

        [Display(Name = "شماره کارت/حساب")]
        [StringLength(25)]
        public string? AccountNumber { get; set; }

        [Display(Name = "شماره پیگیری/رسید")]
        [StringLength(50)]
        public string? ReferenceNumber { get; set; }

        [Display(Name = "شرح")]
        [StringLength(500)]
        public string? Description { get; set; }

        [Display(Name = "مرتبط با بدهی")]
        public int? RelatedDebtId { get; set; }

        [Display(Name = "بدهی")]
        public virtual CustomerDebt? RelatedDebt { get; set; }

        [NotMapped]
        [Display(Name = "تاریخ پرداخت")]
        public string? PaymentDatePersian
        {
            get
            {
                return MyDate.GetPersianDate(PaymentDate)?.ToShortDateString();
            }
            set
            {
                PaymentDate = MyDate.GetEnglishDate(value) ?? DateTime.Now;
            }
        }
    }

    public enum PaymentMethod
    {
        [Display(Name = "نقدی")]
        Cash = 1,

        [Display(Name = "کارت اعتباری")]
        CreditCard = 2,

        [Display(Name = "کارت به کارت")]
        CardToCard = 3,

        [Display(Name = "چک")]
        Check = 4,

        [Display(Name = "انتقال بانکی")]
        BankTransfer = 5,

        [Display(Name = "سایر")]
        Other = 6
    }
}
