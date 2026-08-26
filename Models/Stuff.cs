using Invoices.Models;
using System.ComponentModel.DataAnnotations;

namespace Invoice.Models
{
    public class Stuff
    {
        [Key]
        public string Code { get; set; }
        public string Title { get; set; }
        public StuffType Type { get; set; }
        public bool Special { get; set; }
        public bool VatTax { get; set; }

       
        public int VatRate { get; set; }
        public int VatCustomRate { get; set; }
        public List<InvoiceItem> InvoiceItems { get; set; }

    }
}
