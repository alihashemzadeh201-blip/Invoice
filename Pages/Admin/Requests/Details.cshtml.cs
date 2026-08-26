using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Invoices.Models;

namespace Invoice.Pages_Admin_KarpooshehRequest
{
    public class DetailsModel : PageModel
    {
        private readonly Invoices.Models.DataContext _context;

        public DetailsModel(Invoices.Models.DataContext context)
        {
            _context = context;
        }

      public KarpooshehRequest KarpooshehRequest { get; set; } = default!; 

        public async Task<IActionResult> OnGetAsync(int? id)
        {
            if (id == null || _context.KarpooshehRequests == null)
            {
                return NotFound();
            }

            var request = await _context.KarpooshehRequests.FirstOrDefaultAsync(m => m.Id == id);
            if (request == null)
            {
                return NotFound();
            }
            else 
            {
                KarpooshehRequest = request;
            }
            return Page();
        }
    }
}
