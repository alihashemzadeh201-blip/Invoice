using Invoices.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Invoice.Pages.InvoiceManage
{
    public class AddPaymentModel : PageModel
    {
        DataContext _context;

        [BindProperty]
        public Payment Payment { get; set; }
        public AddPaymentModel(DataContext context)
        {
            _context = context;
        }

        public async Task<IActionResult> OnGetAsync(int? id, int? invoiceId)
        {
            if (id != null)
            {
                Payment = await _context.Payments.Include(p => p.Invoice).SingleOrDefaultAsync(p => p.Id == id);
                if (Payment == null)
                {
                    return NotFound();
                }
                if (Payment.Invoice.Sender != User.Identity.Name)
                    return Forbid();

            }
            else if (invoiceId != null)
            {
                var invoice =await _context.Invoices.FindAsync(invoiceId);
                if (invoice.Sender != User.Identity.Name)
                    return Forbid();
                Payment.InvoiceId = invoiceId.Value;

            }

            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (!ModelState.IsValid)
            {
                return Page();
            }
            if(Payment.Id>0)
            {
                _context.Attach(Payment).State = Microsoft.EntityFrameworkCore.EntityState.Modified;
            }
            else
            {
                _context.Add(Payment);  
            }
            await _context.SaveChangesAsync();
            return Page();
        }
    }
}
