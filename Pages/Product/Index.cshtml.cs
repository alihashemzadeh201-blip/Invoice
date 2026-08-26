using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Invoices.Models;
using Microsoft.AspNetCore.Authorization;

namespace Invoice.Pages_Product
{
    [Authorize]
    public class IndexModel : PageModel
    {
        private readonly Invoices.Models.DataContext _context;
        [BindProperty]
        public string Search { get; set; }
        public IndexModel(Invoices.Models.DataContext context)
        {
            _context = context;
        }

        public IList<Product> Product { get;set; } = default!;

        public async Task OnGetAsync()
        {
            
            Search = Search ?? "";
            if (_context.Products != null)
            {
                Product = await _context.Products.Include(p=>p.MeasureUnit).Where(p=>p.Creator==User.Identity.Name ).ToListAsync();
            }
        }
        public async Task OnPostAsync()
        {
            Search = Search ?? "";

            if (_context.Products != null)
            {
                Product = await _context.Products.Include(p => p.MeasureUnit).Where(p => p.Creator == User.Identity.Name && p.Sstt.Contains(Search)).ToListAsync();
            }
        }
        public async Task<IActionResult> OnGetDeleteAsync(int id) {
            var product = await _context.Products.FindAsync(id);
            if (product == null)
            {
                return NotFound();
            }
            _context.Products.Remove(product);
            await _context.SaveChangesAsync();
            return RedirectToPage("Index");
        }
    }
}
