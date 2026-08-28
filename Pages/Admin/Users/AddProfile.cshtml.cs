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

namespace Invoice.Pages.Admin.Users
{
    [Authorize(Roles ="admin,reseller")]
    public class AddProfileModel : PageModel
    {
        DataContext _context;
        private readonly IProfileSecretProtector _secretProtector;
        private readonly ILogger<AddProfileModel> _logger;

        public AddProfileModel(DataContext context, IProfileSecretProtector secretProtector, ILogger<AddProfileModel> logger)
        {
            _context = context;
            _secretProtector = secretProtector;
            _logger = logger;
        }

        [BindProperty]
        public IFormFile? PrivateKey { get; set; }

        /// <summary>
        /// فقط برای نمایش و دریافت مقادیر فرم. شناسه پروفایل برای تشخیص حالت ویرایش استفاده می‌شود،
        /// اما رکورد واقعی همیشه از پایگاه داده خوانده می‌شود و مالکیت آن بررسی می‌گردد.
        /// </summary>
        [BindProperty]
        public Profile Profile { get; set; } = new Profile();

        [BindProperty(SupportsGet =true)]
        public string? ReturnUrl { get; set; }

        public async Task<IActionResult> OnGetAsync(int? id)
        {
            var username = User.Identity?.Name;

            if(id != null)
            {
                Profile = await _context.Profiles.FindAsync(id);
                if (Profile == null)
                    return NotFound();

                if (Profile.Creator != username && !User.IsInRole("admin"))
                {
                    _logger.LogWarning("تلاش غیرمجاز برای مشاهده پروفایل {ProfileId} توسط کاربر {Username}", id, username);
                    return Forbid();
                }
            }
            var user = _context.Users.SingleOrDefault(p => p.Username == User.Identity.Name);
            if (user == null)
                return Forbid();

            var profilecount = _context.Profiles.Where(p => p.Creator == User.Identity.Name).Count();
            if(user.MaxProfileCount!=null && profilecount>=user.MaxProfileCount && id==null)
            {
                return Unauthorized();
            }
            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (!ModelState.IsValid)
                return Page();

            var username = User.Identity?.Name;
            if (string.IsNullOrEmpty(username))
                return Forbid();

            var isEdit = Profile.Id > 0;
            var profile = isEdit ? await _context.Profiles.FindAsync(Profile.Id) : new Profile();

            if (isEdit)
            {
                if (profile == null)
                    return NotFound();

                // فقط مالک پروفایل یا مدیر اجازه ویرایش دارد
                if (profile.Creator != username && !User.IsInRole("admin"))
                {
                    _logger.LogWarning("تلاش غیرمجاز برای ویرایش پروفایل {ProfileId} توسط کاربر {Username}", Profile.Id, username);
                    return Forbid();
                }
            }

            // کپی فقط فیلدهای مجاز - مالک (Creator) برای رکوردهای موجود تغییر داده نمی‌شود
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
                _logger.LogInformation("کلید خصوصی پروفایل {ProfileId} توسط کاربر {Username} به روز شد.", profile.Id, username);
            }

            if (!isEdit)
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

                _context.Profiles.Add(profile);
            }

            _context.SaveChanges();
            _logger.LogInformation("پروفایل {ProfileId} توسط کاربر {Username} {Action} شد.", profile.Id, username, isEdit ? "ویرایش" : "ایجاد");

            if (ReturnUrl != null)
                return Redirect(ReturnUrl);
            else
                return (RedirectToPage("Profiles"));

        }
    }
}
