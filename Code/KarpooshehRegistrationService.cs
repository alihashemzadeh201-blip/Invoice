using System;
using System.Threading;
using System.Threading.Tasks;
using Invoice.Models;
using Invoices.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Invoice.Code
{
    public class KarpooshehRegistrationService : IKarpooshehRegistrationService
    {
        private readonly DataContext _context;
        private readonly IProfileSecretProtector _secretProtector;
        private readonly ILogger<KarpooshehRegistrationService> _logger;

        public KarpooshehRegistrationService(
            DataContext context,
            IProfileSecretProtector secretProtector,
            ILogger<KarpooshehRegistrationService> logger)
        {
            _context = context;
            _secretProtector = secretProtector;
            _logger = logger;
        }

        public async Task<(User User, Profile Profile, bool CreatedNewUser, bool GeneratedNewKeys)> ProvisionUserAndProfileForRequestAsync(
            KarpooshehRequest request,
            string defaultPassword = "123456",
            CancellationToken ct = default)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));

            // تعیین نام کاربری: شماره همراه در اولویت اول و سپس کدملی
            var cleanPhone = (request.Phone ?? string.Empty).Trim();
            var cleanNatId = (request.NationalId ?? string.Empty).Trim();

            var username = !string.IsNullOrWhiteSpace(cleanPhone)
                ? cleanPhone
                : (!string.IsNullOrWhiteSpace(cleanNatId) ? cleanNatId : $"user_{request.Id}");

            var user = await _context.Users.Include(u => u.Profile).FirstOrDefaultAsync(u => u.Username == username, ct);
            var createdNewUser = false;
            var generatedNewKeys = false;

            if (user == null)
            {
                user = new User
                {
                    Username = username,
                    Password = PasswordHashManager.HashPassword(defaultPassword),
                    Role = "user",
                    RegisterDate = DateTime.Now,
                    BaleChatId = request.BaleChatId,
                    BaleUsername = request.BaleUsername
                };
                _context.Users.Add(user);
                createdNewUser = true;
                await _context.SaveChangesAsync(ct);
                _logger.LogInformation("کاربر جدید {Username} با رمز عبور پیش‌فرض ایجاد شد.", username);
            }
            else
            {
                if (!string.IsNullOrWhiteSpace(request.BaleChatId) && string.IsNullOrWhiteSpace(user.BaleChatId))
                {
                    user.BaleChatId = request.BaleChatId;
                    user.BaleUsername = request.BaleUsername;
                }
            }

            var profile = user.Profile;
            if (profile == null && user.ProfileId.HasValue)
            {
                profile = await _context.Profiles.FindAsync(new object[] { user.ProfileId.Value }, ct);
            }

            if (profile == null)
            {
                var keys = RsaKeyHelper.GenerateKeyPair(2048);
                generatedNewKeys = true;

                profile = new Profile
                {
                    FirstName = request.Name,
                    Lastname = request.JobTitle ?? "",
                    TinCode = cleanNatId,
                    PostCode = request.PostalCode,
                    PrivateKey = _secretProtector.Protect(keys.PrivateKeyPem),
                    PublicKey = keys.PublicKeyPem,
                    Creator = user.Username,
                    CreateDate = DateTime.Now
                };
                _context.Profiles.Add(profile);
                await _context.SaveChangesAsync(ct);

                user.ProfileId = profile.Id;
                user.Profile = profile;
                await _context.SaveChangesAsync(ct);
                _logger.LogInformation("پروفایل و کلیدهای RSA جدید برای مودی {TinCode} با موفقیت تولید و ثبت شد.", cleanNatId);
            }
            else
            {
                if (string.IsNullOrWhiteSpace(profile.PrivateKey) || string.IsNullOrWhiteSpace(profile.PublicKey))
                {
                    var keys = RsaKeyHelper.GenerateKeyPair(2048);
                    generatedNewKeys = true;
                    if (string.IsNullOrWhiteSpace(profile.PrivateKey))
                    {
                        profile.PrivateKey = _secretProtector.Protect(keys.PrivateKeyPem);
                    }
                    if (string.IsNullOrWhiteSpace(profile.PublicKey))
                    {
                        profile.PublicKey = keys.PublicKeyPem;
                    }
                    _logger.LogInformation("کلیدهای RSA مفقود برای مودی {TinCode} مجدداً تولید و ذخیره شدند.", cleanNatId);
                }

                if (string.IsNullOrWhiteSpace(profile.TinCode) && !string.IsNullOrWhiteSpace(cleanNatId))
                {
                    profile.TinCode = cleanNatId;
                }
                if (string.IsNullOrWhiteSpace(profile.PostCode) && !string.IsNullOrWhiteSpace(request.PostalCode))
                {
                    profile.PostCode = request.PostalCode;
                }
                await _context.SaveChangesAsync(ct);
            }

            // اطمینان از به‌روز بودن اشاره‌گر ProfileId روی کاربر
            if (user.ProfileId != profile.Id)
            {
                user.ProfileId = profile.Id;
                await _context.SaveChangesAsync(ct);
            }

            return (user, profile, createdNewUser, generatedNewKeys);
        }
    }
}
