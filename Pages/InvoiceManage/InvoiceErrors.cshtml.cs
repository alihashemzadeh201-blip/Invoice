using Invoices.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Invoice.Pages.InvoiceManage
{
    [Authorize]
    public class InvoiceErrorsModel : PageModel
    {
        DataContext _context;
        [BindProperty]
        public Invoices.Models.Invoice Invoice { get; set; }
        public DateTime? ExpireDate { get; set; }
        public int PerInvoice { get; set; }
        [BindProperty(SupportsGet =true)]
        public bool CancelInvoice { get; set; }
        public InvoiceErrorsModel(DataContext context)
        {
            _context = context;
        }
    
        public async Task<IActionResult> OnGetAsync(int id)
        {
            Invoice=await _context.Invoices.SingleOrDefaultAsync(p=>p.Id==id);
            
            var user = await _context.Users.FindAsync(User.Identity.Name);
            PerInvoice = user.PerInvoiceCost;
            ExpireDate = user.ExpireDate;
            return Page();
        }
    }
}
