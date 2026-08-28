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
        /// <summary>
        /// پیام یکسان برای نام کاربری یا رمز عبور اشتباه تا امکان شناسایی
        /// شماره‌های ثبت‌شده در سامانه از بین برود.
        /// </summary>
        private const string InvalidLoginMessage = "نام کاربری یا رمز عبور اشتباه است.";

        private readonly DataContext _context;
        private ISelectedProfile _selectedProfile;
        private readonly ILoginThrottle _loginThrottle;
        private readonly ILogger<LoginModel> _logger;

        public LoginModel(
            DataContext context, ISelectedProfile profile, ILoginThrottle loginThrottle, ILogger<LoginModel> logger)
        {
            _context = context;
            _selectedProfile = profile;
            _loginThrottle = loginThrottle;
            _logger = logger;
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
            if (!string.IsNullOrEmpty(ErrorMessage))
            {
                ModelState.AddModelError(string.Empty, ErrorMessage);
            }

            returnUrl = returnUrl ?? Url.Content("~/");

            ReturnUrl = returnUrl;
        }

        public async Task<IActionResult> OnPostAsync(string? returnUrl = null)
        {
            returnUrl = returnUrl ?? Url.Content("/InvoiceManage/Index");

            if (ModelState.IsValid)
            {
                var username = (Input.Username ?? string.Empty).Trim();

                // بررسی قفل شدن حساب پس از تلاش‌های ناموفق متوالی
                if (_loginThrottle.IsLockedOut(username))
                {
                    var remaining = _loginThrottle.RemainingLockout(username);
                    _logger.LogWarning("تلاش برای ورود به حساب قفل شده: {Username}", username);
                    ModelState.AddModelError(string.Empty,
                        $"به دلیل {LoginThrottle.MaxFailedAttempts} تلاش ناموفق متوالی، ورود موقتا غیرفعال شده است. لطفا {Math.Ceiling(remaining.TotalMinutes)} دقیقه دیگر تلاش کنید.");
                    return Page();
                }

                var u = await _context.Users.SingleOrDefaultAsync(prop => prop.Username == username);
                var check = u == null
                    ? PasswordCheckResult.Failed
                    : PasswordHashManager.Verify(u.Password, Input.Password);

                if (u == null || check == PasswordCheckResult.Failed)
                {
                    _loginThrottle.RegisterFailedAttempt(username);
                    _logger.LogWarning("ورود ناموفق برای نام کاربری {Username}", username);
                    ModelState.AddModelError(string.Empty, InvalidLoginMessage);
                    return Page();
                }

                if (check == PasswordCheckResult.NeedsRehash)
                {
                    // ارتقای تدریجی رمزهای عبور قدیمی (MD5) به PBKDF2 در اولین ورود موفق
                    u.Password = PasswordHashManager.HashPassword(Input.Password);
                    await _context.SaveChangesAsync();
                    _logger.LogInformation("رمز عبور کاربر {Username} به الگوریتم جدید منتقل شد.", u.Username);
                }

                await Identity.SignIn(HttpContext, u.Username, u.Role);
                _selectedProfile.Initialize(u.Username, u.Role);
                _loginThrottle.Reset(username);
                _logger.LogInformation("ورود موفق کاربر {Username}", u.Username);

                if (u.Username == "admin")
                {
                    return LocalRedirect("/Admin/Transaction/Index");
                }

                var setting = await _context.Settings.SingleOrDefaultAsync(p => p.Role == u.Role);
                u.PerInvoiceCost = setting?.PerInvoicePrice ?? 0;
                await _context.SaveChangesAsync();

                return LocalRedirect(returnUrl);
            }

            // If we got this far, something failed, redisplay form
            return Page();
        }
    }
}
