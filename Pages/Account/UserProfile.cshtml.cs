using Invoice.Code;
using Invoice.Models;
using Invoices.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;

namespace Invoice.Pages.Account
{
    [Authorize]
    public class UserProfileModel : PageModel
    {
        DataContext _context;
        private readonly IProfileSecretProtector _secretProtector;
        private readonly ILogger<UserProfileModel> _logger;

        public UserProfileModel(DataContext context, IProfileSecretProtector secretProtector, ILogger<UserProfileModel> logger)
        {
            _context = context;
            _secretProtector = secretProtector;
            _logger = logger;
        }

        [BindProperty]
        public IFormFile? PrivateKey { get; set; }

        /// <summary>
        /// فقط برای نمایش و دریافت مقادیر فرم استفاده می‌شود. شناسه (Id) و مالک (Creator)
        /// هرگز از این مدل خوانده نمی‌شوند تا امکان دستکاری پروفایل سایر مودیان وجود نداشته باشد.
        /// </summary>
        [BindProperty]
        public Profile Profile { get; set; } = new Profile();

        [BindProperty(SupportsGet = true)]
        public string? ReturnUrl { get; set; }

        public async Task<IActionResult> OnGetAsync(string? message)
        {
            var user = await _context.Users.Include(p => p.Profile).SingleOrDefaultAsync(p => p.Username == User.Identity.Name);
            if (user == null)
                return Forbid();

            Profile = user.Profile ?? new Profile();
            if (message != null)
                ModelState.AddModelError("", message);
            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (!ModelState.IsValid)
                return Page();

            var username = User.Identity?.Name;
            if (string.IsNullOrEmpty(username))
                return Forbid();

            var user = await _context.Users.Include(p => p.Profile).SingleOrDefaultAsync(p => p.Username == username);
            if (user == null)
                return Forbid();

            var isNew = user.Profile == null;
            var profile = user.Profile ?? new Profile();

            // کپی فقط فیلدهای مجاز - Id و Creator از ورودی کاربر پذیرفته نمی‌شوند
            profile.FirstName = Profile.FirstName;
            profile.Lastname = Profile.Lastname;
            profile.TinCode = Profile.TinCode;
            profile.MemoryId = Profile.MemoryId;
            profile.PostCode = Profile.PostCode;
            profile.BranchCode = Profile.BranchCode;
            profile.RepresentativeCode = Profile.RepresentativeCode;
            profile.InvoicePattern = Profile.InvoicePattern;

            if (PrivateKey != null)
            {
                profile.PrivateKey = _secretProtector.Protect(await Utils.ReadPrivateKeyFile(PrivateKey));
                _logger.LogInformation("کلید خصوصی مودی برای کاربر {Username} به روز شد.", username);
            }

            if (isNew)
            {
                profile.Creator = username;
                profile.CreateDate = DateTime.Now;

                if (profile.TinCode != null)
                {
                    var duplicate = await _context.Profiles.SingleOrDefaultAsync(p => p.TinCode == profile.TinCode && p.Creator == username);
                    if (duplicate != null)
                    {
                        ModelState.AddModelError("Profile.TinCode", "این کد اقتصادی قبلا برای پروفایل دیگری تعریف شده است");
                        return Page();
                    }
                }

                user.Profile = profile;
            }

            await _context.SaveChangesAsync();
            _logger.LogInformation("پروفایل مودی کاربر {Username} ذخیره شد.", username);

            if (ReturnUrl != null)
                return Redirect(ReturnUrl);

            return RedirectToPage("/Index");
        }

        public async Task<IActionResult> OnGetDownloadPrivateKeyAsync()
        {
            var username = User.Identity?.Name;
            if (string.IsNullOrEmpty(username)) return Forbid();

            var user = await _context.Users.Include(p => p.Profile).SingleOrDefaultAsync(p => p.Username == username);
            if (user?.Profile?.PrivateKey == null)
            {
                return NotFound("کلید خصوصی برای این کاربر ثبت نشده است.");
            }

            var plainKey = _secretProtector.Unprotect(user.Profile.PrivateKey);
            if (string.IsNullOrWhiteSpace(plainKey))
            {
                return NotFound("کلید خصوصی معتبر نیست.");
            }

            var bytes = System.Text.Encoding.UTF8.GetBytes(RsaKeyHelper.NormalizePem(plainKey));
            var filename = $"private_key_{user.Profile.TinCode ?? username}.pem";
            return File(bytes, "application/x-pem-file", filename);
        }

        public async Task<IActionResult> OnGetDownloadPublicKeyAsync()
        {
            var username = User.Identity?.Name;
            if (string.IsNullOrEmpty(username)) return Forbid();

            var user = await _context.Users.Include(p => p.Profile).SingleOrDefaultAsync(p => p.Username == username);
            if (user?.Profile == null)
            {
                return NotFound("پروفایل برای این کاربر یافت نشد.");
            }

            var publicKey = user.Profile.PublicKey;
            if (string.IsNullOrWhiteSpace(publicKey) && !string.IsNullOrWhiteSpace(user.Profile.PrivateKey))
            {
                var plainPrivate = _secretProtector.Unprotect(user.Profile.PrivateKey);
                publicKey = RsaKeyHelper.TryExtractPublicKeyFromPrivateKeyPem(plainPrivate);
                if (!string.IsNullOrWhiteSpace(publicKey))
                {
                    user.Profile.PublicKey = publicKey;
                    await _context.SaveChangesAsync();
                }
            }

            if (string.IsNullOrWhiteSpace(publicKey))
            {
                return NotFound("کلید عمومی برای این مودی ثبت نشده است.");
            }

            var bytes = System.Text.Encoding.UTF8.GetBytes(RsaKeyHelper.NormalizePem(publicKey));
            var filename = $"public_key_{user.Profile.TinCode ?? username}.pem";
            return File(bytes, "application/x-pem-file", filename);
        }
    }
}
