using Invoices.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Invoice.Pages.Admin.Users
{
    [Authorize(Roles ="admin")]
    public class IndexModel : PageModel
    {
        public List<User> Users { get; set; }
        DataContext _context;

        [BindProperty]
        public string Search { get; set; } = "";
        public IndexModel(DataContext context)
        {
            _context= context;
        }
        public void OnGet()
        {
            
            Users = _context.Users.Include(p=>p.Profile).Where(p=>p.Profile.FirstName.Contains(Search)  || p.Profile.Lastname.Contains(Search) || p.Username.Contains(Search)).ToList();
        }
        public void OnPost()
        {
            Search = Search ?? "";
            Users = _context.Users.Include(p => p.Profile).Where(p => p.Profile.FirstName.Contains(Search) || p.Profile.Lastname.Contains(Search) || p.Username.Contains(Search)).ToList();

        }
        public IActionResult OnGetDelete(string username)
        {
            var user = _context.Users.Find(username);
            if (user == null)
                return NotFound();
            _context.Users.Remove(user);
            _context.SaveChanges();
            return RedirectToPage("./Index");
        }
    }
}
