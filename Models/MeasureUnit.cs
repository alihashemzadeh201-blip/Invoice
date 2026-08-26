namespace Invoices.Models
{
    public class MeasureUnit
    {
        public int Id { get; set; }
        public string Code { get; set; }
        public string Title { get; set; }
        public float? Order { get; set; }
        public override string ToString()
        {
            return Code;
        }
        public List<InvoiceItem> InvoiceItems { get; set; }
    }
}
