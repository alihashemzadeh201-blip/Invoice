using Invoice.Code;
using Invoices.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Nancy.Routing.Trie;

using System.Linq;

namespace Invoices.Pages
{
    [AllowAnonymous]
    public class IndexModel : PageModel
    {
        DataContext _context;
        ISelectedProfile _selectedProfile;
        IScopedFields _scopedFields;
        public int? Vallet { get; set; }
        
        public int TotalInvoices { get; set; }
        public int SuccessInvoices { get; set; }
        public int FailedInvoices { get; set; }
        public int PendingInvoices { get; set; }
        public int CancelledInvoices { get; set; }
        public int CorrectiveInvoices { get; set; }

        public IndexModel(DataContext context,ISelectedProfile selectedProfile,IScopedFields scopedFields)
        {
            _context = context;
            _selectedProfile = selectedProfile;
            _scopedFields = scopedFields;
        }

        public void OnGet()
        {
            if (User.Identity.IsAuthenticated)
            {
                var user = _context.Users.Find(User.Identity.Name);
                Vallet = user?.Vallet;

                var profileId = _selectedProfile.ProfileId;
                if(profileId != null)
                {
                    TotalInvoices = _context.Invoices.Count(i => i.ProfileId == profileId && !i.Draft);
                    SuccessInvoices = _context.Invoices.Count(i => i.ProfileId == profileId && !i.Draft && i.Status == InvoiceStatus.موفق);
                    FailedInvoices = _context.Invoices.Count(i => i.ProfileId == profileId && !i.Draft && (i.Status == InvoiceStatus.خطا_در_ارسال || i.Status == InvoiceStatus.اشکال_در_صورتحساب));
                    PendingInvoices = _context.Invoices.Count(i => i.ProfileId == profileId && !i.Draft && (i.Status == InvoiceStatus.در_انتظار_ارسال || i.Status == InvoiceStatus.در_حال_ارسال || i.Status == InvoiceStatus.در_حال_انجام));
                    
                    CancelledInvoices = _context.Invoices.Count(i => i.ProfileId == profileId && !i.Draft && i.Ins == InvoiceTopic.ابطالی);
                    CorrectiveInvoices = _context.Invoices.Count(i => i.ProfileId == profileId && !i.Draft && i.Ins == InvoiceTopic.اصلاحی);
                }
            }
        }

        public IActionResult OnPostSelectProfile(int selectedProfileId)
        {
            _selectedProfile.ProfileId = selectedProfileId;
            var referer = Request.Headers["Referer"].ToString();
            if (!string.IsNullOrEmpty(referer))
            {
                return Redirect(referer);
            }
            return RedirectToPage();
        }

        public IActionResult OnGetSetMenuState(string  menuState)
        {
            _scopedFields.MenuOpen=bool.Parse(menuState);
            return new OkResult();
        }
    }
}