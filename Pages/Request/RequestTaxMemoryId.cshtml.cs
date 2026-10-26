using Invoice.Code;
using Invoice.Models;
using Invoices.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Invoice.Pages.Request
{
    public class RequestTaxMemoryIdModel : PageModel
    {
        private readonly DataContext _context;
        private readonly IBaleNotificationService _baleService;

        [BindProperty]
        public KarpooshehRequest Request { get; set; }
        [BindProperty(SupportsGet =true)]
        public int? Id { get; set; }

        public RequestTaxMemoryIdModel(DataContext context, IBaleNotificationService baleService)
        {
            _context = context;
            _baleService = baleService;
        }
    
        public async Task<ActionResult> OnGetAsync()
        {
            if (Id != null)
            {
                // ثبت درخواست جدید عمومی است، اما مشاهده یک درخواست ثبت‌شده با شناسه
                // نیازمند ورود است تا امکان مشاهده درخواست‌های سایر افراد وجود نداشته باشد.
                if (!(User.Identity?.IsAuthenticated ?? false))
                    return Challenge();

                Request = await _context.KarpooshehRequests.FindAsync(Id);
                if (Request == null)
                    return NotFound();
            }
            return Page();
        }
        public async Task<IActionResult> OnPostAsync()
        {
            if (!ModelState.IsValid)
                return Page();
            var pre = await _context.KarpooshehRequests.SingleOrDefaultAsync(p => p.NationalId == Request.NationalId);
            if (pre != null)
            {
                ModelState.AddModelError("", "با این شناسه ملی قبلا درخواست ارسال کرده اید");
                return Page();
            }
            var isNew = Id == null;
            if (isNew)
                _context.KarpooshehRequests.Add(Request);
            else
                _context.Attach(Request).State = EntityState.Modified;
            Request.RequestDate= DateTime.Now;
            await _context.SaveChangesAsync();

            if (isNew)
            {
                await _baleService.SendKarpooshehRequestNotificationAsync(Request);
            }

            return RedirectToPage("./SentSuccess",new {requestid=Request.Id});
        }
    }
}
