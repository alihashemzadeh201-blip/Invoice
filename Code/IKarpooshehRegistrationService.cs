using System.Threading;
using System.Threading.Tasks;
using Invoice.Models;
using Invoices.Models;

namespace Invoice.Code
{
    public interface IKarpooshehRegistrationService
    {
        Task<(User User, Profile Profile, bool CreatedNewUser, bool GeneratedNewKeys)> ProvisionUserAndProfileForRequestAsync(
            KarpooshehRequest request,
            string defaultPassword = "123456",
            CancellationToken ct = default);
    }
}
