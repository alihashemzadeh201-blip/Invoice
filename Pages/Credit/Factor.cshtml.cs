using Invoice.Models;
using Invoices.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Invoice.Pages.Credit
{
    [Authorize]
    public class FactorModel : PageModel
    {
        DataContext _context;
        public User Profile { get; set; }
        public CreditRequest CreditRequest { get; set; }
        public FactorModel(DataContext context)
        {
            _context = context;
        }

        public async Task<IActionResult> OnGetAsync(int id)
        {
            CreditRequest = await _context.CreditRequests.Include(p => p.Period).SingleOrDefaultAsync(p => p.Id == id);
            if(CreditRequest== null)
            {
                return NotFound();
            }
            if(CreditRequest.Username!=User.Identity.Name)
                return Forbid();
            Profile = await _context.Users.Include(p=>p.Profile).SingleOrDefaultAsync(p => p.Username == User.Identity.Name);
            return Page();
        }
    }
}
