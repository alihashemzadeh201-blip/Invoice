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
        private readonly ILogger<DetailsModel> _logger;

        public DetailsModel(
            DataContext context,
            IBaleUserBotService baleUserBotService,
            IUserActivityLogger activityLogger,
            ILogger<DetailsModel> logger)
        {
            _context = context;
            _baleUserBotService = baleUserBotService;
            _activityLogger = activityLogger;
            _logger = logger;
        }

        public KarpooshehRequest KarpooshehRequest { get; set; } = default!;

        [BindProperty]
        [Display(Name = "شناسه یکتای حافظه مالیاتی (۶ کاراکتر)")]
        public string MemoryId { get; set; } = string.Empty;

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

            // تعیین نام کاربری سامانه (شماره همراه یا کد ملی)
            var username = !string.IsNullOrWhiteSpace(request.Phone) && request.Phone.Trim().Length == 11
                ? request.Phone.Trim()
                : request.NationalId.Trim();

            var rawPassword = !string.IsNullOrWhiteSpace(UserPassword)
                ? UserPassword.Trim()
                : (!string.IsNullOrWhiteSpace(request.Phone) ? request.Phone.Trim() : "User@1234");

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
                profile = new Profile
                {
                    FirstName = request.Name,
                    Lastname = request.JobTitle ?? "",
                    TinCode = request.NationalId,
                    MemoryId = cleanMemoryId,
                    PostCode = request.PostalCode,
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
    }
}
