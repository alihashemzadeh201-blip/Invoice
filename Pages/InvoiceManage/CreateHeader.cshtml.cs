using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Invoices.Models;
using Microsoft.EntityFrameworkCore;
using TaxCollectData;
using Microsoft.AspNetCore.Authorization;
using System.Linq.Expressions;
using Nancy.ViewEngines.SuperSimpleViewEngine;
using Microsoft.AspNetCore.Http.Extensions;
using Invoice.Code;
using Microsoft.Extensions.Logging;

namespace Invoices.Pages.InvoiceManage
{
    [Authorize]
    public class CreateModel : PageModel
    {
        private readonly Invoices.Models.DataContext _context;
        private ISelectedProfile _profile;
        [BindProperty]
        public Invoices.Models.Invoice Invoice { get; set; }
        [BindProperty(SupportsGet = true)]
        public string? IrTaxId { get; set; }
        [BindProperty(SupportsGet = true)]
        public InvoiceTopic? Ins { get; set; }

        private readonly IProfileSecretProtector _secretProtector;
        private readonly ILogger<CreateModel> _logger;

        public CreateModel(Invoices.Models.DataContext context,ISelectedProfile profile, IProfileSecretProtector secretProtector, ILogger<CreateModel> logger)
        {
            _context = context;
            _profile = profile;
            _secretProtector = secretProtector;
            _logger = logger;
        }
        public async Task<IActionResult> OnGetAsync(int? id)
        {

            if (id != null)
            {
                var user = _context.Users.Include(p => p.Profile).SingleOrDefault(p => p.Username == User.Identity.Name);
                Invoice = _context.Invoices.SingleOrDefault(p => p.Id == id);
                //if (User.IsInRole("user"))
                //{
                    Invoice.Tins = _profile.Profile.TinCode;
                    Invoice.ProfileId = _profile.ProfileId;
                //}
                //else if (User.IsInRole("reseller"))
                //{
                //    var profile = await _context.Profiles.FindAsync(Invoice.ProfileId);
                //    Invoice.Tins = profile?.TinCode;
                //}
                if (Invoice.Sender != User.Identity.Name)
                    return Forbid();
                if (Invoice == null)
                    return NotFound();

            }
            else
            {

                var user = _context.Users.Include(p => p.Profile).SingleOrDefault(p => p.Username == User.Identity.Name);
                if (user.Profile == null)
                {
                    return RedirectToPage("/account/userprofile", new { returnurl = "/invoicemanage/createheader", message = "برای صدور فاکتور بایستی ابتدا پروفایل کاربری خود را تکمیل نمایید." });
                }
                var Invoice = new Models.Invoice
                {
                    Sender = User.Identity.Name,
                    Irtaxid = IrTaxId,
                    Ins = Ins ?? InvoiceTopic.اصلی,
                    Inp = user.Profile.InvoicePattern
                };
                if (IrTaxId != null && (Ins == InvoiceTopic.اصلاحی || Ins == InvoiceTopic.برگشت_از_فروش))
                {
                    var invoice = await _context.Invoices.SingleOrDefaultAsync(p => p.Taxid == IrTaxId);
                    //ProfileId = invoice.ProfileId;
                    Utils.CopyProperties<Invoices.Models.Invoice>(invoice, ref Invoice, "Id", "Sender", "Irtaxid", "Ins");
                }
                else
                {
                    if (User.IsInRole("user"))
                    {
                        Invoice.Tins = user.Profile.TinCode;
                        Invoice.ProfileId = user.ProfileId;
                    }
                    else if (User.IsInRole("reseller"))
                    {
                        var profile = await _context.Profiles.FindAsync(_profile.ProfileId);
                        Invoice.Tins = profile?.TinCode;
                        Invoice.ProfileId = profile?.Id;
                    }
                }
                this.Invoice = Invoice;


            }
            if (User.IsInRole("reseller"))
                ViewData["ProfileId"] = new SelectList(await _context.Profiles.Where(p => p.Creator == User.Identity.Name).ToListAsync(), "Id", "DisplayName", Invoice.ProfileId);
            return Page();
        }




        // To protect from overposting attacks, see https://aka.ms/RazorPagesCRUD

        public async Task<IActionResult> OnPostAsync()
        {
            if (!ModelState.IsValid || _context.Invoices == null || Invoice == null)
            {
                if (User.IsInRole("reseller"))
                    ViewData["ProfileId"] = new SelectList(await _context.Profiles.Where(p => p.Creator == User.Identity.Name).ToListAsync(), "Id", "DisplayName", Invoice.ProfileId);

                return Page();
            }
            if (Invoice.Id == 0)
            {
                _context.Invoices.Add(Invoice);
                if (Invoice.Ins == InvoiceTopic.اصلاحی)
                {
                    var invoice = await _context.Invoices.Include(p => p.InvoiceItems).Include(p => p.Payments).SingleOrDefaultAsync(p => p.Taxid == Invoice.Irtaxid);
                    foreach (var item in invoice.InvoiceItems)
                    {
                        var invoiceitem = new InvoiceItem();
                        Utils.CopyProperties<InvoiceItem>(item, ref invoiceitem, "Id");
                        Invoice.InvoiceItems.Add(invoiceitem);
                    }
                    foreach (var item in invoice.Payments)
                    {
                        var payment = new Payment();
                        Utils.CopyProperties<Payment>(item, ref payment, "Id");
                        Invoice.Payments.Add(payment);
                    }

                }
            }
            else
                _context.Attach(Invoice).State = Microsoft.EntityFrameworkCore.EntityState.Modified;


            Invoice.Status = InvoiceStatus.در_انتظار_ارسال;
            Invoice.Sender = User?.Identity?.Name ?? "";
            await _context.SaveChangesAsync();

            return RedirectToPage("./InvoiceItems", new { invoiceid = Invoice.Id });
        }
        public async Task<IActionResult> OnGetDuplicateAsync(int id)
        {
            var pinvoice = await _context.Invoices.Include(p => p.InvoiceItems).Include(p => p.Payments).SingleOrDefaultAsync(p => p.Id == id);

            Models.Invoice newInvoice = new Models.Invoice
            {
                Sender = User.Identity.Name,
                Status = InvoiceStatus.در_انتظار_ارسال,

            };
            Utils.CopyProperties<Invoices.Models.Invoice>(pinvoice, ref newInvoice, "Id", "Taxid", "Sender", "ReferenceCode", "Status", "ResponseMessage", "SendDate", "Indati2m", "InvoiceItems", "Payments");

            foreach (var item in pinvoice.InvoiceItems)
            {
                var invoiceitem = new InvoiceItem();
                Utils.CopyProperties<InvoiceItem>(item, ref invoiceitem, "Id");
                newInvoice.InvoiceItems.Add(invoiceitem);
            }
            foreach (var item in pinvoice.Payments)
            {
                var payment = new Payment();
                Utils.CopyProperties<Payment>(item, ref payment, "Id");
                newInvoice.Payments.Add(payment);
            }
            _context.Invoices.Add(newInvoice);
            await _context.SaveChangesAsync();
            return RedirectToPage("Index");
        }
        public IActionResult OnGetSendInvoice(int id, bool? cancel = false)
        {
            User user = null;
            try
            {
                user = _context.Users.SingleOrDefault(p => p.Username == User.Identity.Name);
            }
            catch (Exception ex)
            {

            }

            var invoice = _context.Invoices.Include(p => p.InvoiceItems).Include(p => p.Payments).Include(p => p.Profile).SingleOrDefault(p => p.Id == id);
            if (invoice.Sender != User.Identity.Name)
                return Forbid();
            if (user != null && invoice.Ins == InvoiceTopic.اصلی && cancel == false && (user.ExpireDate == null || user.ExpireDate < DateTime.Now) && user.Vallet < user.PerInvoiceCost)
            {

                return RedirectToPage("/Credit/Expired");

            }
            if (cancel == true)
            {
                var cancelInvoice = new Invoices.Models.Invoice
                {
                    Ins = InvoiceTopic.ابطالی,
                    Irtaxid = invoice.Taxid,
                    Tins = invoice.Tins,
                    Ft = invoice.Ft,
                    Scln = invoice.Scln,
                    Scc = invoice.Scc,
                    Sbc = invoice.Sbc,
                    Crn = invoice.Crn,
                    Sender = User.Identity.Name,
                    Indatim = DateTime.Now,
                    ProfileId = invoice.ProfileId,
                    Status = InvoiceStatus.در_انتظار_ارسال
                };
                _context.Invoices.Add(cancelInvoice);
                _context.SaveChanges();
                invoice = cancelInvoice;

            }
            if (invoice.Status != InvoiceStatus.در_حال_ارسال && invoice.Status != InvoiceStatus.موفق)
            {
                invoice.Status = InvoiceStatus.در_حال_ارسال;
                _context.SaveChanges();



                //invoice.Irtaxid = "A2F9NM04D03002F52EDB29";
                if (invoice == null) return NotFound();
                var apiHelper = new InvoiceApiHelper(_secretProtector, _logger);

                var response = apiHelper.SendInvoice(invoice);

                invoice.Status = response.Status;
                invoice.ReferenceCode = response.RefId;
                invoice.ResponseMessage = response.Response;
                invoice.UID = response.UID;
                invoice.Taxid = response.TaxId;
                invoice.SendDate = DateTime.Now;

                if (invoice.Status == InvoiceStatus.موفق)
                {
                    if (invoice.Ins == InvoiceTopic.اصلی)
                    {
                        if (user != null && (user.ExpireDate == null || user.ExpireDate < DateTime.Now))
                        {
                            user.Vallet -= user.PerInvoiceCost;
                            user.InvoiceSent = true;
                        }
                      
                    }
                    else if (invoice.Ins == InvoiceTopic.ابطالی || invoice.Ins== InvoiceTopic.اصلاحی)
                    {
                        var refInvoice = _context.Invoices.SingleOrDefault(p => p.Taxid == invoice.Irtaxid);
                        if (refInvoice != null)
                        {
                            refInvoice.Status =invoice.Ins== InvoiceTopic.ابطالی? InvoiceStatus.ابطال_شده: InvoiceStatus.اصلاح_شده;
                            refInvoice.ReferalId = invoice.Id;
                        }
                    }
                   
                }
                else if (invoice.Status == InvoiceStatus.خطا_در_ارسال || invoice.Status == InvoiceStatus.اشکال_در_صورتحساب)
                {
                    _context.SaveChanges();
                    return RedirectToPage("InvoiceConfirm", new { id = invoice.Id });
                }
                _context.SaveChanges();
            }

            return RedirectToPage("Index");
        }

        public async Task<IActionResult> OnGetDeleteAsync(int id)
        {
            var invoice = await _context.Invoices.Include(p => p.InvoiceItems).SingleOrDefaultAsync(p => p.Id == id);
            if (invoice == null) return NotFound();
            _context.Remove(invoice);
            await _context.SaveChangesAsync();
            return RedirectToPage("Index");

        }

        public async Task<IActionResult> OnGetLastStatusAsync(int id)
        {
            var user = await _context.Users.Include(p => p.Profile).SingleOrDefaultAsync(p => p.Username == User.Identity.Name);
            var apiHelper = new InvoiceApiHelper(_secretProtector, _logger);
            var invoice = await _context.Invoices.Include(p => p.InvoiceItems).Include(p => p.Payments).Include(p => p.Profile).SingleOrDefaultAsync(p => p.Id == id);

            var response = apiHelper.GetLastStatus(invoice);
            
            bool isSuccessNow = response.Status == InvoiceStatus.موفق && invoice.Status != InvoiceStatus.موفق;
            
            if (isSuccessNow)
            {
                if (user.ExpireDate == null || user.ExpireDate < DateTime.Now)
                {
                    user.Vallet -= user.PerInvoiceCost;
                    user.InvoiceSent = true;
                }
            }
            invoice.Status = response.Status;
            invoice.ResponseMessage = response.Response;
            await _context.SaveChangesAsync();

            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest" || Request.Headers["Accept"].ToString().Contains("application/json"))
            {
                return new JsonResult(new { success = true, isSuccessNow = isSuccessNow, newStatus = response.Status.ToString() });
            }

            return RedirectToPage("Index");
        }

    }
}
