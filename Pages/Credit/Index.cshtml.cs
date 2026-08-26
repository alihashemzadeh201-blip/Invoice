using Invoice.Models;
using Invoices.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Invoice.Pages.Credit
{
    [Authorize]
    public class IndexModel : PageModel
    {
        DataContext _context;

        public List<CreditRequest> Credits { get; set; }
        public IndexModel(DataContext context)
        {
            _context = context;
        }
    
        public async Task OnGetAsync()
        {
            Credits = await _context.CreditRequests.Include(p=>p.Period).Where(p => p.Username == User.Identity.Name).OrderByDescending(p=>p.Date).ToListAsync();
        }
    }
}
