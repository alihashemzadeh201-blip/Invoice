using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Invoices.Models;
using Invoice.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Invoice.Pages_Product
{
    [Authorize]
    public class CreateModel : PageModel
    {
        private readonly Invoices.Models.DataContext _context;
        [BindProperty(SupportsGet = true)]
        public int? Id { get; set; }

        public CreateModel(Invoices.Models.DataContext context)
        {
            _context = context;
        }

        public async Task<IActionResult>  OnGetAsync()
        {
            if (Id != null)
            {
                Product = await _context.Products.FindAsync(Id);
                if (Product == null)
                    return NotFound();
                if (Product.Creator != User.Identity.Name)
                    return Forbid();
            }
            ViewData["Measur"] = new SelectList(await _context.MeasureUnits.ToListAsync(), "Code", "Title");
            return Page();
        }

        [BindProperty]
        public Product Product { get; set; } = default!;

        // To protect from overposting attacks, see https://aka.ms/RazorPagesCRUD
        public async Task<IActionResult> OnPostAsync()
        {
            if (!ModelState.IsValid || Product == null)
            {
                ViewData["Measur"] = new SelectList(await _context.MeasureUnits.ToListAsync(), "Code", "Title");
                return Page();
            }

            var username = User.Identity?.Name;
            Product.Creator = username;
            if (Id == null)
                _context.Products.Add(Product);
            else
            {
                Product.Id = Id.Value;
                _context.Attach(Product).State = Microsoft.EntityFrameworkCore.EntityState.Modified;
            }

            // همگام‌سازی خودکار با کالاهای منتخب کاربر (بدون افزودن به جدول عمومی Stuffs)
            if (!string.IsNullOrWhiteSpace(Product.Sstid) && !string.IsNullOrWhiteSpace(username))
            {
                var existingFav = await _context.UserFavoriteStuffs
                    .FirstOrDefaultAsync(f => f.Username == username && f.StuffCode == Product.Sstid);
                if (existingFav == null)
                {
                    _context.UserFavoriteStuffs.Add(new UserFavoriteStuff
                    {
                        Username = username,
                        StuffCode = Product.Sstid,
                        AddedDate = DateTime.Now
                    });
                }
            }

            await _context.SaveChangesAsync();

            return RedirectToPage("/Stuff/Index", new { Tab = "fav" });
        }
    }
}
