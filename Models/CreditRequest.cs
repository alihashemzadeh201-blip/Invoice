namespace Invoice.Models
{
    public class CreditRequest
    {
        public int Id { get; set; }
        
        public string Username { get; set; }
        public string? CardNumber { get; set; }
        public DateTime Date { get; set; }
        public int PeriodId { get; set; }
        public int? Price { get; set; }
        public int? Months { get; set; }
        public string? Description { get; set; }

        public virtual CreditPeriod? Period { get; set; } 

    }
}
