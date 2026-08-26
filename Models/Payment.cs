using Humanizer;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Xml.Linq;

namespace Invoices.Models
{
    public class Payment
    {
        public int Id { get; set; }
        public int InvoiceId { get; set; }
        [Display(Name = " شماره سوییچ پرداخت")]
        public string Iinn { get; set; }

        [Display(Name = " شماره پذیرنده فروشگاهی")]

        public string Acn { get; set; }

        [Display(Name = " شماره پایانه")]

        public string Trmn { get; set; }

        [Display(Name = " شماره پیگیری/شماره مرجع")]

        public string Trn { get; set; }

        [Display(Name = "شماره کارت پرداخت کننده  فاکتور")]
        public string Pcn { get; set; }

        [Display(Name = "شماره/شناسه ملی/کد فراگیر اتباع غیر  ایرانی پرداخت کننده فاکتور")]

        public string Pid { get; set; }

        [Display(Name = " تاریخ و زمان پرداخت ")]

        public DateTime? Pdt { get; set; }
        [Display(Name = " تاریخ و زمان پرداخت ")]
        [NotMapped]
        public string? PayDate
        {
            get
            {
                return MyDate.GetPersianDate(Pdt)?.ToShortDateString();
            }
            set
            {
                Pdt = MyDate.GetEnglishDate(value);
            }
        }

        [Display(Name = " روش پرداخت")]

        public PayType? Pmt { get; set; }

        [Display(Name = " مبلغ پرداختی")]

        public long? Pv { get; set; }


        public virtual Invoice Invoice { get; set; }

    }
}
