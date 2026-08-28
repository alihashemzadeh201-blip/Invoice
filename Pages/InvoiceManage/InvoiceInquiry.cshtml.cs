using DocumentFormat.OpenXml.Drawing.Charts;
using Invoices.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using TaxCollectData;
using Invoice.Code;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;

namespace Invoice.Pages.InvoiceManage
{
    [Authorize]
    public class InvoiceInquiryModel : PageModel
    {
        DataContext _context;
        [BindProperty]
        public string TaxId { get; set; }

        public Invoices.Models.Invoice Invoice { get; set; }
        private readonly IProfileSecretProtector _secretProtector;
        private readonly ILogger<InvoiceInquiryModel> _logger;

        public InvoiceInquiryModel(DataContext context, IProfileSecretProtector secretProtector, ILogger<InvoiceInquiryModel> logger)
        {
            _context= context;
            _secretProtector = secretProtector;
            _logger = logger;
        }
        public void OnGet()
        {
        }
        public ActionResult OnPost()
        {
                var invoice = _context.Invoices.Include(p => p.InvoiceItems).Include(p => p.Payments).Include(p => p.Profile).FirstOrDefault(p => p.Taxid == TaxId && p.Profile!=null);
                var apiHelper = new InvoiceApiHelper(_secretProtector, _logger);
                var response = apiHelper.GetLastStatus(invoice);
            Invoice = new Invoices.Models.Invoice();
                Invoice.Status = response.Status;
                Invoice.ResponseMessage = response.Response;
                return Page();

        }
    }
}
