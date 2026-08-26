using Invoices.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Invoices;
using Microsoft.AspNetCore.Authorization;
using TaxCollectData;
using Microsoft.Build.Framework;
using Invoice.Code;
using Invoice.Models;
namespace Invoice.Pages.InvoiceManage
{
    [Authorize]
    public class ImportInvoicesModel : PageModel
    {
        DataContext _context;
        ISelectedProfile _profile;
        public IFormFile File { get; set; }
        public List<Invoices.Models.Invoice> Invoices { get; set; }
        public ImportInvoicesModel(DataContext context,ISelectedProfile profile)
        {
            _context = context;
            _profile = profile;
        }

        public void OnGet()
        {
            Invoices =  _context.Invoices.Include(p=>p.InvoiceItems).Where(p => p.Sender == User.Identity.Name && p.Draft == true&& p.ProfileId==_profile.ProfileId).ToList();
            ModelState.Clear();
        }
        public async Task<IActionResult> OnPostAsync()
        {
            var path = $"Uploads/{Path.GetRandomFileName()}.xlsx";
            using (var fs = new FileStream(path, FileMode.Create))
            {
                await File.CopyToAsync(fs);
            }
            var doc = ExcelHelp.Open(path);
            int index = 0;
            var groups = doc.Rows.Skip(1).GroupBy(p => new { Code = p.Cells[0], Date = p.Cells[1], Type = p.Cells[2], Pattern = p.Cells[3], CheckoutType = p.Cells[4], BuyerType = p.Cells[5], BuyerTin = p.Cells[6] });
            //var user = _context.Users.Include(p => p.Profile).SingleOrDefault(p => p.Username == User.Identity.Name);
            foreach (var group in groups)
            {
                try
                {
                    var Invoice = new Invoices.Models.Invoice
                    {
                        Sender = User.Identity.Name,
                        Ins= InvoiceTopic.اصلی,
                        Inty = (InvoiceType)int.Parse(group.Key.Type),
                        Inp = (InvoicePattern)int.Parse(group.Key.Pattern),
                        InvoiceDate = group.Key.Date,
                        Tob = (PersonType)int.Parse(group.Key.BuyerType),
                        Tinb = group.Key.BuyerTin,
                        Setm = (CheckoutType)int.Parse(group.Key.CheckoutType),
                        Draft = true,
                    };
                    //if (User.IsInRole("user"))
                    //{
                        Invoice.Tins = _profile.Profile.TinCode;
                        Invoice.ProfileId = _profile.ProfileId;
                    //}
                    //else if (User.IsInRole("reseller"))
                    //{
                    //    //var profile = await _context.Profiles.FindAsync(_profile.ProfileId);
                    //    Invoice.Tins = _profile.Profile.TinCode;
                    //    Invoice.ProfileId = _profile.ProfileId;
                    //}

                    foreach (var item in group)
                    {

                        index++;
                        if (item.Cells.Count == 0 || item.Cells[0] == "")
                            break;

                        var mustr = item.Cells[13].Replace(" ", "");

                        var mu = await _context.MeasureUnits.SingleOrDefaultAsync(p => p.Title.Contains(mustr));
                        if (mu == null)
                        {
                            ModelState.AddModelError("", $"واحد سنجش در سطر {index} نامعتبر است");
                            continue;
                        }
                        var invoiceItem = new InvoiceItem
                        {
                            Sstid = item.Cells[7],
                            Sstt = item.Cells[8],
                            Am = decimal.Parse(item.Cells[9]),
                            Fee = decimal.Parse(item.Cells[10]),
                            Dis = decimal.Parse(item.Cells[11]),
                            Vra = (VatRate)int.Parse(item.Cells[12]),
                            Mu = mu.Code,

                        };
                        Invoice.InvoiceItems.Add(invoiceItem);

                    }
                    _context.Invoices.Add(Invoice);
                }
                catch (Exception ex)
                {
                    ModelState.AddModelError("", $"اطلاعات سطر {index} فایل اکسل اشکال دارد لطفا بررسی نمایید.");
                }
            }
            if (ModelState.ErrorCount > 0)
            {
                Invoices = await _context.Invoices.Include(p=>p.InvoiceItems).Where(p => p.Sender == User.Identity.Name && p.Draft == true &&  p.ProfileId == _profile.ProfileId).ToListAsync();

                return Page();
            }
            else
            {
                await _context.SaveChangesAsync();
                return RedirectToPage("ImportInvoices");
            }

        }
        public async Task<IActionResult> OnGetDeleteAsync(int id)
        {
            var invoice = await _context.Invoices.FindAsync(id);
            if (invoice == null)
                return NotFound();

            if (invoice.Sender != User.Identity.Name)
                return Forbid(); 

            _context.Invoices.Remove(invoice);  
            _context.SaveChanges();
            return RedirectToPage("ImportInvoices");
        }

        public async Task<IActionResult> OnGetCancelAsync()
        {
            var drafts = await _context.Invoices.Where(p => p.Draft == true && p.Sender==User.Identity.Name && p.ProfileId==_profile.ProfileId).ToListAsync();
            foreach (var draft in drafts)
            {
                _context.Invoices.Remove(draft);

            }
            _context.SaveChanges();
            return RedirectToPage("Index");
        }

        public async Task<IActionResult> OnGetSendAsync()
        {
            var user = await _context.Users.FindAsync(User.Identity.Name);
            var invoices = await _context.Invoices.Where(p => p.Draft == true && p.Sender == User.Identity.Name &&  p.ProfileId == _profile.ProfileId).ToListAsync();
            var profile = await _context.Profiles.FindAsync(_profile.ProfileId);
            var responses = new InvoiceApiHelper().SendInvoices(invoices, profile);
            for (int i = 0; i < responses.Count; i++)
            {
                var invoice = invoices[i];
                var response= responses[i];
                invoice.Status = response.Status;
                invoice.ReferenceCode = response.RefId;
                invoice.ResponseMessage = response.Response;
                invoice.UID = response.UID;
                invoice.Taxid = response.TaxId;
                invoice.SendDate = DateTime.Now;
                invoice.Draft = false;
                if (invoice.Status == InvoiceStatus.موفق)
                {
                    if (invoice.Ins == InvoiceTopic.اصلی)
                    {
                        if (user.ExpireDate == null || user.ExpireDate < DateTime.Now)
                        {
                            user.Vallet -= user.PerInvoiceCost;
                            user.InvoiceSent = true;
                        }

                    }
                    else if (invoice.Ins == InvoiceTopic.ابطالی || invoice.Ins == InvoiceTopic.اصلاحی)
                    {
                        var refInvoice = _context.Invoices.SingleOrDefault(p => p.Taxid == invoice.Irtaxid);
                        if (refInvoice != null)
                        {
                            refInvoice.Status = invoice.Ins == InvoiceTopic.ابطالی ? InvoiceStatus.ابطال_شده : InvoiceStatus.اصلاح_شده;
                            refInvoice.ReferalId = invoice.Id;
                        }
                    }

                }
            }
            await _context.SaveChangesAsync();
            return RedirectToPage("Index");
        }
    }


}
