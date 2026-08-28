using Microsoft.AspNetCore.DataProtection;

namespace Invoice.Code
{
    /// <summary>
    /// رمزنگاری/رمزگشایی داده‌های بسیار حساسِ مودیان (مانند کلید خصوصی امضای مالیاتی)
    /// پیش از ذخیره شدن در پایگاه داده.
    /// </summary>
    public interface IProfileSecretProtector
    {
        string? Protect(string? plainValue);
        string? Unprotect(string? storedValue);
    }

    public class ProfileSecretProtector : IProfileSecretProtector
    {
        private readonly IDataProtector _protector;

        public ProfileSecretProtector(IDataProtectionProvider provider)
        {
            _protector = provider.CreateProtector("Invoice.Profile.PrivateKey.v1");
        }

        public string? Protect(string? plainValue)
        {
            if (string.IsNullOrWhiteSpace(plainValue))
                return plainValue;

            return _protector.Protect(plainValue);
        }

        /// <summary>
        /// رمزگشایی مقدار ذخیره شده. اگر مقدار قابل رمزگشایی نباشد (مقادیر قدیمی که پیش از این
        /// به صورت متن ساده ذخیره شده‌اند)، همان مقدار اولیه برگردانده می‌شود تا روند فعلی مختل نشود؛
        /// این مقادیر در اولین ذخیره بعدی به صورت رمزنگاری شده نوشته می‌شوند.
        /// </summary>
        public string? Unprotect(string? storedValue)
        {
            if (string.IsNullOrWhiteSpace(storedValue))
                return storedValue;

            try
            {
                return _protector.Unprotect(storedValue);
            }
            catch (Exception)
            {
                return storedValue;
            }
        }
    }
}
