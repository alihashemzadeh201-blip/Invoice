using Invoice.Models;
using Invoices.Models;
using System.ComponentModel.DataAnnotations;
using System.Xml.Linq;

namespace Invoices.Models
{
    public class InvoiceItem
    {
        public int Id { get; set; }

        public int InvoiceId { get; set; }


        [Display(Name = "شناسه کالا/خدمت")]
        [Required(ErrorMessage =Messages.Required)]
        [RegularExpression(@"^\d+$",ErrorMessage=Messages.Numeric)]
        public string? Sstid { get; set; }

        //public Stuff? Stuff { get; set; }

        [Display(Name = "شرح کالا/خدمت")]
        public string? Sstt { get; set; }


        [Display(Name = " واحد اندازهگیری")]
        public string? Mu { get; set; }
        
        public MeasureUnit? MuObject { get; set; }


        [Display(Name = " تعداد/مقدار")]
        [Required(ErrorMessage =Messages.Required)]
        [Range(0,double.MaxValue,ErrorMessage =Messages.MinimumRange)]
        public decimal? Am { get; set; }


        [Display(Name = " مبلغ واحد")]
        [Required(ErrorMessage =Messages.Required)]
        public decimal? Fee { get; set; }


        [Display(Name = " میزان ارز")]
        public decimal? Cfee
        {
            get
            {
                return Utils.GetRoundedValue( (Fee / Exr));
            }
        }


        [Display(Name = " نوع ارز")]
        public string? Cut { get; set; }
        public CurrencyCode? CutObject { get; set; }

        [Display(Name = " نرخ برابری ارز با ریال")]
        public decimal? Exr { get; set; }


        [Display(Name = " مبلغ کل")]
        public decimal? Prdis { get
            {
                var result= Utils.GetRoundedValue((Fee * Am));
                return result;
            }
        }


        [Display(Name = "  تخفیف")]
        [Required(ErrorMessage = Messages.Required)]
        public decimal? Dis { get; set; } = 0;


        [Display(Name = " مبلغ بعد از تخفیف")]
        public decimal? Adis
        {
            get
            {
                return Prdis - Dis;
            }
        }


        [Display(Name = " نرخ مالیات بر ارزش افزوده")]
        [Required(ErrorMessage =Messages.Required)]
        public VatRate Vra { get; set; }


        [Display(Name = " مبلغ مالیات بر ارزش افزوده")]
        public decimal? Vam
        {
            get
            {
                var result= Utils.GetRoundedValue((Adis * (decimal)Vra / (decimal)100.00));
                return result;
            }
        }


        [Display(Name = " موضوع سایرمالیات و عوارض")]
        public string? Odt { get; set; }


        [Display(Name = " نرخ سایرمالیات و عوارض")]
        public decimal? Odr { get; set; }


        [Display(Name = " مبلغ سایرمالیات و عوارض")]
        public decimal? Odam { get; set; }


        [Display(Name = " موضوع سایر وجوه قانونی")]
        public string? Olt { get; set; }


        [Display(Name = " نرخ سایر وجوه قانونی")]
        public decimal? Olr { get; set; }


        [Display(Name = " مبلغ سایر وجوه قانونی")]
        public decimal? Olam { get; set; }


        [Display(Name = " اجرت ساخت")]
        public decimal? Consfee { get; set; }


        [Display(Name = " سود فروشنده")]
        public decimal? Spro { get; set; }


        [Display(Name = " حق العمل")]
        public decimal? Bros { get; set; }


        [Display(Name = " جمع کل اجرت، حق العمل و سود")]
        public decimal? Tcpbs
        {
            get
            {
                return Utils.GetRoundedValue((Consfee??0) + (Spro??0) + (Bros??0));
            }
        }


        [Display(Name = " سهم نقدی از پرداخت")]
        public decimal? Cop
        {
            get
            {
                return Utils.GetRoundedValue((Tsstam * Invoice?.Cap / Invoice?.Tprdis));
            }
        }


        [Display(Name = " سهم مالیات بر ارزش افزوده از پرداخت")]
        public decimal? Vop
        {
            get
            {
                return Utils.GetRoundedValue((Vam * Invoice?.Cap / Invoice?.Tprdis));
            }
        }


        [StringLength(12)]
        [Display(Name = "شماره قرارداد معامله از طریق شخص ثالث")]
        public string? Bsrn { get; set; }


        [Display(Name = " مبلغ کل کالا/خدمت")]
        public decimal? Tsstam
        {
            get
            {
                var result= Math.Round(((Adis ?? 0) + (Vam ?? 0) + (Odam ?? 0) + (Olam ?? 0) + (Tcpbs ?? 0)));
                return result;
            }
        }

        //فیلد های مربوط به صادرات

        [Display(Name = " وزن خالص")]
        public decimal? Nw { get; set; }

        [Display(Name = " ارزش ریالی کالا")]
        public decimal? Ssrv { get; set; }

        [Display(Name = " ارزش ارزی کالا")]
        public decimal? Sscv { get; set; }

        [Display(Name = "تفاوت نرخ خرید و فروش ارز/ کارمزد  فروش ارز")]
        public decimal? pspd { get; set; }

        //فیلد های مربوط به فروش ارز

        [Display(Name = " نرخ خرید ارز")]
        public decimal? Cpr { get; set; }

        [Display(Name = " ماخذ مالیات بر ارزش افزوده در الگوی فروش ارز")]
        public long? Sovat { get; set; }


        public  Invoice? Invoice { get; set; }

    }
}
