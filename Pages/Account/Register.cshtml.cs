using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using Invoices.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Nancy.Security;

namespace Invoices.Pages.Account
{
    [AllowAnonymous]
    public class RegisterModel : PageModel
    {
        DataContext _context;
        private readonly ILogger<RegisterModel> _logger;

        public RegisterModel(
            DataContext context,
            ILogger<RegisterModel> logger
          )
        {
            _context = context;
            _logger = logger;

        }

        [BindProperty]
        public Invoices.Models.User Input { get; set; }

        //[BindProperty]
        //public IFormFile? PrivateKey { get; set; }
        [BindProperty(SupportsGet = true)]
        public string? ReturnUrl { get; set; }
        //public SelectList UserTypes { get; set; }
        public IList<AuthenticationScheme> ExternalLogins { get; set; }



        public async Task OnGetAsync()
        {
            if (User.Identity.IsAuthenticated)
            {
                Input = await _context.Users.SingleOrDefaultAsync(prop => prop.Username == User.Identity.Name);
            }
        }

        public async Task<IActionResult> OnPostAsync()
        {

            if (ModelState.IsValid)
            {
                //if (PrivateKey != null)
                //{
                //    string strPrivateKey = "";
                //    using (var fs = new MemoryStream())
                //    {
                //        await PrivateKey.CopyToAsync(fs);
                //        StreamReader sr = new StreamReader(fs);
                //        fs.Position = 0;
                //        var data = sr.ReadToEnd();
                //        strPrivateKey = data.Replace("-----BEGIN PRIVATE KEY-----\r\n", "").Replace("\r\n-----END PRIVATE KEY-----", "").TrimEnd('\r', '\n');
                //        strPrivateKey = strPrivateKey.Replace("-----BEGIN PRIVATE KEY-----\n", "").Replace("\n-----END PRIVATE KEY-----", "").TrimEnd('\r', '\n');
                //    }
                //    Input.Profile.PrivateKey = strPrivateKey;
                //}

                if (User.Identity?.IsAuthenticated ?? false)
                {

                    var user = await _context.Users.SingleOrDefaultAsync(p => p.Username == Input.Username);
                    //if (user.Profile == null)
                    //    user.Profile = new Invoice.Models.Profile();
                    //user.Profile.TinCode = Input.Profile.TinCode;
                    //user.Profile.PostCode = Input.Profile.PostCode;
                    //user.Profile.FirstName = Input.Profile.FirstName;
                    //user.Profile.Lastname = Input.Profile.Lastname;
                    //user.Profile.MemoryId = Input.Profile.MemoryId;
                    //if (PrivateKey != null)
                    //    user.Profile.PrivateKey = Input.Profile.PrivateKey;
                    if (Input.Password != null)
                        if (User.IsAuthenticated() && Encryptor.MD5Hash(Input.OldPassword) != user.Password)
                        {
                            ModelState.AddModelError("OldPassword", "پسورد قبلی صحیح نمی باشد");
                            return Page();
                        }
                        else
                            user.Password = Encryptor.MD5Hash(Input.Password);
                    //_context.Users.Attach(Input).State = EntityState.Modified;
                    //if (PrivateKey == null)
                    //    _context.Entry(Input).Property(p => p.PrivateKey).IsModified = false;
                    //if(Input.Password==null)
                    //    _context.Entry(Input).Property(p=>p.Password).IsModified = false;
                    //else
                    //    Input.Password=Encryptor.MD5Hash(Input.Password);
                    //_context.Entry(Input).Property(p => p.PerInvoiceCost).IsModified = false;
                    //_context.Entry(Input).Property(p => p.Vallet).IsModified = false;
                }
                else
                {
                    if (Input.Password == null)
                    {
                        ModelState.AddModelError("Password", "بایستی رمز عبور وارد شود.");
                        return Page();
                    }
                    var dbUser = _context.Users.SingleOrDefault(predicate => predicate.Username == Input.Username);
                    if (dbUser != null)
                    {
                        ModelState.AddModelError(string.Empty, "این نام کاربری قبلا ثبت شده است.");
                        return Page();
                    }
                    else
                    {
                        Input.Password = Encryptor.MD5Hash(Input.Password);
                        Input.RegisterDate = DateTime.Now;
                        _context.Users.Add(Input);

                        //if (_userManager.Options.SignIn.RequireConfirmedAccount)
                        //{
                        await Identity.SignIn(HttpContext, Input.Username, Input.Role);
                    }

                    //}
                    //else
                    //{
                    //    await _signInManager.SignInAsync(user, isPersistent: false);
                    //    return LocalRedirect(returnUrl);
                    //}
                }
                await _context.SaveChangesAsync();
                return RedirectToPage("UserProfile", new { ReturnUrl });

            }

            // If we got this far, something failed, redisplay form
            return Page();
        }
    }
}
