using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.Cookies;
using Invoices.Models;
using Invoice.Code;

namespace Invoices.Pages.Account
{
    [AllowAnonymous]
    public class LoginModel : PageModel
    {
        //private readonly ILogger<LoginModel> _logger;
        private readonly DataContext _context;
        private ISelectedProfile _selectedProfile;

        public LoginModel(
            DataContext context,ISelectedProfile profile )
        {
            _context = context;
            _selectedProfile = profile; 
        }

        [BindProperty]
        public InputModel Input { get; set; }

        public IList<AuthenticationScheme> ExternalLogins { get; set; }

        public string? ReturnUrl { get; set; }

        [TempData]
        public string ErrorMessage { get; set; }

        public class InputModel
        {
            [Required(ErrorMessage = Messages.Required)]
            [Display(Name = "تلفن همراه")]
            public string Username { get; set; }

            [Required(ErrorMessage = Messages.Required)]
            [DataType(DataType.Password)]
            [Display(Name = "رمز عبور")]
            public string Password { get; set; }


            [Display(Name = "مرا بخاطر بسپار؟")]
            public bool RememberMe { get; set; }

            
        }

        public async Task OnGetAsync(string? returnUrl = null)
        {
            //var users = _context.Users.Where(p => p.Role == null);
            //foreach (var item in users)
            //{
            //    item.Role = item.UserType.ToString();
            //}
            //_context.SaveChanges();
            if (!string.IsNullOrEmpty(ErrorMessage))
            {
                ModelState.AddModelError(string.Empty, ErrorMessage);
            }

            returnUrl = returnUrl ?? Url.Content("~/");
            // Clear the existing external cookie to ensure a clean login process


            ReturnUrl = returnUrl;
        }

        public async Task<IActionResult> OnPostAsync(string? returnUrl = null)
        {
            returnUrl = returnUrl ?? Url.Content("/InvoiceManage/Index");

            if (ModelState.IsValid)
            {
                // This doesn't count login failures towards account lockout
                // To enable password failures to trigger account lockout, set lockoutOnFailure: true
                var u = await _context.Users.SingleOrDefaultAsync(prop => prop.Username == Input.Username);
                if (u == null)
                {
                    ModelState.AddModelError("", "نام کاربری یافت نشد.");
                    return Page();
                }
                if (Encryptor.MD5Hash(Input.Password) == u.Password)
                {
                    await Identity.SignIn(HttpContext, Input.Username,u.Role);
                    _selectedProfile.Initialize(u.Username,u.Role);
                    if (Input.Username == "admin")
                    {
                        return LocalRedirect ("/Admin/Transaction/Index");
                    }
                    
                    var setting = await _context.Settings.SingleOrDefaultAsync(p => p.Role == u.Role);
                    u.PerInvoiceCost = setting?.PerInvoicePrice ?? 0;
                    await _context.SaveChangesAsync();
                    //if (u.PhoneVerifications.Count(p => p.IsVerified == true) == 0)
                    //    return RedirectToPage("MobileConfirmation", new { username = Input.Username, returnUrl });
                    //if (u.Profiles.Count == 0)
                    //    return RedirectToPage("/Profiles/Create");
                    //_logger.LogInformation("User logged in.");
                    return LocalRedirect(returnUrl);
                }
                else
                {
                    ModelState.AddModelError("", "نام کابری یا رمز عبور اشتباه است.");
                    return Page();
                }
                //var result = await _signInManager.PasswordSignInAsync(Input.Username, Input.Password, Input.RememberMe, lockoutOnFailure: false);
                //if (result.Succeeded)
                //{

                
                //}

                //if (result.IsLockedOut)
                //{
                //    _logger.LogWarning("حساب کاربری مسدود شده است.");
                //    return RedirectToPage("./Lockout");
                //}
                //else
                //{
                //    ModelState.AddModelError(string.Empty, "نام کاربری یا رمز عبور اشتباه است.");
                //    return Page();
                //}
            }

            // If we got this far, something failed, redisplay form
            return Page();
        }
    }
}
