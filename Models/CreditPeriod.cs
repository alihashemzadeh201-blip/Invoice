using System.ComponentModel.DataAnnotations;

namespace Invoice.Models
{
    public class CreditPeriod
    {
        public int Id { get; set; }
        [Display(Name ="عنوان")]
        public string Title { get; set; }
        [Display(Name ="تعداد ماه")]
        public int Months { get; set; }
        [Display(Name ="حداکثر تعداد ارسال فاکتور")]
        public int? LimitCount { get; set; }
        [Display(Name ="مبلغ دوره")]
        public int Price { get; set; }
    }
}
