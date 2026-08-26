using Invoice.Models;
using Invoices.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;

namespace Invoice.Pages.Admin.Users
{
    [Authorize(Roles ="admin,reseller")]
    public class ProfileModel : PageModel
    {
        [BindProperty]
        public string Search { get; set; }
        public List<Profile> Profiles { get; set; }
        DataContext _context;
        public int? MaxProfileCount { get; set; }
        public ProfileModel(DataContext context)
        {
            _context= context;
        }
        public async Task OnGetAsync()
        {
            var user = await _context.Users.SingleOrDefaultAsync(p => p.Username == User.Identity.Name);
            if (!User.IsInRole("admin"))
                Profiles =await _context.Profiles.Include(p => p.CreatorUser).Where(p => p.Creator == User.Identity.Name && p.Id!=user.ProfileId).ToListAsync();
            else
               Profiles=await _context.Profiles.Include(p => p.CreatorUser).ToListAsync();
            MaxProfileCount = user.MaxProfileCount;
        }

        public async Task OnPostAsync()
        {
            Search = Search ?? "";
            var user = await _context.Users.SingleOrDefaultAsync(p => p.Username == User.Identity.Name);
            if (!User.IsInRole("admin"))
                Profiles = await _context.Profiles.Include(p => p.CreatorUser).Where(p => p.Creator == User.Identity.Name && p.Id != user.ProfileId && (p.FirstName.Contains(Search)|| p.Lastname.Contains(Search) || p.Creator.Contains(Search))).ToListAsync();
            else
                Profiles = await _context.Profiles.Include(p => p.CreatorUser).Where(p=>p.FirstName.Contains(Search) || p.Lastname.Contains(Search) || p.Creator.Contains(Search)).ToListAsync();
            MaxProfileCount = user.MaxProfileCount;
        }
        public IActionResult OnGetDelete(int id)
        {
            var user = _context.Profiles.Find(id);
            if (user == null)
                return NotFound();
            _context.Profiles.Remove(user);
            _context.SaveChanges();
            return RedirectToPage("./Profiles");
        }
    }
}
