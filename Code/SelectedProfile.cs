using Invoice.Models;
using Invoices.Models;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Nancy.Security;
using System.Security.Claims;

namespace Invoice.Code
{

    public interface ISelectedProfile
    {
        public abstract int? ProfileId { get; set; }
        public abstract Profile Profile { get; }
        public abstract List<Profile> GetProfiles(string username);
        public abstract void Initialize(string username,string role);
    }
    public class SelectedProfile : ISelectedProfile
    {

        private DataContext _context;
        private HttpContext? _httpContext;
        private Profile? profile;

        public SelectedProfile(DataContext dataContext, IHttpContextAccessor httpContext)
        {
            _context = dataContext;
            _httpContext = httpContext?.HttpContext;
        }
        public int? ProfileId
        {
            get
            {
                var id = _httpContext?.Session.GetInt32("profileId");

                var user = _httpContext.User;
                if (!user.IsAuthenticated())
                    return null;
                if (id == null)
                {
                    if (user.IsInRole("reseller"))
                        id = GetProfiles(user.Identity.Name).FirstOrDefault()?.Id;
                    else
                        id = _context.Users.SingleOrDefault(p => p.Username == user.Identity.Name)?.ProfileId;
                }
                _httpContext?.Session.SetInt32("profileId", id ?? 0);
                return id;
            }
            set
            {
                var user = _httpContext.User;
                if (!user.IsAuthenticated())
                    return;
                if (value == null)
                    return;
                _httpContext?.Session.SetInt32("profileId", value.Value);
            }
        }


        public Profile? Profile
        {
            get
            {
                var profileId = ProfileId;
                if (profileId == null || profileId == 0)
                    return null;
                if (profile == null)
                {
                    profile = _context.Profiles.Find(profileId);
                }
                return profile;
            }
        }

        public List<Profile> GetProfiles(string username)
        {
            var user = _context.Users.Find(username);
            var list = _context.Profiles.Where(p => p.Creator == username && user.ProfileId != p.Id).OrderBy(p => p.Lastname).ToList();
            return list;
        }

        public void Initialize(string username,string role)
        {

            int? id = null;
            if (role.Contains("reseller"))
                id = GetProfiles(username).FirstOrDefault()?.Id;
            else
                id = _context.Users.SingleOrDefault(p => p.Username == username)?.ProfileId;
            _httpContext?.Session.SetInt32("profileId", id ?? 0);
        }
    }
}
