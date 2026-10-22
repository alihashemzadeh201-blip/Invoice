using Invoices.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Invoices;
using Microsoft.AspNetCore.Authorization;
using TaxCollectData;
using Microsoft.Build.Framework;
using Invoice.Code;
using Invoice.Models;
using Microsoft.Extensions.Logging;
namespace Invoice.Pages.InvoiceManage
{
    [Authorize]
    public class ImportGroupFactorConfirm : PageModel
    {
        DataContext _context;
        ISelectedProfile _profile;
        public IFormFile File { get; set; }
        public List<Invoices.Models.Invoice> Invoices { get; set; }
        private readonly IProfileSecretProtector _secretProtector;
        private readonly ILogger<ImportGroupFactorConfirm> _logger;

        public ImportGroupFactorConfirm(DataContext context,ISelectedProfile profile, IProfileSecretProtector secretProtector, ILogger<ImportGroupFactorConfirm> logger)
        {
            _context = context;
            _profile = profile;
            _secretProtector = secretProtector;
            _logger = logger;
        }

        public void OnGet()
        {
            Invoices =  _context.Invoices.Include(p=>p.InvoiceItems).Where(p => p.Sender == User.Identity.Name && p.Draft == true&& p.ProfileId==_profile.ProfileId).ToList();
            ModelState.Clear();
        }
      
        public async Task<IActionResult> OnGetDeleteAsync(int id)
        {
            var invoice = await _context.Invoices.FindAsync(id);
            if (invoice == null)
                return NotFound();

            if (invoice.Sender != User.Identity.Name)
                return Forbid(); 

            _context.Invoices.Remove(invoice);  
            _context.SaveChanges();
            return RedirectToPage("ImportInvoices");
        }

        public async Task<IActionResult> OnGetCancelAsync()
        {
            var drafts = await _context.Invoices.Where(p => p.Draft == true && p.Sender==User.Identity.Name && p.ProfileId==_profile.ProfileId).ToListAsync();
            foreach (var draft in drafts)
            {
                _context.Invoices.Remove(draft);

            }
            _context.SaveChanges();
            return RedirectToPage("Index");
        }

        public async Task<IActionResult> OnGetSendAsync()
        {
            var user = await _context.Users.FindAsync(User.Identity.Name);
            var invoices = await _context.Invoices.Where(p => p.Draft == true && p.Sender == User.Identity.Name &&  p.ProfileId == _profile.ProfileId).ToListAsync();
            var profile = await _context.Profiles.FindAsync(_profile.ProfileId);
            var responses = new InvoiceApiHelper(_secretProtector, _logger).SendInvoices(invoices, profile);
            for (int i = 0; i < responses.Count; i++)
            {
                var invoice = invoices[i];
                var response= responses[i];
                invoice.Status = response.Status;
                invoice.ReferenceCode = response.RefId;
                invoice.ResponseMessage = response.Response;
                invoice.UID = response.UID;
                invoice.Taxid = response.TaxId;
                invoice.SendDate = DateTime.Now;
                invoice.Draft = false;
                if (invoice.Status == InvoiceStatus.موفق)
                {
                    if (invoice.Ins == InvoiceTopic.اصلی)
                    {
                        if (user.ExpireDate == null || user.ExpireDate < DateTime.Now)
                        {
                            user.Vallet -= Utils.GetPerInvoiceCost(user, _context);
                            user.InvoiceSent = true;
                        }

                    }
                    else if (invoice.Ins == InvoiceTopic.ابطالی || invoice.Ins == InvoiceTopic.اصلاحی)
                    {
                        var refInvoice = _context.Invoices.SingleOrDefault(p => p.Taxid == invoice.Irtaxid);
                        if (refInvoice != null)
                        {
                            refInvoice.Status = invoice.Ins == InvoiceTopic.ابطالی ? InvoiceStatus.ابطال_شده : InvoiceStatus.اصلاح_شده;
                            refInvoice.ReferalId = invoice.Id;
                        }
                    }

                }
            }
            await _context.SaveChangesAsync();
            return RedirectToPage("Index");
        }
    }


}
