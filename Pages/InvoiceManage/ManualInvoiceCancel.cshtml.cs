using Invoice.Code;
using Invoice.Models;
using Invoices.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Nancy.ViewEngines.SuperSimpleViewEngine;
using SQLitePCL;
using System.ComponentModel.DataAnnotations;
using TaxCollectData;
using Microsoft.Extensions.Logging;

namespace Invoice.Pages.InvoiceManage
{
    [Authorize]
    public class ManualInvoiceCancelModel : PageModel
    {
        //public Profile Profile { get; set; }
        [BindProperty]
        [Required(ErrorMessage=Messages.Required)]
        public string TaxId { get; set; }
        DataContext _context;
        ISelectedProfile _profile;
        private readonly IProfileSecretProtector _secretProtector;
        private readonly ILogger<ManualInvoiceCancelModel> _logger;

        public ManualInvoiceCancelModel(DataContext context,ISelectedProfile profile, IProfileSecretProtector secretProtector, ILogger<ManualInvoiceCancelModel> logger)
        {
            _context= context;  
            _profile= profile;
            _secretProtector = secretProtector;
            _logger = logger;
        }
        public void OnGet()
        {
            //if (ProfileId == null)
            //{
            //    var user = _context.Users.Include(p => p.Profile).SingleOrDefault(p => p.Username == User.Identity.Name);

            //    ProfileId = user.ProfileId;
            //    Profile = user.Profile;
            //}
            //else
            //{
            //    Profile = _context.Profiles.Find(ProfileId);
            //}


        }
        public IActionResult OnPost()
        {
            //var profile = _context.Profiles.Find(_profile.ProfileId);

            var invoice = new Invoices.Models.Invoice
            {
                Ins = InvoiceTopic.ابطالی,
                Irtaxid = TaxId,
                Tins =_profile.Profile.TinCode,
                Sender = User.Identity.Name,
                Indatim = DateTime.Now,
                ProfileId = _profile.ProfileId,
                Profile = _profile.Profile,
                Status = InvoiceStatus.در_حال_ارسال
            };
            _context.Invoices.Add(invoice);
            //invoice.Irtaxid = "A2F9NM04D03002F52EDB29";
            try
            {
                var apiHelper = new InvoiceApiHelper(_secretProtector, _logger);
                var response = apiHelper.SendInvoice(invoice);
                invoice.Status = response.Status;
                invoice.ReferenceCode = response.RefId;
                invoice.UID = response.UID;
                invoice.ResponseMessage = response.Response;
                invoice.Taxid = response.TaxId;
            }
            catch (Exception ex)
            {
                invoice.Status = InvoiceStatus.خطا_در_ارسال;
            }
                invoice.SendDate = DateTime.Now;
                if (invoice.Status == InvoiceStatus.خطا_در_ارسال || invoice.Status == InvoiceStatus.اشکال_در_صورتحساب)
                {
                    _context.SaveChanges();
                    return RedirectToPage("InvoiceConfirm", new { id = invoice.Id, cancelinvoice = (invoice.Ins == InvoiceTopic.ابطالی ? true : false) });
                }
                _context.SaveChanges();

            return RedirectToPage("Index");
        }
    }
}
