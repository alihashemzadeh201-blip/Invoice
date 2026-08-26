using Invoice.Models;
using Microsoft.Extensions.Logging.Abstractions;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Invoices.Models
{
    public class Invoice
    {

        public int Id { get; set; }
        public string? ReferenceCode { get; set; }
        public string? UID { get; set; }
        public InvoiceStatus? Status { get; set; }
        public string? Sender { get; set; }
        public string? ResponseMessage { get; internal set; }
        public DateTime? SendDate { get; set; }
        [Display(Name = "پروفایل")]
        public int? ProfileId { get; set; }
        public bool Draft { get; set; } = false;
        public virtual Profile? Profile { get; set; }
        ///////////////////////// Header ///////////////////////////

        [Display(Name = "شماره منحصر به فرد مالیاتی")]
        public string? Taxid { get; set; }

        [Display(Name = "تاریخ و زمان صدورفاکتور")]
        public DateTime? Indatim { get; set; }

        [NotMapped]
        [Required(ErrorMessage =Messages.Required)]
        [Display(Name = "تاریخ و زمان صدورفاکتور")]
        public string? InvoiceDate
        {
            get
            {
                return MyDate.GetPersianDate(Indatim)?.ToShortDateString();
            }
            set
            {
                Indatim = MyDate.GetEnglishDate(value);
            }
        }

        [Display(Name = " تاریخ و زمان ثبت صورتحساب")]
        [Required(ErrorMessage =Messages.Required)]
        public DateTime? Indati2m { get
            {
                return DateTime.Now;
            }
        }
        [Display(Name = " تاریخ و زمان ارسال فاکتور")]
        public string? InvoiceSendDate
        {
            get
            {
                return MyDate.GetPersianDate(Indati2m)?.ToShortDateString();
            }           
        }

        [Display(Name = " نوع فاکتور")]
        [RequiredIf("Ins","1,2,4",ErrorMessage =Messages.Required)]
        public InvoiceType? Inty { get; set; }

        [Display(Name = "سریال فاکتور داخلی حافظه  مالیاتی")]
        public string? Inno { get; set; }

        [Display(Name = "شماره منحصر به فرد مالیاتی  فاکتور مرجع")]
        [RequiredIf("Ins","2,3,4",ErrorMessage =Messages.Required)]
        public string? Irtaxid { get; set; }

        [Display(Name = " الگوی فاکتور")]
        [RequiredIf("Ins","1,2,4",ErrorMessage =Messages.Required)]
        public InvoicePattern? Inp { get; set; }

        [Display(Name = " موضوع فاکتور")]
        [Required(ErrorMessage =Messages.Required)]
        public InvoiceTopic? Ins { get; set; }

        [Display(Name = " شماره اقتصادی فروشنده")]
        [Required(ErrorMessage =Messages.Required)]
        public string? Tins { get; set; }

        [Display(Name = " نوع شخص خریدار")]
        [RequiredIf("Inty","1",ErrorMessage =Messages.Required)]
        public PersonType? Tob { get; set; }

        [Display(Name = "شماره/شناسه ملی/شناسه مشارکت/کد فراگیر خریدار")]
                [RegularExpression(@"^\d+$",ErrorMessage=Messages.Numeric)]
        public string? Bid { get; set; }

        [Display(Name = " شماره اقتصادی خریدار")]
        [RequiredIf("Inty","1",ErrorMessage =Messages.Required)]
                [RegularExpression(@"^\d+$",ErrorMessage=Messages.Numeric)]
        public string? Tinb { get; set; }

        [Display(Name = " کد شعبه فروشنده")]
                [RegularExpression(@"^\d+$",ErrorMessage=Messages.Numeric)]
        public string? Sbc { get; set; }

        [Display(Name = " کد پستی خریدار")]
                [RegularExpression(@"^\d+$",ErrorMessage=Messages.Numeric)]
        public string? Bpc { get; set; }

        [Display(Name = " کد شعبه خریدار ")]
                [RegularExpression(@"^\d+$",ErrorMessage=Messages.Numeric)]
        public string? Bbc { get; set; }

        [Display(Name = " نوع پرواز")]
        public FlightType? Ft { get; set; }

        [Display(Name = " شماره گذرنامه خریدار")]
        public string? Bpn { get; set; }

        [Display(Name = " شماره پروانه گمرکی")]
        public string? Scln { get; set; }

        [Display(Name = " کد گمرک محل اظهار فروشنده")]
        public string? Scc { get; set; }

        [Display(Name = " شناسه یکتای ثبت قرارداد فروشنده")]
        [RequiredIf("Inp","4",ErrorMessage =Messages.Required)]
        public string? Crn { get; set; }

        [Display(Name = "شماره اشتراک/ شناسه قبض بهره بردار  ")]
        public string? Billid { get; set; }

        [Display(Name = " مجموع مبلغ قبل از کسر تخفیف")]
        public decimal? Tprdis { get
            {
                if (InvoiceItems.Count == 0)
                    return null;
                return Utils.GetRoundedValue(InvoiceItems?.Sum(p => p.Prdis));
            }
        }

        [Display(Name = " مجموع تخفیفات")]
        public decimal? Tdis { get {
                if (InvoiceItems.Count == 0)
                    return null;
            return Utils.GetRoundedValue(InvoiceItems?.Sum(p => p.Dis));

            }
        }

        [Display(Name = " مجموع مبلغ پس از کسر تخفیف")]
        public decimal? Tadis { get {
                if (InvoiceItems.Count == 0)
                    return null;
                return Utils.GetRoundedValue(InvoiceItems?.Sum(p => p.Adis));
            }
        }

        [Display(Name = " مجموع مالیات بر ارزش افزوده")]
        public decimal? Tvam { get {
                if (InvoiceItems.Count == 0)
                    return null;
                return Math.Round(InvoiceItems?.Sum(p => p.Vam) ?? 0);
            }
        }

        [Display(Name = "مجموع سایر مالیات، عوارض و وجوه  قانونی")]
        public decimal? Todam
        {
            get
            {
                if (InvoiceItems.Count == 0)
                    return null;
                return Utils.GetRoundedValue((InvoiceItems?.Sum(p => p.Odam)+InvoiceItems?.Sum(p=>p.Olam)));
            }
        }

        [Display(Name = " مجموع فاکتور")]
        public decimal? Tbill
        {
            get
            {
                if (InvoiceItems.Count == 0)
                    return null;
                return Math.Round(InvoiceItems?.Sum(p => p.Tsstam) ?? 0);
            }
        }

        [Display(Name = " روش تسویه")]
        [RequiredIf("Inty","1",ErrorMessage =Messages.Required)]
        public CheckoutType? Setm { get; set; }

        [Display(Name = " مبلغ پرداختی نقدی")]
        [RequiredIf("Setm","3",ErrorMessage =Messages.Required)]
        public decimal? Cap { get; set; }

        [Display(Name = " مبلغ نسیه")]
        public decimal? Insp
        {
            get
            {
                return Tbill - Todam - Tvam - Cap;
            }
        }

        [Display(Name = "مجموع سهم مالیات بر ارزش افزوده از  پرداخت")]
        public decimal? Tvop
        {
            get
            {
                if (InvoiceItems.Count == 0)
                    return null;
                return Math.Round(InvoiceItems?.Sum(p => p.Vop) ?? 0);
            }
        }

        [Display(Name = " مالیات موضوع ماده 17")]
        public decimal? Tax17 { get; set; }

        //فیلد های مربوط به صادرات

        [Display(Name = " شماره کوتاژ اظهارنامه گمرکی")]
        public string? Cdcn { get; set; }

        [Display(Name = " تاریخ کوتاژ اظهارنامه گمرکی")]
        public int? Cdcd { get; set; }

        [NotMapped]
        [Display(Name = " تاریخ کوتاژ اظهارنامه گمرکی")]
        public string? CdcdDate
        {
            get
            {
                if (Cdcd == null)
                    return null;
                var s = Cdcd.Value.ToString();
                if (s.Length != 8)
                    return s;
                return $"{s.Substring(0, 4)}/{s.Substring(4, 2)}/{s.Substring(6, 2)}";
            }
            set
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    Cdcd = null;
                    return;
                }
                var digits = Utils.ConvertPersianNumberToEnglish(value).Replace("/", "").Trim();
                Cdcd = int.TryParse(digits, out int result) ? result : null;
            }
        }


        [Display(Name = " مجموع وزن خالص")]
        public decimal? Tonw
        {
            get
            {
                var r = Utils.GetRoundedValue(InvoiceItems?.Sum(p => p.Nw));
                return (r==0?null:r);
            }
        }

        [Display(Name = " مجموع ارزش ریالی")]
        public decimal? Torv
        {
            get
            {
                var r= Math.Round(InvoiceItems?.Sum(p => p.Ssrv) ?? 0); 
                return r==0?null:r; 
            }
        }

        [Display(Name = " مجموع ارزش ارزی")]
        public decimal? Tocv
        {
            get
            {
                var r= Math.Round(InvoiceItems?.Sum(p => p.Sscv) ?? 0);
                return r==0?null:r;  
            }
        }
        public int? ReferalId { get; set; } //شناسه صورتحساب ابطالی یا اصلاحی در صورت ابطال یا اصلاح 

        //فیلدهای جدید نسخه V7.9 دستورالعمل صدور صورتحساب

        [Display(Name = " قاعده ارسال صورتحساب")]
        public int? Insr { get; set; }

        [NotMapped]
        [Display(Name = "این صورتحساب موضوع ماده (۹) قانون پایانه‌های فروشگاهی است (ارسال با تاخیر مجاز)")]
        public bool IsArticle9
        {
            get => Insr == 1;
            set => Insr = value ? 1 : null;
        }

        [StringLength(30)]
        [Display(Name = " یادداشت ۱")]
        public string? Nti1 { get; set; }

        [StringLength(30)]
        [Display(Name = " یادداشت ۲")]
        public string? Nti2 { get; set; }

        public virtual List<InvoiceItem> InvoiceItems { get; set; } = new List<InvoiceItem>();
        public virtual List<Payment> Payments { get; set; } = new List<Payment>();
    }
}
