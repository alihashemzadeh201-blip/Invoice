using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using System.Text.Json.Nodes;
using Invoice.Code;

namespace Invoices.Pages.InvoiceManage
{
    [Authorize]
    public class InvoiceItemsModel : PageModel
    {
        private readonly Invoices.Models.DataContext _context;
        private readonly IUserActivityLogger _activityLogger;

        [BindProperty(SupportsGet = true)]
        public int InvoiceId { get; set; }

        [BindProperty]
        public Invoices.Models.InvoiceItem InvoiceItem { get; set; } = default!;

        public List<Models.Product> Products { get; set; }
        public Invoices.Models.Invoice Invoice { get; set; } = default!;

        public InvoiceItemsModel(Invoices.Models.DataContext context, IUserActivityLogger activityLogger)
        {
            _context = context;
            _activityLogger = activityLogger;
        }

        public async Task<IActionResult> OnGetAsync()
        {
            Invoice = await _context.Invoices.Include(p => p.InvoiceItems).SingleOrDefaultAsync(p => p.Id == InvoiceId);
            if (Invoice == null)
                return NotFound();
            if (Invoice.Sender != User.Identity.Name)
                return Forbid();
            //if (id != null)
            //{
            //    InvoiceItem = await _context.InvoiceItems.Include(p => p.Invoice).SingleOrDefaultAsync(prop => prop.Id == id);
            //    if (InvoiceItem == null)
            //        return NotFound();
            //    if (InvoiceItem.Invoice.Sender != User.Identity.Name)
            //        return Forbid();
            //    Invoice = InvoiceItem.Invoice;
            //}
            //else
            {
                InvoiceItem = new Models.InvoiceItem();
                InvoiceItem.Dis = 0;
                Invoice = await _context.Invoices.FindAsync(InvoiceId);
                //if (Invoice.Inp == InvoicePattern.طلا_و_جواهر)
                //    InvoiceItem.Vra = VatRate.درصد_10;
            }
            Products = await _context.Products.Where(p => p.Creator == User.Identity.Name || p.Creator == "admin").ToListAsync();
            ViewData["CurrencyCodes"] = new SelectList(await _context.CurrencyCodes.ToListAsync(), "AlphabetCode", "AlphabetCode");
            ViewData["Measur"] = new SelectList(await _context.MeasureUnits.OrderBy(p=>p.Order).ThenBy(p=>p.Title).ToListAsync(), "Code", "Title");

            return Page();

        }
        public async Task<IActionResult> OnPostAsync()
        {
            InvoiceItem.InvoiceId = InvoiceId;

            // بررسی مالکیت: فقط ایجادکننده فاکتور می‌تواند آیتم‌های آن را اضافه یا ویرایش کند
            var ownerInvoice = await _context.Invoices.AsNoTracking().SingleOrDefaultAsync(p => p.Id == InvoiceId);
            if (ownerInvoice == null)
                return NotFound();
            if (ownerInvoice.Sender != User.Identity?.Name)
                return Forbid();

            if (!ModelState.IsValid)
            {
                Products = await _context.Products.Where(p => p.Creator == User.Identity.Name || p.Creator == "admin").ToListAsync();
                ViewData["CurrencyCodes"] = new SelectList(await _context.CurrencyCodes.ToListAsync(), "AlphabetCode", "AlphabetCode", InvoiceItem.Cut);
                ViewData["Measur"] = new SelectList(await _context.MeasureUnits.OrderBy(p => p.Order).ThenBy(p => p.Title).ToListAsync(), "Code", "Title", InvoiceItem.Mu);
                Invoice = await _context.Invoices.Include(p => p.InvoiceItems).SingleOrDefaultAsync(p => p.Id == InvoiceId);
                return Page();
            }

            bool isNewItem = (InvoiceItem.Id == 0);
            if (isNewItem)
            {
                _context.Add(InvoiceItem);
            }
            else
            {
                var existingItem = await _context.InvoiceItems.FindAsync(InvoiceItem.Id);
                if (existingItem == null)
                    return NotFound();

                // آیتم باید واقعا متعلق به همین فاکتور باشد (جلوگیری از دستکاری آیتم‌های دیگران)
                if (existingItem.InvoiceId != InvoiceId)
                    return Forbid();

                existingItem.InvoiceId = InvoiceItem.InvoiceId;
                existingItem.Sstid = InvoiceItem.Sstid;
                existingItem.Sstt = InvoiceItem.Sstt;
                existingItem.Mu = InvoiceItem.Mu;
                existingItem.Am = InvoiceItem.Am;
                existingItem.Fee = InvoiceItem.Fee;
                existingItem.Dis = InvoiceItem.Dis;
                existingItem.Vra = InvoiceItem.Vra;
                existingItem.Consfee = InvoiceItem.Consfee;
                existingItem.Spro = InvoiceItem.Spro;
                existingItem.Bros = InvoiceItem.Bros;
                existingItem.Odt = InvoiceItem.Odt;
                existingItem.Odr = InvoiceItem.Odr;
                existingItem.Odam = InvoiceItem.Odam;
                existingItem.Olt = InvoiceItem.Olt;
                existingItem.Olr = InvoiceItem.Olr;
                existingItem.Olam = InvoiceItem.Olam;
                existingItem.Bsrn = InvoiceItem.Bsrn;
                existingItem.Cut = InvoiceItem.Cut;
                existingItem.Exr = InvoiceItem.Exr;
                existingItem.Nw = InvoiceItem.Nw;
                existingItem.Ssrv = InvoiceItem.Ssrv;
                existingItem.Sscv = InvoiceItem.Sscv;
                existingItem.pspd = InvoiceItem.pspd;
                existingItem.Cpr = InvoiceItem.Cpr;
                existingItem.Sovat = InvoiceItem.Sovat;
            }

            await _context.SaveChangesAsync();

            if (isNewItem)
            {
                await _activityLogger.LogInvoiceActionAsync("افزودن قلم کالا", InvoiceId, ownerInvoice.Taxid, $"افزودن قلم کالا: '{InvoiceItem.Sstt}' (تعداد: {InvoiceItem.Am}، فی: {InvoiceItem.Fee})");
            }
            else
            {
                await _activityLogger.LogInvoiceActionAsync("ویرایش قلم کالا", InvoiceId, ownerInvoice.Taxid, $"ویرایش قلم کالا: '{InvoiceItem.Sstt}' (تعداد: {InvoiceItem.Am}، فی: {InvoiceItem.Fee})");
            }

            return RedirectToPage("InvoiceItems", new { InvoiceId = InvoiceItem.InvoiceId });
        }

        public async Task<IActionResult> OnGetDeleteAsync(int id)
        {
            var invoiceitem = await _context.InvoiceItems.Include(p => p.Invoice).SingleOrDefaultAsync(p => p.Id == id);

            if (invoiceitem == null)
                return NotFound();

            // فقط ایجادکننده فاکتور مجاز به حذف آیتم‌های آن است
            if (invoiceitem.Invoice == null || invoiceitem.Invoice.Sender != User.Identity?.Name)
                return Forbid();

            var invoiceid = invoiceitem.InvoiceId;
            var taxId = invoiceitem.Invoice?.Taxid;
            var itemName = invoiceitem.Sstt;
            _context.InvoiceItems.Remove(invoiceitem);
            await _context.SaveChangesAsync();
            await _activityLogger.LogInvoiceActionAsync("حذف قلم کالا", invoiceid, taxId, $"حذف قلم کالا: '{itemName}'");
            return RedirectToPage("InvoiceItems", new { invoiceid });

        }

        public async Task<IActionResult> OnGetGetProductsAsync(string search)
        {
            search = search ?? "";
            var products = await _context.Products.Where(p =>(p.Creator==User.Identity.Name || p.Creator==null) && ( p.Sstt.Contains(search) || p.Sstid.Contains(search))).Take(20).ToListAsync();
            return new JsonResult(products);
        }


    }
}
