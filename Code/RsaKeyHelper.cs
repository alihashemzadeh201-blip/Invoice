using System;
using System.Security.Cryptography;

namespace Invoice.Code
{
    public class RsaKeyPair
    {
        public string PrivateKeyPem { get; set; } = string.Empty;
        public string PublicKeyPem { get; set; } = string.Empty;
    }

    public static class RsaKeyHelper
    {
        /// <summary>
        /// تولید یک جفت کلید RSA ۲۰۴۸ بیتی استاندارد سازمان امور مالیاتی کشور (PKCS#8 برای کلید خصوصی و SPKI برای کلید عمومی)
        /// </summary>
        public static RsaKeyPair GenerateKeyPair(int keySize = 2048)
        {
            using var rsa = RSA.Create(keySize);
            return new RsaKeyPair
            {
                PrivateKeyPem = rsa.ExportPkcs8PrivateKeyPem(),
                PublicKeyPem = rsa.ExportSubjectPublicKeyInfoPem()
            };
        }

        /// <summary>
        /// استخراج کلید عمومی از متن کلید خصوصی PEM در صورت مفقود بودن کلید عمومی
        /// </summary>
        public static string? TryExtractPublicKeyFromPrivateKeyPem(string? privateKeyPem)
        {
            if (string.IsNullOrWhiteSpace(privateKeyPem)) return null;
            try
            {
                using var rsa = RSA.Create();
                rsa.ImportFromPem(privateKeyPem.Trim());
                return rsa.ExportSubjectPublicKeyInfoPem();
            }
            catch
            {
                return null;
            }
        }

        public static string NormalizePem(string pem)
        {
            if (string.IsNullOrWhiteSpace(pem)) return string.Empty;
            return pem.Trim() + "\n";
        }
    }
}
