using Invoices.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Invoice.Pages.InvoiceManage
{
    [Authorize]
    public class PaymentsModel : PageModel
    {
        DataContext _context;
        public List<Payment> Payments { get; set; }
        [BindProperty(SupportsGet =true)]
        public int InvoiceId { get; set; }
        public PaymentsModel(DataContext context)
        {
            _context = context;
        }
    
        public async Task<IActionResult> OnGetAsync()
        {
            var invoice = await _context.Invoices.AsNoTracking().SingleOrDefaultAsync(p => p.Id == InvoiceId);
            if (invoice == null)
                return NotFound();

            // فقط ایجادکننده فاکتور مجاز به مشاهده اطلاعات پرداخت‌های آن است
            if (invoice.Sender != User.Identity?.Name)
                return Forbid();

            Payments = await _context.Payments.Where(p => p.InvoiceId == InvoiceId).ToListAsync();
            return Page();
        }
    }
}
