using Invoice.Models;
using Invoices.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;

namespace Invoice.Pages.Account
{
    [Authorize]
    public class UserProfileModel : PageModel
    {
        DataContext _context;
        public UserProfileModel(DataContext context)
        {
            _context = context;
        }
        [BindProperty]
        public IFormFile? PrivateKey { get; set; }

        [BindProperty]
        public Profile Profile { get; set; }

        [BindProperty(SupportsGet = true)]
        public string? ReturnUrl { get; set; }

        public async Task<IActionResult> OnGetAsync(string? message)
        {
            var user = await _context.Users.Include(p => p.Profile).SingleOrDefaultAsync(p => p.Username == User.Identity.Name);
            Profile = user.Profile;
            if (message != null)
                ModelState.AddModelError("", message);
            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (ModelState.IsValid)
            {

                if (PrivateKey != null)
                {
                    string strPrivateKey = "";
                    using (var fs = new MemoryStream())
                    {
                        await PrivateKey.CopyToAsync(fs);
                        StreamReader sr = new StreamReader(fs);
                        fs.Position = 0;
                        var data = sr.ReadToEnd();
                        strPrivateKey = data.Replace("-----BEGIN PRIVATE KEY-----\r\n", "").Replace("\r\n-----END PRIVATE KEY-----", "").TrimEnd('\r', '\n');
                        strPrivateKey = strPrivateKey.Replace("-----BEGIN PRIVATE KEY-----\n", "").Replace("\n-----END PRIVATE KEY-----", "").TrimEnd('\r', '\n');
                    }
                    Profile.PrivateKey = strPrivateKey;
                }

                Profile.Creator = User.Identity.Name;
                Profile.CreateDate = DateTime.Now;
                if (Profile.Id > 0)
                {
                    _context.Attach(Profile).State = Microsoft.EntityFrameworkCore.EntityState.Modified;
                }
                else
                {
                    if (Profile.TinCode != null)
                    {
                        var profile = await _context.Profiles.SingleOrDefaultAsync(p => p.TinCode == Profile.TinCode && p.Creator == User.Identity.Name);
                        if (profile != null)
                        {
                            ModelState.AddModelError("TinCode", "این کد اقتصادی قبلا برای پروفایل دیگری تعریف شده است");
                            return Page();
                        }
                    }
                    //_context.Add(Profile);
                    var user = await _context.Users.Include(p => p.Profile).SingleOrDefaultAsync(p => p.Username == User.Identity.Name);
                    user.Profile = Profile;
                }
                if (PrivateKey == null)
                {
                    _context.Entry(Profile).Property("PrivateKey").IsModified = false;
                }
                _context.SaveChanges();
                if (ReturnUrl != null)
                    return Redirect(ReturnUrl);
                else
                    return (RedirectToPage("/Index"));

            }
            return Page();

        }
    }
}
