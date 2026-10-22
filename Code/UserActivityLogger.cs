using System;
using System.Threading;
using System.Threading.Tasks;
using Invoices.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Invoice.Code
{
    public interface IUserActivityLogger
    {
        Task LogAsync(string category, string action, string description, string? entityId = null, string status = "موفق", CancellationToken ct = default);
        Task LogInvoiceActionAsync(string action, int invoiceId, string? taxId, string description, string status = "موفق", CancellationToken ct = default);
    }

    public class UserActivityLogger : IUserActivityLogger
    {
        private readonly DataContext _context;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly ISelectedProfile _selectedProfile;
        private readonly ILogger<UserActivityLogger> _logger;

        public UserActivityLogger(
            DataContext context,
            IHttpContextAccessor httpContextAccessor,
            ISelectedProfile selectedProfile,
            ILogger<UserActivityLogger> logger)
        {
            _context = context;
            _httpContextAccessor = httpContextAccessor;
            _selectedProfile = selectedProfile;
            _logger = logger;
        }

        public async Task LogAsync(string category, string action, string description, string? entityId = null, string status = "موفق", CancellationToken ct = default)
        {
            try
            {
                var httpContext = _httpContextAccessor?.HttpContext;
                var username = httpContext?.User?.Identity?.Name ?? "سیستم";

                string? role = null;
                if (httpContext?.User != null)
                {
                    if (httpContext.User.IsInRole("admin")) role = "admin";
                    else if (httpContext.User.IsInRole("reseller")) role = "reseller";
                    else if (httpContext.User.IsInRole("user")) role = "user";
                }

                int? profileId = null;
                string? profileTitle = null;

                try
                {
                    profileId = _selectedProfile?.ProfileId;
                    var profile = _selectedProfile?.Profile;
                    if (profile != null)
                    {
                        profileTitle = profile.DisplayName;
                    }
                }
                catch
                {
                    // در صورت بروز خطا در دریافت پروفایل، ادامه بدون عنوان پروفایل
                }

                var ip = httpContext?.Connection?.RemoteIpAddress?.ToString();

                var log = new UserActivityLog
                {
                    Timestamp = DateTime.Now,
                    Username = username,
                    UserRole = role,
                    ProfileId = profileId,
                    ProfileTitle = profileTitle,
                    Category = category,
                    Action = action,
                    Description = description,
                    EntityId = entityId,
                    IpAddress = ip,
                    Status = status
                };

                _context.UserActivityLogs.Add(log);
                await _context.SaveChangesAsync(ct);
            }
            catch (Exception ex)
            {
                // ثبت لاگ به عنوان عملیات جانبی، هرگز نباید مانع فعالیت اصلی کاربر شود
                _logger.LogError(ex, "خطا در ثبت لاگ فعالیت کاربر: {Action} - {Category}", action, category);
            }
        }

        public async Task LogInvoiceActionAsync(string action, int invoiceId, string? taxId, string description, string status = "موفق", CancellationToken ct = default)
        {
            var entityDesc = !string.IsNullOrEmpty(taxId)
                ? $"شناسه فاکتور: {invoiceId} (کد مالیاتی: {taxId})"
                : $"شناسه فاکتور: {invoiceId}";

            var fullDesc = string.IsNullOrEmpty(description) ? entityDesc : $"{entityDesc} | {description}";
            await LogAsync("فاکتور", action, fullDesc, entityId: invoiceId.ToString(), status: status, ct: ct);
        }
    }
}
