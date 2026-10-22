using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Invoices.Models;
using Microsoft.AspNetCore.Authorization;

namespace Invoice.Pages_Admin_KarpooshehRequest
{
    [Authorize(Roles = "admin")]
    public class IndexModel : PageModel
    {
        private readonly Invoices.Models.DataContext _context;

        public IndexModel(Invoices.Models.DataContext context)
        {
            _context = context;
        }

        public IList<KarpooshehRequest> KarpooshehRequest { get; set; } = default!;

        public async Task OnGetAsync()
        {
            if (_context.KarpooshehRequests != null)
            {
                KarpooshehRequest = await _context.KarpooshehRequests
                    .Where(p => p.FinishDate == null)
                    .OrderByDescending(p => p.RequestDate)
                    .ToListAsync();
            }
            else
            {
                KarpooshehRequest = new List<KarpooshehRequest>();
            }
        }

        public async Task<IActionResult> OnGetReceiveAsync(int id)
        {
            var request = await _context.KarpooshehRequests.FindAsync(id);
            if (request == null)
            {
                return NotFound();
            }

            request.ReceiveDate = DateTime.Now;
            await _context.SaveChangesAsync();
            return RedirectToPage("Index");
        }

        public async Task<IActionResult> OnGetRejectAsync(int id)
        {
            return await OnGetDeleteAsync(id);
        }

        public async Task<IActionResult> OnGetDeleteAsync(int id)
        {
            var request = await _context.KarpooshehRequests.FindAsync(id);
            if (request == null)
            {
                return NotFound();
            }
            _context.KarpooshehRequests.Remove(request);
            await _context.SaveChangesAsync();
            return RedirectToPage("Index");
        }
    }
}
