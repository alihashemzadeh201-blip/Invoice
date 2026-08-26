using Invoices.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.ComponentModel.DataAnnotations;

namespace Invoice.Pages.Admin.Users
{
    [Authorize(Roles ="admin")]
    public class SetExpireModel : PageModel
    {
        DataContext _context;

        [Display(Name ="مدت تمدید (ماه)")]
        [Required(ErrorMessage =Messages.Required)]
        [BindProperty]
        public int Months { get; set; }

        [BindProperty(SupportsGet =true)]
        public string Username { get; set; }
        public string UserDisplay { get; set; }
        public SetExpireModel(DataContext context)
        {
            _context = context;
        }
    
        public IActionResult OnGet()
        {
            var user = _context.Users.Find(Username);
            if(user == null)
            {
                return NotFound();
            }
            UserDisplay = user.DisplayName;
            return Page();
        }

        public IActionResult OnPost()
        {
            var user = _context.Users.SingleOrDefault(p => p.Username == Username);
            if (user == null)
            {
                return NotFound();

            }
            var preexpdate=user.ExpireDate??DateTime.Now;
            var newexpdate = preexpdate.AddMonths(Months);
            user.ExpireDate= newexpdate;
            user.LastChargeDate = DateTime.Now;
            _context.SaveChanges();
            return RedirectToPage("./Index");
        }
    }
}
