using Invoices.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace AflakTicket.Pages.Vallet
{
    [Authorize]
    public class ValletTransactionsModel : PageModel
    {
        private readonly Invoices.Models.DataContext _context;
        [BindProperty(SupportsGet =true)]
        public string? Message { get; set; }
        public ValletTransactionsModel(Invoices.Models.DataContext context)
        {
            _context = context;
        }

        public IList<Transaction> Transaction { get; set; }

        public async Task OnGetAsync()
        {
            Transaction = await _context.Transactions.Include(p => p.Customer).Where(p=>p.CustomerUsername==User.Identity.Name).OrderByDescending(p=>p.Date).ToListAsync();
        }
    }
}
