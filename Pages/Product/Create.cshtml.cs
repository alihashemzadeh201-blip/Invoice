using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Invoices.Models;
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
            if(Id!=null)
            {
                Product = await _context.Products.FindAsync(Id);
                if (Product == null)
                    return NotFound();
                if (Product.Creator != User.Identity.Name)
                    return Forbid();
            }
            ViewData["Measur"] =new SelectList( await _context.MeasureUnits.ToListAsync(),"Code","Title");
            return Page();
        }

        [BindProperty]
        public Product Product { get; set; } = default!;


        // To protect from overposting attacks, see https://aka.ms/RazorPagesCRUD
        public async Task<IActionResult> OnPostAsync()
        {
            if (!ModelState.IsValid  || Product == null)
            {
            ViewData["Measur"] =new SelectList( await _context.MeasureUnits.ToListAsync(),"Code","Title");
                return Page();
            }
            Product.Creator = User.Identity.Name;
            if (Id == null)
                _context.Products.Add(Product);
            else
            {
                Product.Id = Id.Value;
                _context.Attach(Product).State = Microsoft.EntityFrameworkCore.EntityState.Modified;
            }
            await _context.SaveChangesAsync();

            return RedirectToPage("./Index");
        }
    }
}
