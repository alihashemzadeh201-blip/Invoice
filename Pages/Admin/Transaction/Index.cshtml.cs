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

namespace Invoices.Pages_Admin_Transaction
{
    [Authorize(Roles="admin,counter")]
    public class IndexModel : PageModel
    {
        private readonly DataContext _context;

        public IndexModel(DataContext context)
        {
            _context = context;
        }

        public IList<Transaction> Transaction { get;set; }

        public async Task OnGetAsync()
        {
            Transaction = await _context.Transactions.Include(p=>p.Customer).ThenInclude(p=>p.Profile).Where(p=>p.Confirm==null || p.Confirm==false).ToListAsync();
        }

        public IActionResult OnGetDelete(int id)
        {
            var trans = _context.Transactions.Find(id);
            if (trans == null)
                return NotFound();
            _context.Transactions.Remove(trans);
            _context.SaveChanges();
            return RedirectToPage("./Index");
        }
    }
}
