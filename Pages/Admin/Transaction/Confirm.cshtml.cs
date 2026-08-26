#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Invoices.Models;
using Microsoft.AspNetCore.Authorization;

namespace AflakTicket.Pages_Admin_Transaction
{
    [Authorize(Roles="admin,counter")]
    public class ConfirmModel : PageModel
    {
        private readonly DataContext _context;

        public ConfirmModel(DataContext context)
        {
            _context = context;
        }
        [BindProperty]
        public Transaction Transaction { get; set; }
        public async Task<IActionResult> OnGetAsync(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            Transaction = await _context.Transactions
                .Include(t => t.Customer).ThenInclude(p=>p.Profile).FirstOrDefaultAsync(m => m.Id == id);
            if (Transaction == null)
            {
                return NotFound();
            }
            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            
           
            var transaction = await _context.Transactions.FindAsync(Transaction.Id);
            var user = await _context.Users.SingleOrDefaultAsync(p => p.Username == transaction.CustomerUsername);
          
            user.Vallet += transaction.Price.Value;
            transaction.Confirm= true;
            transaction.ConfirmDate = DateTime.Now;
            transaction.ByCash = Transaction.ByCash;
            await _context.SaveChangesAsync();
            return RedirectToPage("./Index");
        }
    }
}
