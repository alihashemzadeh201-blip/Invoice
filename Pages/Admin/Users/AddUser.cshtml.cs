using Invoices.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace Invoice.Pages.Admin.Users
{
    [Authorize(Roles ="admin")]

    public class AddUserModel : PageModel
    {
    DataContext _context;
        [BindProperty]
        public User User { get; set; }
        [BindProperty]
        public bool Edit { get; set; }

        private readonly ILogger<AddUserModel> _logger;

        public AddUserModel(DataContext context, ILogger<AddUserModel> logger)
        {
            _context = context;
            _logger = logger;
        }
    
        public void OnGet(string? username)
        {
            if(username!=null)
            {
                User = _context.Users.SingleOrDefault(p=>p.Username==username);
                Edit = true;
            }
        }
        public async Task<ActionResult> OnPost()
        {
           
           
            //if (!ModelState.IsValid)
            //    return Page();
            
                var user=await _context.Users.SingleOrDefaultAsync(p=>p.Username==User.Username);
            if (Edit)
            {
                //_context.Attach(User).State = Microsoft.EntityFrameworkCore.EntityState.Modified;
                //    _context.Entry(User).Property("Vallet").IsModified = false;
                //    _context.Entry(User).Property("RegisterDate").IsModified = false;
                //    _context.Entry(User).Property("LastChargeDate").IsModified = false;
                //    _context.Entry(User).Property("InvoiceSent").IsModified = false;
                //    _context.Entry(User).Property("PerInvoiceCost").IsModified = false;
                if (user != null)
                {
                    user.ExpireDate = User.ExpireDate;
                    user.Role = User.Role;
                    user.PerInvoiceCost = User.PerInvoiceCost;
                }


                //if (User.Password == null)                    
                    //_context.Entry(User).Property("Password").IsModified = false;
                //else
                if(User.Password!=null)
                    user.Password = PasswordHashManager.HashPassword(User.Password);
            }
            else
            {
                if (user != null)
                {
                    ModelState.AddModelError(string.Empty, "این نام کاربری قبلا ثبت شده است.");
                    return Page();
                }
                if (string.IsNullOrEmpty(User.Password))
                {
                    ModelState.AddModelError("User.Password", "بایستی رمز عبور وارد شود.");
                    return Page();
                }
                User.Password = PasswordHashManager.HashPassword(User.Password);
                _context.Add(User);
            }
           
                


            _context.SaveChanges();
            _logger.LogInformation("کاربر {Username} توسط مدیر {Admin} {Action} شد.",
                User?.Username, HttpContext.User.Identity?.Name, Edit ? "ویرایش" : "ایجاد");
            return RedirectToPage("./Index");
        }
    }
}
