using Invoices.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Invoice.Pages.InvoiceManage
{
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
            Payments = await _context.Payments.Where(p => p.InvoiceId == InvoiceId).ToListAsync();
            return Page();
        }
    }
}
