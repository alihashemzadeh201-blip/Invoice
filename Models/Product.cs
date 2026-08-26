using System.ComponentModel.DataAnnotations;

namespace Invoices.Models
{
    public class Product
    {
        public int Id { get; set; }
        [Display(Name = "شناسه کالا/خدمت")]
        [Required(ErrorMessage =Messages.Required)]
        [RegularExpression(@"^\d{13}$",ErrorMessage =Messages.Invalid)]
        public string Sstid { get; set; }
        [Required(ErrorMessage =Messages.Required)]
        [Display(Name = "شرح کالا/خدمت")]
        public string Sstt { get; set; }
        [Display(Name = " واحد اندازهگیری")]
        public string? Mu { get; set; }
        [Display(Name = " واحد اندازهگیری")]
        public virtual MeasureUnit? MeasureUnit { get; set; }
        [Display(Name = " مبلغ واحد")]
        public decimal? Fee { get; set; }
        [Display(Name = " نرخ مالیات بر ارزش افزوده")]
        [Required(ErrorMessage =Messages.Required)]
        public VatRate Vra { get; set; }
        [Display(Name = " نوع شناسه کالا/خدمت")]
        public StuffType StuffType { get; set; }
        public string? Creator { get; set; }

        public override string ToString()
        {
            return $"{Sstt} ({StuffType})";
        }
        public string Title { get
            {
                return $"{Sstt} -- {StuffType.ToString().Replace("_"," ")} -- {Sstid}";
            }
        }

    }
}
