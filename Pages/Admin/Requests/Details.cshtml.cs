using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Invoice.Code;
using Invoice.Models;
using Invoices.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Invoice.Pages_Admin_KarpooshehRequest
{
    [Authorize(Roles = "admin")]
    public class DetailsModel : PageModel
    {
        private readonly DataContext _context;
        private readonly IBaleUserBotService _baleUserBotService;
        private readonly IUserActivityLogger _activityLogger;
        private readonly IProfileSecretProtector _secretProtector;
        private readonly IKarpooshehRegistrationService _registrationService;
        private readonly ILogger<DetailsModel> _logger;

        public DetailsModel(
            DataContext context,
            IBaleUserBotService baleUserBotService,
            IUserActivityLogger activityLogger,
            IProfileSecretProtector secretProtector,
            IKarpooshehRegistrationService registrationService,
            ILogger<DetailsModel> logger)
        {
            _context = context;
            _baleUserBotService = baleUserBotService;
            _activityLogger = activityLogger;
            _secretProtector = secretProtector;
            _registrationService = registrationService;
            _logger = logger;
        }

        public KarpooshehRequest KarpooshehRequest { get; set; } = default!;
        public User? AssociatedUser { get; set; }
        public Profile? AssociatedProfile { get; set; }

        [BindProperty]
        [Display(Name = "شناسه یکتای حافظه مالیاتی (۶ کاراکتر)")]
        public string MemoryId { get; set; } = string.Empty;

        [BindProperty]
        [Display(Name = "نام کاربری سامانه")]
        public string? SelectedUsername { get; set; }

        [BindProperty]
        [Display(Name = "رمز عبور اولیه ورود به سامانه")]
        public string? UserPassword { get; set; }

        [BindProperty]
        [Display(Name = "ارسال خودکار اعلان و مشخصات ورود در پیام‌رسان بله")]
        public bool NotifyOnBale { get; set; } = true;

        [TempData]
        public string? SuccessMessage { get; set; }

        [TempData]
        public string? ErrorMessage { get; set; }

        public async Task<IActionResult> OnGetAsync(int? id)
        {
            if (id == null || _context.KarpooshehRequests == null)
            {
                return NotFound();
            }

            var request = await _context.KarpooshehRequests.FirstOrDefaultAsync(m => m.Id == id);
            if (request == null)
            {
                return NotFound();
            }

            KarpooshehRequest = request;

            // بارگذاری کاربر و پروفایل مرتبط
            var cleanPhone = (request.Phone ?? string.Empty).Trim();
            var cleanNat = (request.NationalId ?? string.Empty).Trim();

            AssociatedUser = await _context.Users
                .Include(u => u.Profile)
                .FirstOrDefaultAsync(u => u.Username == cleanPhone || u.Username == cleanNat || (u.Profile != null && u.Profile.TinCode == cleanNat));

            if (AssociatedUser == null || AssociatedUser.Profile == null || string.IsNullOrWhiteSpace(AssociatedUser.Profile.PrivateKey))
            {
                // تولید خودکار کلیدها و ثبت‌نام در صورت عدم وجود
                try
                {
                    var (u, p, _, _) = await _registrationService.ProvisionUserAndProfileForRequestAsync(request, "123456");
                    AssociatedUser = u;
                    AssociatedProfile = p;
                }
                catch (Exception exProv)
                {
                    _logger.LogWarning(exProv, "خطا در تولید خودکار کلید برای درخواست #{RequestId}", id);
                }
            }
            else
            {
                AssociatedProfile = AssociatedUser.Profile;
            }

            if (!string.IsNullOrWhiteSpace(AssociatedProfile?.MemoryId) && string.IsNullOrWhiteSpace(MemoryId))
            {
                MemoryId = AssociatedProfile.MemoryId;
            }

            return Page();
        }

        public async Task<IActionResult> OnPostCompleteAsync(int id)
        {
            var request = await _context.KarpooshehRequests.FirstOrDefaultAsync(r => r.Id == id);
            if (request == null)
            {
                return NotFound();
            }

            KarpooshehRequest = request;

            var cleanMemoryId = (MemoryId ?? string.Empty).Trim().ToUpperInvariant();
            if (!Regex.IsMatch(cleanMemoryId, @"^[A-Z0-9]{6}$"))
            {
                ErrorMessage = "شناسه یکتای حافظه مالیاتی باید دقیقاً ۶ کاراکتر شامل حروف بزرگ انگلیسی و ارقام لاتین باشد (مانند A1B2C3).";
                return Page();
            }

            // تعیین نام کاربری سامانه (بر اساس انتخاب کاربر یا ادمین: شماره همراه یا کد ملی)
            var username = !string.IsNullOrWhiteSpace(SelectedUsername)
                ? SelectedUsername.Trim()
                : (request.Description?.Contains(request.NationalId) == true
                    ? request.NationalId.Trim()
                    : (!string.IsNullOrWhiteSpace(request.Phone) ? request.Phone.Trim() : request.NationalId.Trim()));

            var rawPassword = !string.IsNullOrWhiteSpace(UserPassword)
                ? UserPassword.Trim()
                : "123456";

            var user = await _context.Users.Include(u => u.Profile).FirstOrDefaultAsync(u => u.Username == username);
            if (user == null)
            {
                user = new User
                {
                    Username = username,
                    Password = PasswordHashManager.HashPassword(rawPassword),
                    Role = "user",
                    RegisterDate = DateTime.Now,
                    BaleChatId = request.BaleChatId,
                    BaleUsername = request.BaleUsername
                };
                _context.Users.Add(user);
            }
            else
            {
                if (!string.IsNullOrWhiteSpace(request.BaleChatId))
                {
                    user.BaleChatId = request.BaleChatId;
                    user.BaleUsername = request.BaleUsername;
                }
            }

            var profile = user.Profile;
            if (profile == null)
            {
                var keys = RsaKeyHelper.GenerateKeyPair(2048);
                profile = new Profile
                {
                    FirstName = request.Name,
                    Lastname = request.JobTitle ?? "",
                    TinCode = request.NationalId,
                    MemoryId = cleanMemoryId,
                    PostCode = request.PostalCode,
                    PrivateKey = _secretProtector.Protect(keys.PrivateKeyPem),
                    PublicKey = keys.PublicKeyPem,
                    Creator = user.Username,
                    CreateDate = DateTime.Now
                };
                _context.Profiles.Add(profile);
                user.Profile = profile;
            }
            else
            {
                profile.MemoryId = cleanMemoryId;
                if (!string.IsNullOrWhiteSpace(request.PostalCode))
                {
                    profile.PostCode = request.PostalCode;
                }
                if (string.IsNullOrWhiteSpace(profile.PrivateKey) || string.IsNullOrWhiteSpace(profile.PublicKey))
                {
                    var keys = RsaKeyHelper.GenerateKeyPair(2048);
                    if (string.IsNullOrWhiteSpace(profile.PrivateKey))
                        profile.PrivateKey = _secretProtector.Protect(keys.PrivateKeyPem);
                    if (string.IsNullOrWhiteSpace(profile.PublicKey))
                        profile.PublicKey = keys.PublicKeyPem;
                }
            }

            request.FinishDate = DateTime.Now;
            if (request.ReceiveDate == null)
            {
                request.ReceiveDate = DateTime.Now;
            }

            await _context.SaveChangesAsync();

            // ارسال اعلان خودکار به بله در صورت وجود ChatId
            var notifiedBale = false;
            if (NotifyOnBale && !string.IsNullOrWhiteSpace(request.BaleChatId))
            {
                try
                {
                    notifiedBale = await _baleUserBotService.NotifyUserTaxIdReadyAsync(
                        request.BaleChatId,
                        request.Name,
                        cleanMemoryId,
                        user.Username,
                        rawPassword);
                }
                catch (Exception exBale)
                {
                    _logger.LogError(exBale, "خطا در ارسال پیام تبریک صدور شناسه یکتا به کاربر بله.");
                }
            }

            await _activityLogger.LogAsync(
                "کارپوشه",
                "تکمیل و صدور شناسه یکتا",
                $"تکمیل درخواست کارپوشه #{id} و صدور شناسه یکتای {cleanMemoryId} برای {request.Name}" + (notifiedBale ? " (اعلان بله ارسال شد)" : ""),
                entityId: id.ToString());

            SuccessMessage = $"پرونده کارپوشه با موفقیت تکمیل شد و شناسه یکتای {cleanMemoryId} برای مودی ثبت گردید." +
                             (notifiedBale ? " پیام حاوی مشخصات ورود و شناسه یکتا نیز در پیام‌رسان بله برای کاربر ارسال شد." : "");

            return RedirectToPage(new { id });
        }

        public async Task<IActionResult> OnGetDownloadPrivateKeyAsync(int id)
        {
            var request = await _context.KarpooshehRequests.FirstOrDefaultAsync(r => r.Id == id);
            if (request == null) return NotFound();

            var cleanPhone = (request.Phone ?? string.Empty).Trim();
            var cleanNat = (request.NationalId ?? string.Empty).Trim();

            var user = await _context.Users
                .Include(u => u.Profile)
                .FirstOrDefaultAsync(u => u.Username == cleanPhone || u.Username == cleanNat || (u.Profile != null && u.Profile.TinCode == cleanNat));

            var profile = user?.Profile;
            if (profile == null || string.IsNullOrWhiteSpace(profile.PrivateKey))
            {
                // تولید خودکار در صورت عدم وجود
                var (_, p, _, _) = await _registrationService.ProvisionUserAndProfileForRequestAsync(request, "123456");
                profile = p;
            }

            var plainKey = _secretProtector.Unprotect(profile.PrivateKey);
            if (string.IsNullOrWhiteSpace(plainKey))
            {
                return NotFound("کلید خصوصی برای این درخواست یافت نشد.");
            }

            var bytes = System.Text.Encoding.UTF8.GetBytes(RsaKeyHelper.NormalizePem(plainKey));
            var filename = $"private_key_{request.NationalId ?? id.ToString()}.pem";
            return File(bytes, "application/x-pem-file", filename);
        }

        public async Task<IActionResult> OnGetDownloadPublicKeyAsync(int id)
        {
            var request = await _context.KarpooshehRequests.FirstOrDefaultAsync(r => r.Id == id);
            if (request == null) return NotFound();

            var cleanPhone = (request.Phone ?? string.Empty).Trim();
            var cleanNat = (request.NationalId ?? string.Empty).Trim();

            var user = await _context.Users
                .Include(u => u.Profile)
                .FirstOrDefaultAsync(u => u.Username == cleanPhone || u.Username == cleanNat || (u.Profile != null && u.Profile.TinCode == cleanNat));

            var profile = user?.Profile;
            if (profile == null)
            {
                var (_, p, _, _) = await _registrationService.ProvisionUserAndProfileForRequestAsync(request, "123456");
                profile = p;
            }

            var publicKey = profile.PublicKey;
            if (string.IsNullOrWhiteSpace(publicKey) && !string.IsNullOrWhiteSpace(profile.PrivateKey))
            {
                var plainPrivate = _secretProtector.Unprotect(profile.PrivateKey);
                publicKey = RsaKeyHelper.TryExtractPublicKeyFromPrivateKeyPem(plainPrivate);
                if (!string.IsNullOrWhiteSpace(publicKey))
                {
                    profile.PublicKey = publicKey;
                    await _context.SaveChangesAsync();
                }
            }

            if (string.IsNullOrWhiteSpace(publicKey))
            {
                return NotFound("کلید عمومی برای این درخواست یافت نشد.");
            }

            var bytes = System.Text.Encoding.UTF8.GetBytes(RsaKeyHelper.NormalizePem(publicKey));
            var filename = $"public_key_{request.NationalId ?? id.ToString()}.pem";
            return File(bytes, "application/x-pem-file", filename);
        }
    }
}
