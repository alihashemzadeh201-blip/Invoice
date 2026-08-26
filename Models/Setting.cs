using Invoices.Pages.InvoiceManage;

namespace Invoice.Models
{
    public class Setting
    {
        public int Id { get; set; }
        public DateTime Date { get; set; }
        public string Title { get; set; }
        public int PerInvoicePrice { get; set; }
        public string? Role { get; set; }

    }
}
