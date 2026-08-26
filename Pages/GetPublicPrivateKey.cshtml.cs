using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Security.Cryptography;

namespace Invoice.Pages
{
    public class GetPublicPrivateKeyModel : PageModel
    {
        public void OnGet()
        {
            using (RSA rsa = RSA.Create())
            {
                // Get the public and private key
                RSAParameters publicKey = rsa.ExportParameters(false); // Public key
                RSAParameters privateKey = rsa.ExportParameters(true); // Private key

                // Convert keys to strings (for example, to store or transmit)
                string publicKeyString = Convert.ToBase64String(publicKey.Modulus);
                string privateKeyString = Convert.ToBase64String(privateKey.D);

                // Display keys
                Console.WriteLine("Public key: " + publicKeyString);
                Console.WriteLine("Private key: " + privateKeyString);
            }
        }
    }
}
