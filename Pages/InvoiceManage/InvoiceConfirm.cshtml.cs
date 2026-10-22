using Invoices.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Invoice.Pages.InvoiceManage
{
    [Authorize]
    public class InvoiceConfirmModel : PageModel
    {
        DataContext _context;
        [BindProperty]
        public Invoices.Models.Invoice Invoice { get; set; }
        public DateTime? ExpireDate { get; set; }
        public int PerInvoice { get; set; }
        [BindProperty(SupportsGet = true)]
        public bool CancelInvoice { get; set; }
        [BindProperty(SupportsGet =true)]
        public string? ReturnUrl { get; set; }
        public InvoiceConfirmModel(DataContext context)
        {
            _context = context;
        }

        public async Task<IActionResult> OnGetAsync(int? id, string? irtaxid)
        {
            if (id == null && irtaxid == null) return NotFound();
            if (id != null)
                Invoice = await _context.Invoices.Include(p => p.InvoiceItems).Include(p => p.Payments).SingleOrDefaultAsync(p => p.Id == id);
            else if(irtaxid != null)
                Invoice = await _context.Invoices.Include(p => p.InvoiceItems).Include(p => p.Payments).SingleOrDefaultAsync(p => p.Taxid == irtaxid);
            if (Invoice.Sender!=User.Identity.Name)
                return Forbid();    
            var user = await _context.Users.FindAsync(User.Identity.Name);
            PerInvoice = await Utils.GetPerInvoiceCostAsync(user, _context);
            ExpireDate = user?.ExpireDate;
            return Page();
        }
    }
}
