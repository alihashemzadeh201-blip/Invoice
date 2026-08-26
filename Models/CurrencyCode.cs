namespace Invoices.Models
{
    public class CurrencyCode
    {
        public int Id { get; set; }
        public string? Title { get; set; }
        public string AlphabetCode { get; set; }
        public string NumericCode { get; set; }
        public List<InvoiceItem> InvoiceItems { get; set; }
    }
}
