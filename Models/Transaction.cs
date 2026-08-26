using Invoices.Models;
using System.ComponentModel.DataAnnotations;

namespace Invoices.Models
{
    public class Transaction
    {
        public int Id { get; set; }
        [Display(Name="تاریخ")]
        public DateTime Date { get; set; }
        [Display(Name = "نقدی/کارت به کارت")]
        public bool ByCash { get; set; }
        [Display(Name="مبلغ ")]
        [Required(ErrorMessage =Messages.Required)]
        [Range(100000,1000000000,ErrorMessage =Messages.MinimumRange)]
        public int? Price { get; set; }
        [Display(Name="مشتری")]
        public string? CustomerUsername { get; set; }
        [Display(Name="کد رسید بانک")]
        public string? BankTransactionReceipt { get; set; }

        [Display(Name="شماره کارت مبدا")]
        [Required(ErrorMessage =Messages.Required)]
        [CreditCard(ErrorMessage ="شماره کارت مبدا را به درستی و بدون خط تیره وارد نمایید")]
        public string SourceCardNumber { get; set; }
        public string? CheckedByUsername { get; set; }
        public bool? Confirm { get; set; }
        public DateTime? ConfirmDate { get; set; }

        [Display(Name = "توضیحات")]
        public string? Description { get;  set; }
        public virtual User? Customer { get; set; }
    }
}
