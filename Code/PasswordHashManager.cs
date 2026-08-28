using System;
using System.Security.Cryptography;
using System.Text;

/// <summary>
/// نتیجه بررسی رمز عبور در برابر مقدار ذخیره شده.
/// </summary>
public enum PasswordCheckResult
{
    /// <summary>رمز عبور اشتباه است.</summary>
    Failed,

    /// <summary>رمز عبور صحیح است و با الگوریتم فعلی ذخیره شده.</summary>
    Success,

    /// <summary>
    /// رمز عبور صحیح است ولی با الگوریتم قدیمی (یا تعداد تکرار کمتر) ذخیره شده
    /// و باید با <see cref="PasswordHashManager.HashPassword"/> دوباره هش و ذخیره شود.
    /// </summary>
    NeedsRehash
}

/// <summary>
/// نگهداری امن رمز عبور با استفاده از PBKDF2-HMACSHA256 به همراه نمک تصادفی.
/// قالب رشته ذخیره شده: <c>PBKDF2$تعداد_تکرار$نمک_base64$هش_base64</c>
/// </summary>
public static class PasswordHashManager
{
    private const string Prefix = "PBKDF2";
    private const int SaltSize = 16;
    private const int KeySize = 32;
    private const int Iterations = 100_000;

    /// <summary>هش امن یک رمز عبور جدید.</summary>
    public static string HashPassword(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var key = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, KeySize);
        return string.Join('$', Prefix, Iterations, Convert.ToBase64String(salt), Convert.ToBase64String(key));
    }

    /// <summary>
    /// بررسی رمز عبور در برابر مقدار ذخیره شده. رمزهای قدیمی (MD5) نیز پذیرفته می‌شوند
    /// تا کاربران فعلی قفل نشوند، اما با برچسب <see cref="PasswordCheckResult.NeedsRehash"/>
    /// برمی‌گردند تا در اولین ورود موفق به الگوریتم جدید منتقل شوند.
    /// </summary>
    public static PasswordCheckResult Verify(string? storedHash, string? password)
    {
        if (string.IsNullOrEmpty(storedHash) || password == null)
            return PasswordCheckResult.Failed;

        var parts = storedHash.Split('$');
        if (parts.Length == 4 && parts[0] == Prefix && int.TryParse(parts[1], out var iterations) && iterations > 0)
        {
            try
            {
                var salt = Convert.FromBase64String(parts[2]);
                var expected = Convert.FromBase64String(parts[3]);
                var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);

                if (!CryptographicOperations.FixedTimeEquals(actual, expected))
                    return PasswordCheckResult.Failed;

                return iterations < Iterations ? PasswordCheckResult.NeedsRehash : PasswordCheckResult.Success;
            }
            catch (FormatException)
            {
                return PasswordCheckResult.Failed;
            }
        }

        // تطبیق با رمزهای عبور قدیمیِ ذخیره شده با MD5
        if (IsLegacyMd5(storedHash))
        {
#pragma warning disable CS0618 // فقط برای مهاجرت رمزهای قدیمی
            if (Encryptor.MD5Hash(password).Equals(storedHash, StringComparison.OrdinalIgnoreCase))
                return PasswordCheckResult.NeedsRehash;
#pragma warning restore CS0618
        }

        return PasswordCheckResult.Failed;
    }

    private static bool IsLegacyMd5(string value)
    {
        if (value.Length != 32)
            return false;

        foreach (var c in value)
            if (!Uri.IsHexDigit(c))
                return false;

        return true;
    }
}

/// <summary>
/// هش MD5 - منسوخ: دیگر برای ذخیره رمز عبور استفاده نمی‌شود و صرفاً برای
/// بررسی رمزهای قدیمی در <see cref="PasswordHashManager.Verify"/> نگه داشته شده است.
/// </summary>
public static class Encryptor
{
    [Obsolete("MD5 برای نگهداری رمز عبور ناامن است. برای هش جدید از PasswordHashManager.HashPassword استفاده کنید.")]
    public static string MD5Hash(string text)
    {
        MD5 md5 = new MD5CryptoServiceProvider();

        md5.ComputeHash(ASCIIEncoding.ASCII.GetBytes(text));

        byte[] result = md5.Hash;

        StringBuilder strBuilder = new StringBuilder();
        for (int i = 0; i < result.Length; i++)
        {
            strBuilder.Append(result[i].ToString("x2"));
        }

        return strBuilder.ToString();
    }
}
