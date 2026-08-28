using Microsoft.Extensions.Caching.Memory;
using System;

namespace Invoice.Code
{
    /// <summary>
    /// محدودسازی تعداد تلاش‌های ناموفق ورود (محافظت در برابر حملات بروت‌فورس).
    /// شمارنده‌ها در حافظه نگه داشته می‌شوند و با ری‌استارت برنامه صفر می‌شوند.
    /// </summary>
    public interface ILoginThrottle
    {
        bool IsLockedOut(string username);
        TimeSpan RemainingLockout(string username);
        int RegisterFailedAttempt(string username);
        void Reset(string username);
    }

    public class LoginThrottle : ILoginThrottle
    {
        /// <summary>تعداد تلاش ناموفق مجاز پیش از قفل شدن حساب.</summary>
        public const int MaxFailedAttempts = 5;

        /// <summary>مدت زمان قفل شدن حساب پس از تلاش‌های ناموفق متوالی.</summary>
        public static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

        private static readonly TimeSpan AttemptsWindow = TimeSpan.FromMinutes(15);

        private readonly IMemoryCache _cache;

        public LoginThrottle(IMemoryCache cache)
        {
            _cache = cache;
        }

        public bool IsLockedOut(string username)
        {
            return _cache.TryGetValue(LockoutKey(username), out _);
        }

        public TimeSpan RemainingLockout(string username)
        {
            if (_cache.TryGetValue(LockoutKey(username), out DateTime until))
            {
                var remaining = until - DateTime.UtcNow;
                return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
            }

            return TimeSpan.Zero;
        }

        public int RegisterFailedAttempt(string username)
        {
            var attempts = _cache.GetOrCreate(AttemptsKey(username), entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = AttemptsWindow;
                return 0;
            });

            attempts++;

            if (attempts >= MaxFailedAttempts)
            {
                _cache.Set(LockoutKey(username), DateTime.UtcNow.Add(LockoutDuration), DateTime.UtcNow.Add(LockoutDuration));
                _cache.Remove(AttemptsKey(username));
            }
            else
            {
                _cache.Set(AttemptsKey(username), attempts, DateTime.UtcNow.Add(AttemptsWindow));
            }

            return attempts;
        }

        public void Reset(string username)
        {
            _cache.Remove(AttemptsKey(username));
            _cache.Remove(LockoutKey(username));
        }

        private static string Normalize(string username) => (username ?? string.Empty).Trim().ToLowerInvariant();

        private static string AttemptsKey(string username) => $"login-attempts:{Normalize(username)}";

        private static string LockoutKey(string username) => $"login-lockout:{Normalize(username)}";
    }
}
