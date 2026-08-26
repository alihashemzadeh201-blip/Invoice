using DocumentFormat.OpenXml.Drawing.Charts;
using Invoices.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using TaxCollectData;

namespace Invoice.Pages.InvoiceManage
{
    public class InvoiceInquiryModel : PageModel
    {
        DataContext _context;
        [BindProperty]
        public string TaxId { get; set; }

        public Invoices.Models.Invoice Invoice { get; set; }
        public InvoiceInquiryModel(DataContext context)
        {
            _context= context;  
        }
        public void OnGet()
        {
        }
        public ActionResult OnPost()
        {
                var invoice = _context.Invoices.Include(p => p.InvoiceItems).Include(p => p.Payments).Include(p => p.Profile).FirstOrDefault(p => p.Taxid == TaxId && p.Profile!=null);
                var apiHelper = new InvoiceApiHelper();
                var response = apiHelper.GetLastStatus(invoice);
            Invoice = new Invoices.Models.Invoice();
                Invoice.Status = response.Status;
                Invoice.ResponseMessage = response.Response;
                return Page();

        }
    }
}
