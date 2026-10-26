using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
using Invoice.Code;
using Invoices.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Invoices.Pages.Account
{
    [AllowAnonymous]
    public class RegisterModel : PageModel
    {
        private readonly DataContext _context;
        private readonly ILogger<RegisterModel> _logger;
        private readonly IBaleNotificationService _baleService;

        public RegisterModel(
            DataContext context,
            ILogger<RegisterModel> logger,
            IBaleNotificationService baleService
          )
        {
            _context = context;
            _logger = logger;
            _baleService = baleService;
        }

        /// <summary>
        /// مدل ورودی فرم: فقط فیلدهایی که کاربر مجاز به ارسال آن‌هاست.
        /// (مدل دیتابیسی User دیگر مستقیماً بایند نمی‌شود تا امکان ارسال Role،
        /// موجودی کیف پول، هزینه هر فاکتور و ... از سمت مرورگر وجود نداشته باشد)
        /// </summary>
        [BindProperty]
        public InputModel Input { get; set; } = new InputModel();

        [BindProperty(SupportsGet = true)]
        public string? ReturnUrl { get; set; }

        public IList<AuthenticationScheme> ExternalLogins { get; set; }

        public class InputModel
        {
            [Required(ErrorMessage = Messages.Required)]
            [Display(Name = "تلفن همراه")]
            public string? Username { get; set; }

            [StringLength(100, ErrorMessage = Messages.BetweenLength, MinimumLength = 6)]
            [DataType(DataType.Password)]
            [Display(Name = "رمز عبور")]
            public string? Password { get; set; }

            [DataType(DataType.Password)]
            [Display(Name = "تایید رمز عبور")]
            [Compare("Password", ErrorMessage = Messages.PasswordConfirm)]
            public string? ConfirmPassword { get; set; }

            [DataType(DataType.Password)]
            [Display(Name = "رمز عبور قبلی")]
            public string? OldPassword { get; set; }
        }

        public async Task OnGetAsync()
        {
            if (User.Identity?.IsAuthenticated ?? false)
            {
                Input.Username = User.Identity!.Name;
            }
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (!ModelState.IsValid)
                return Page();

            if (User.Identity?.IsAuthenticated ?? false)
            {
                // تغییر رمز عبور: نام کاربری از نشستِ کاربر خوانده می‌شود، نه از فرم
                var username = User.Identity!.Name;
                var user = await _context.Users.SingleOrDefaultAsync(p => p.Username == username);
                if (user == null)
                {
                    ModelState.AddModelError(string.Empty, "کاربر مورد نظر یافت نشد.");
                    return Page();
                }

                if (string.IsNullOrEmpty(Input.Password))
                {
                    ModelState.AddModelError("Input.Password", "بایستی رمز عبور جدید وارد شود.");
                    return Page();
                }

                if (PasswordHashManager.Verify(user.Password, Input.OldPassword) == PasswordCheckResult.Failed)
                {
                    _logger.LogWarning("تلاش ناموفق برای تغییر رمز عبور کاربر {Username}: رمز عبور قبلی نادرست است.", username);
                    ModelState.AddModelError("Input.OldPassword", "پسورد قبلی صحیح نمی باشد");
                    return Page();
                }

                user.Password = PasswordHashManager.HashPassword(Input.Password);
                _logger.LogInformation("رمز عبور کاربر {Username} با موفقیت تغییر کرد.", username);
            }
            else
            {
                var username = (Input.Username ?? string.Empty).Trim();

                if (string.IsNullOrEmpty(username))
                {
                    ModelState.AddModelError("Input.Username", Messages.Required);
                    return Page();
                }

                if (string.IsNullOrEmpty(Input.Password))
                {
                    ModelState.AddModelError("Input.Password", "بایستی رمز عبور وارد شود.");
                    return Page();
                }

                var dbUser = _context.Users.SingleOrDefault(predicate => predicate.Username == username);
                if (dbUser != null)
                {
                    ModelState.AddModelError(string.Empty, "این نام کاربری قبلا ثبت شده است.");
                    return Page();
                }

                var newUser = new Invoices.Models.User
                {
                    Username = username,
                    Password = PasswordHashManager.HashPassword(Input.Password),
                    RegisterDate = DateTime.Now,
                    Role = "user"   // نقش همواره سمت سرور تعیین می‌شود و از فرم پذیرفته نمی‌شود
                };

                _context.Users.Add(newUser);
                await _context.SaveChangesAsync();
                await Identity.SignIn(HttpContext, newUser.Username, newUser.Role);
                _logger.LogInformation("ثبت نام کاربر جدید {Username}", newUser.Username);
                await _baleService.SendUserRegisteredNotificationAsync(newUser);
            }

            await _context.SaveChangesAsync();
            return RedirectToPage("UserProfile", new { ReturnUrl });
        }
    }
}
