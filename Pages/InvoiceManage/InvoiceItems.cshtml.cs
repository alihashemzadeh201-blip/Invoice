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
            var username = User.Identity?.Name;
            if (string.IsNullOrWhiteSpace(username))
                return new JsonResult(new List<object>());

            search = (search ?? "").Trim();

            // تشخیص تمامی نام‌های کاربری مرتبط با پروفایل کاربر/نماینده
            var effectiveUsernames = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { username };
            var currentUser = await _context.Users.FirstOrDefaultAsync(u => u.Username == username);
            if (currentUser?.ProfileId != null)
            {
                var profile = await _context.Profiles.FindAsync(currentUser.ProfileId.Value);
                if (profile != null && !string.IsNullOrWhiteSpace(profile.Creator))
                {
                    effectiveUsernames.Add(profile.Creator);
                }

                var siblingUsers = await _context.Users
                    .Where(u => u.ProfileId == currentUser.ProfileId.Value)
                    .Select(u => u.Username)
                    .ToListAsync();
                foreach (var su in siblingUsers)
                {
                    if (!string.IsNullOrWhiteSpace(su)) effectiveUsernames.Add(su);
                }
            }

            // ۱. دریافت کالاهای ذخیره‌شده در Products مربوط به کاربر
            var userProductsList = await _context.Products
                .Include(p => p.MeasureUnit)
                .Where(p => effectiveUsernames.Contains(p.Creator))
                .AsNoTracking()
                .ToListAsync();

            // ۲. دریافت کالاهای منتخب کاربر از UserFavoriteStuffs
            var favQuery = _context.UserFavoriteStuffs
                .Include(f => f.Stuff)
                .Where(f => effectiveUsernames.Contains(f.Username))
                .AsNoTracking();

            if (!string.IsNullOrWhiteSpace(search))
            {
                favQuery = favQuery.Where(f =>
                    f.StuffCode.Contains(search) ||
                    (f.Stuff != null && f.Stuff.Title.Contains(search)));
            }

            var favorites = await favQuery
                .OrderByDescending(f => f.AddedDate)
                .ToListAsync();

            var combined = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

            // اضافه کردن کالاهای منتخب
            foreach (var f in favorites)
            {
                var code = f.StuffCode;
                var stuff = f.Stuff;
                var prod = userProductsList.FirstOrDefault(p => string.Equals(p.Sstid, code, StringComparison.OrdinalIgnoreCase));

                var title = prod?.Sstt ?? stuff?.Title ?? code;
                var stuffType = stuff != null
                    ? stuff.Type.ToString().Replace("_", " ")
                    : (prod?.StuffType.ToString().Replace("_", " ") ?? "");

                VatRate vra = VatRate.درصد_10;
                int vatPercent = 10;
                if (stuff != null)
                {
                    vatPercent = stuff.VatRate;
                    if (stuff.VatRate == 0 || !stuff.VatTax)
                        vra = VatRate.معاف;
                    else if (stuff.VatRate == 9)
                        vra = VatRate.درصد_9;
                    else if (stuff.VatRate == 10)
                        vra = VatRate.درصد_10;
                    else if (stuff.VatRate == 16)
                        vra = VatRate.درصد_16;
                    else if (Enum.IsDefined(typeof(VatRate), stuff.VatRate))
                        vra = (VatRate)stuff.VatRate;
                }
                else if (prod != null)
                {
                    vra = prod.Vra;
                    vatPercent = (int)prod.Vra;
                }

                combined[code] = new
                {
                    id = prod?.Id ?? 0,
                    sstid = code,
                    sstt = title,
                    title = $"{title} -- {stuffType} -- {code}",
                    vra = (int)vra,
                    vatRatePercent = vatPercent,
                    mu = prod?.Mu ?? "",
                    fee = prod?.Fee ?? 0
                };
            }

            // اضافه کردن کالاهایی که از قبل در Products ثبت شده بودند اما در UserFavoriteStuffs نیستند
            foreach (var prod in userProductsList)
            {
                if (string.IsNullOrWhiteSpace(prod.Sstid) || combined.ContainsKey(prod.Sstid))
                    continue;

                if (!string.IsNullOrWhiteSpace(search) &&
                    !prod.Sstid.Contains(search, StringComparison.OrdinalIgnoreCase) &&
                    (prod.Sstt == null || !prod.Sstt.Contains(search, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                var stuffType = prod.StuffType.ToString().Replace("_", " ");
                int vatPercent = (int)prod.Vra;
                if (prod.Vra == VatRate.معاف) vatPercent = 0;
                else if (prod.Vra == VatRate.درصد_9) vatPercent = 9;
                else if (prod.Vra == VatRate.درصد_10) vatPercent = 10;
                else if (prod.Vra == VatRate.درصد_16) vatPercent = 16;

                combined[prod.Sstid] = new
                {
                    id = prod.Id,
                    sstid = prod.Sstid,
                    sstt = prod.Sstt ?? prod.Sstid,
                    title = $"{prod.Sstt ?? prod.Sstid} -- {stuffType} -- {prod.Sstid}",
                    vra = (int)prod.Vra,
                    vatRatePercent = vatPercent,
                    mu = prod.Mu ?? "",
                    fee = prod.Fee ?? 0
                };
            }

            return new JsonResult(combined.Values.Take(50).ToList());
        }


    }
}
