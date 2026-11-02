using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Invoice.Code;
using Invoice.Models;
using Invoices.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Invoice.Pages.StuffPages
{
    [Authorize]
    public class IndexModel : PageModel
    {
        private readonly DataContext _context;
        private readonly IUserActivityLogger _activityLogger;
        private readonly ILogger<IndexModel> _logger;
        private readonly ISelectedProfile? _selectedProfile;

        public IndexModel(
            DataContext context,
            IUserActivityLogger activityLogger,
            ILogger<IndexModel> logger,
            ISelectedProfile? selectedProfile = null)
        {
            _context = context;
            _activityLogger = activityLogger;
            _logger = logger;
            _selectedProfile = selectedProfile;
        }

        private async Task<List<string>> GetEffectiveUsernamesAsync()
        {
            var usernames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var currentUsername = User.Identity?.Name;
            if (!string.IsNullOrWhiteSpace(currentUsername))
            {
                usernames.Add(currentUsername);
            }

            if (User.IsInRole("reseller") && _selectedProfile?.ProfileId != null && _selectedProfile.ProfileId > 0)
            {
                var targetProfileId = _selectedProfile.ProfileId.Value;
                var profileUsers = await _context.Users
                    .Where(u => u.ProfileId == targetProfileId)
                    .Select(u => u.Username)
                    .ToListAsync();
                foreach (var u in profileUsers)
                {
                    if (!string.IsNullOrWhiteSpace(u)) usernames.Add(u);
                }

                var profile = _selectedProfile.Profile ?? await _context.Profiles.FindAsync(targetProfileId);
                if (profile != null && !string.IsNullOrWhiteSpace(profile.Creator))
                {
                    usernames.Add(profile.Creator);
                }
            }
            else if (!string.IsNullOrWhiteSpace(currentUsername))
            {
                var user = await _context.Users.FirstOrDefaultAsync(u => u.Username == currentUsername);
                if (user?.ProfileId != null)
                {
                    var profile = await _context.Profiles.FindAsync(user.ProfileId.Value);
                    if (profile != null && !string.IsNullOrWhiteSpace(profile.Creator))
                    {
                        usernames.Add(profile.Creator);
                    }

                    var siblingUsers = await _context.Users
                        .Where(u => u.ProfileId == user.ProfileId.Value)
                        .Select(u => u.Username)
                        .ToListAsync();
                    foreach (var su in siblingUsers)
                    {
                        if (!string.IsNullOrWhiteSpace(su)) usernames.Add(su);
                    }
                }
            }

            return usernames.ToList();
        }

        public class StuffItemViewModel
        {
            public string Code { get; set; } = string.Empty;
            public string Title { get; set; } = string.Empty;
            public int VatRate { get; set; }
            public StuffType Type { get; set; }
            public bool VatTax { get; set; }
            public bool Special { get; set; }
            public bool IsFavorite { get; set; }
            public bool IsInProducts { get; set; }
            public int? ProductId { get; set; }
            public decimal? Fee { get; set; }
            public string? Mu { get; set; }
            public string? MeasureUnitTitle { get; set; }
            public string? CustomTitle { get; set; }
            public DateTime? AddedDate { get; set; }
        }

        public List<StuffItemViewModel> Items { get; set; } = new();
        public List<MeasureUnit> MeasureUnits { get; set; } = new();

        [BindProperty(SupportsGet = true)]
        public string? Search { get; set; }

        [BindProperty(SupportsGet = true)]
        public string Tab { get; set; } = "fav"; // "fav" or "all"

        [BindProperty(SupportsGet = true)]
        public int PageNumber { get; set; } = 1;

        public int PageSize { get; set; } = 30;
        public int TotalCount { get; set; }
        public int FavoritesCount { get; set; }
        public int TotalPages => (int)Math.Ceiling((double)TotalCount / Math.Max(1, PageSize));

        [TempData]
        public string? SuccessMessage { get; set; }

        [TempData]
        public string? ErrorMessage { get; set; }

        public async Task<IActionResult> OnGetAsync()
        {
            var username = User.Identity?.Name ?? string.Empty;
            var effectiveUsernames = await GetEffectiveUsernamesAsync();
            if (PageNumber < 1) PageNumber = 1;

            MeasureUnits = await _context.MeasureUnits
                .AsNoTracking()
                .OrderBy(m => m.Order ?? 999)
                .ThenBy(m => m.Title)
                .ToListAsync();

            // ۱. دریافت تمامی کالاهای موجود در Products مربوط به کاربر یا پروفایل مشتری
            var userProductsList = await _context.Products
                .Include(p => p.MeasureUnit)
                .Where(p => effectiveUsernames.Contains(p.Creator))
                .AsNoTracking()
                .ToListAsync();

            var userProducts = new Dictionary<string, Product>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in userProductsList)
            {
                if (!string.IsNullOrWhiteSpace(p.Sstid) && !userProducts.ContainsKey(p.Sstid))
                {
                    userProducts[p.Sstid] = p;
                }
            }

            // ۲. بررسی و همگام‌سازی خودکار کالاهای قبلی موجود در Products با علاقه‌مندی‌ها
            var existingFavs = await _context.UserFavoriteStuffs
                .Where(f => effectiveUsernames.Contains(f.Username))
                .ToListAsync();

            var existingFavCodes = existingFavs
                .Select(f => f.StuffCode)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            bool dbNeedsSave = false;

            if (!string.IsNullOrWhiteSpace(username))
            {
                foreach (var prod in userProductsList)
                {
                    if (!string.IsNullOrWhiteSpace(prod.Sstid))
                    {
                        if (!existingFavCodes.Contains(prod.Sstid))
                        {
                            var targetFavUser = !string.IsNullOrWhiteSpace(prod.Creator) ? prod.Creator : username;
                            var newFav = new UserFavoriteStuff
                            {
                                Username = targetFavUser,
                                StuffCode = prod.Sstid,
                                AddedDate = DateTime.Now
                            };
                            _context.UserFavoriteStuffs.Add(newFav);
                            existingFavs.Add(newFav);
                            existingFavCodes.Add(prod.Sstid);
                            dbNeedsSave = true;
                        }
                    }
                }
            }

            if (dbNeedsSave)
            {
                await _context.SaveChangesAsync();
            }

            FavoritesCount = existingFavCodes.Count;

            // تب پیش‌فرض همواره کالاهای منتخب (fav) است و در صورت خالی بودن، وضعیت خالی نمایش داده می‌شود نه کل کالاها
            if (string.IsNullOrWhiteSpace(Tab))
            {
                Tab = "fav";
            }

            var favoriteCodes = existingFavs
                .GroupBy(f => f.StuffCode, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Max(x => x.AddedDate), StringComparer.OrdinalIgnoreCase);

            if (Tab == "fav")
            {
                var favQuery = _context.UserFavoriteStuffs
                    .Include(f => f.Stuff)
                    .Where(f => effectiveUsernames.Contains(f.Username))
                    .AsNoTracking()
                    .AsQueryable();

                if (!string.IsNullOrWhiteSpace(Search))
                {
                    var term = Search.Trim();
                    var matchingProdCodes = userProductsList
                        .Where(p => (!string.IsNullOrWhiteSpace(p.Sstid) && p.Sstid.Contains(term, StringComparison.OrdinalIgnoreCase)) ||
                                    (!string.IsNullOrWhiteSpace(p.Sstt) && p.Sstt.Contains(term, StringComparison.OrdinalIgnoreCase)))
                        .Select(p => p.Sstid)
                        .ToList();

                    favQuery = favQuery.Where(f => f.StuffCode.Contains(term) ||
                                                   (f.Stuff != null && f.Stuff.Title.Contains(term)) ||
                                                   matchingProdCodes.Contains(f.StuffCode));
                }

                TotalCount = await favQuery.CountAsync();

                var favItems = await favQuery
                    .OrderByDescending(f => f.AddedDate)
                    .Skip((PageNumber - 1) * PageSize)
                    .Take(PageSize)
                    .ToListAsync();

                Items = favItems.Select(f =>
                {
                    userProducts.TryGetValue(f.StuffCode, out var prod);
                    var title = prod?.Sstt ?? f.Stuff?.Title ?? "بدون شرح";
                    var vatRate = f.Stuff?.VatRate ?? (prod != null ? (int)prod.Vra : 10);
                    var type = f.Stuff?.Type ?? prod?.StuffType ?? StuffType.شناسه_عمومی_تولید_داخل_کالا;
                    var vatTax = f.Stuff?.VatTax ?? (prod != null ? prod.Vra != VatRate.معاف : true);

                    return new StuffItemViewModel
                    {
                        Code = f.StuffCode,
                        Title = title,
                        VatRate = vatRate,
                        Type = type,
                        VatTax = vatTax,
                        Special = f.Stuff?.Special ?? false,
                        IsFavorite = true,
                        IsInProducts = prod != null,
                        ProductId = prod?.Id,
                        Fee = prod?.Fee,
                        Mu = prod?.Mu,
                        MeasureUnitTitle = prod?.MeasureUnit?.Title,
                        CustomTitle = prod?.Sstt,
                        AddedDate = f.AddedDate
                    };
                }).ToList();
            }
            else
            {
                var stuffQuery = _context.Stuffs.AsNoTracking().AsQueryable();

                if (!string.IsNullOrWhiteSpace(Search))
                {
                    var term = Search.Trim();
                    stuffQuery = stuffQuery.Where(s => s.Code.Contains(term) || s.Title.Contains(term));
                }

                TotalCount = await stuffQuery.CountAsync();

                var stuffs = await stuffQuery
                    .OrderBy(s => s.Code)
                    .Skip((PageNumber - 1) * PageSize)
                    .Take(PageSize)
                    .ToListAsync();

                Items = stuffs.Select(s =>
                {
                    userProducts.TryGetValue(s.Code, out var prod);
                    var title = prod?.Sstt ?? s.Title;

                    return new StuffItemViewModel
                    {
                        Code = s.Code,
                        Title = title,
                        VatRate = s.VatRate,
                        Type = s.Type,
                        VatTax = s.VatTax,
                        Special = s.Special,
                        IsFavorite = favoriteCodes.ContainsKey(s.Code),
                        IsInProducts = prod != null,
                        ProductId = prod?.Id,
                        Fee = prod?.Fee,
                        Mu = prod?.Mu,
                        MeasureUnitTitle = prod?.MeasureUnit?.Title,
                        CustomTitle = prod?.Sstt,
                        AddedDate = favoriteCodes.TryGetValue(s.Code, out var dt) ? dt : null
                    };
                }).ToList();
            }

            if (IsAjaxRequest())
            {
                var viewData = ViewData ?? new Microsoft.AspNetCore.Mvc.ViewFeatures.ViewDataDictionary<IndexModel>(
                    new Microsoft.AspNetCore.Mvc.ModelBinding.EmptyModelMetadataProvider(),
                    new Microsoft.AspNetCore.Mvc.ModelBinding.ModelStateDictionary());
                viewData.Model = this;

                return new PartialViewResult
                {
                    ViewName = "_StuffTablePartial",
                    ViewData = viewData
                };
            }

            return Page();
        }

        private bool IsAjaxRequest()
        {
            return Request.Headers["X-Requested-With"] == "XMLHttpRequest" ||
                   Request.Headers.Accept.Any(a => a != null && a.Contains("application/json")) ||
                   Request.Query.ContainsKey("ajax") ||
                   Request.Query.ContainsKey("handler");
        }

        public async Task<IActionResult> OnPostToggleFavoriteAsync(string code, string? returnTab, string? search, int pageNumber = 1)
        {
            var username = User.Identity?.Name;
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(code))
            {
                if (IsAjaxRequest())
                {
                    return new JsonResult(new { success = false, message = "کاربر یا کد شناسه نامعتبر است." });
                }
                return RedirectToPage(new { Tab = returnTab ?? "fav", Search = search, PageNumber = pageNumber });
            }

            var existing = await _context.UserFavoriteStuffs
                .FirstOrDefaultAsync(f => f.Username == username && f.StuffCode == code);

            bool isFavorite;
            string message;
            if (existing != null)
            {
                _context.UserFavoriteStuffs.Remove(existing);
                await _context.SaveChangesAsync();
                isFavorite = false;
                message = $"شناسه {code} از لیست منتخب‌ها حذف شد.";
            }
            else
            {
                // بررسی وجود شناسه در کالاها
                var stuff = await _context.Stuffs.FindAsync(code);
                if (stuff != null)
                {
                    _context.UserFavoriteStuffs.Add(new UserFavoriteStuff
                    {
                        Username = username,
                        StuffCode = code,
                        AddedDate = DateTime.Now
                    });

                    // ایجاد یا اطمینان از وجود محصول جهت ذخیره قیمت و واحد سنجش
                    var existingProd = await _context.Products.FirstOrDefaultAsync(p => p.Creator == username && p.Sstid == code);
                    if (existingProd == null)
                    {
                        VatRate vra = VatRate.درصد_10;
                        if (stuff.VatRate == 0 || !stuff.VatTax) vra = VatRate.معاف;
                        else if (stuff.VatRate == 9) vra = VatRate.درصد_9;
                        else if (stuff.VatRate == 10) vra = VatRate.درصد_10;
                        else if (stuff.VatRate == 16) vra = VatRate.درصد_16;

                        _context.Products.Add(new Product
                        {
                            Sstid = stuff.Code,
                            Sstt = stuff.Title,
                            StuffType = stuff.Type,
                            Vra = vra,
                            Creator = username,
                            Fee = 0,
                            Mu = null
                        });
                    }

                    await _context.SaveChangesAsync();
                    isFavorite = true;
                    message = $"شناسه {code} با موفقیت به لیست منتخب‌ها افزوده شد.";
                }
                else
                {
                    if (IsAjaxRequest())
                    {
                        return new JsonResult(new { success = false, message = "شناسه کالای مورد نظر در بانک اطلاعاتی یافت نشد." });
                    }
                    ErrorMessage = "شناسه کالای مورد نظر در بانک اطلاعاتی یافت نشد.";
                    return RedirectToPage(new { Tab = returnTab ?? "fav", Search = search, PageNumber = pageNumber });
                }
            }

            var favCount = await _context.UserFavoriteStuffs.CountAsync(f => f.Username == username);

            if (IsAjaxRequest())
            {
                var prod = await _context.Products.Include(p => p.MeasureUnit).FirstOrDefaultAsync(p => p.Creator == username && p.Sstid == code);
                return new JsonResult(new
                {
                    success = true,
                    isFavorite = isFavorite,
                    favoritesCount = favCount,
                    message = message,
                    code = code,
                    fee = prod?.Fee ?? 0,
                    feeFormatted = (prod?.Fee.HasValue == true && prod.Fee > 0) ? $"{prod.Fee.Value:N0} ریال" : "۰ ریال",
                    mu = prod?.Mu ?? "",
                    muTitle = prod?.MeasureUnit?.Title ?? "تعیین‌نشده"
                });
            }

            SuccessMessage = message;
            return RedirectToPage(new { Tab = returnTab ?? "fav", Search = search, PageNumber = pageNumber });
        }

        public async Task<IActionResult> OnPostUpdateProductDetailsAsync(string code, decimal? fee, string? mu, string? customTitle)
        {
            var username = User.Identity?.Name;
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(code))
            {
                return new JsonResult(new { success = false, message = "درخواست نامعتبر است." });
            }

            code = code.Trim();
            var stuff = await _context.Stuffs.FindAsync(code);
            var prod = await _context.Products.Include(p => p.MeasureUnit).FirstOrDefaultAsync(p => p.Creator == username && p.Sstid == code);

            if (prod == null)
            {
                VatRate vra = VatRate.درصد_10;
                if (stuff != null)
                {
                    if (stuff.VatRate == 0 || !stuff.VatTax) vra = VatRate.معاف;
                    else if (stuff.VatRate == 9) vra = VatRate.درصد_9;
                    else if (stuff.VatRate == 10) vra = VatRate.درصد_10;
                    else if (stuff.VatRate == 16) vra = VatRate.درصد_16;
                }

                prod = new Product
                {
                    Sstid = code,
                    Sstt = !string.IsNullOrWhiteSpace(customTitle) ? customTitle.Trim() : (stuff?.Title ?? code),
                    StuffType = stuff?.Type ?? StuffType.شناسه_عمومی_تولید_داخل_کالا,
                    Vra = vra,
                    Creator = username,
                    Fee = fee ?? 0,
                    Mu = string.IsNullOrWhiteSpace(mu) ? null : mu.Trim()
                };
                _context.Products.Add(prod);
            }
            else
            {
                prod.Fee = fee ?? 0;
                prod.Mu = string.IsNullOrWhiteSpace(mu) ? null : mu.Trim();
                if (!string.IsNullOrWhiteSpace(customTitle))
                {
                    prod.Sstt = customTitle.Trim();
                }
            }

            // اطمینان از قرار داشتن در کالاهای منتخب
            var fav = await _context.UserFavoriteStuffs.FirstOrDefaultAsync(f => f.Username == username && f.StuffCode == code);
            if (fav == null)
            {
                _context.UserFavoriteStuffs.Add(new UserFavoriteStuff
                {
                    Username = username,
                    StuffCode = code,
                    AddedDate = DateTime.Now
                });
            }

            await _context.SaveChangesAsync();

            string? muTitle = null;
            if (!string.IsNullOrWhiteSpace(prod.Mu))
            {
                var measureUnit = await _context.MeasureUnits.FirstOrDefaultAsync(m => m.Code == prod.Mu);
                muTitle = measureUnit?.Title;
            }

            var favCount = await _context.UserFavoriteStuffs.CountAsync(f => f.Username == username);

            await _activityLogger.LogAsync(
                "کالا/خدمت",
                "ویرایش مشخصات کالا",
                $"تنظیم قیمت {(prod.Fee ?? 0):N0} ریال و واحد سنجش {muTitle ?? "تعیین‌نشده"} برای شناسه {code}",
                entityId: prod.Id.ToString());

            return new JsonResult(new
            {
                success = true,
                message = $"مشخصات کالا «{prod.Sstt}» با موفقیت ذخیره شد.",
                code = prod.Sstid,
                title = prod.Sstt,
                fee = prod.Fee ?? 0,
                feeFormatted = (prod.Fee.HasValue && prod.Fee > 0) ? $"{prod.Fee.Value:N0} ریال" : "۰ ریال",
                mu = prod.Mu ?? "",
                muTitle = muTitle ?? "تعیین‌نشده",
                favoritesCount = favCount
            });
        }

        public async Task<IActionResult> OnPostAddToProductsAsync(string code, string? returnTab, string? search, int pageNumber = 1)
        {
            var username = User.Identity?.Name;
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(code))
            {
                if (IsAjaxRequest())
                {
                    return new JsonResult(new { success = false, message = "درخواست نامعتبر است." });
                }
                return RedirectToPage(new { Tab = returnTab ?? "fav", Search = search, PageNumber = pageNumber });
            }

            var stuff = await _context.Stuffs.FindAsync(code);
            if (stuff == null)
            {
                if (IsAjaxRequest())
                {
                    return new JsonResult(new { success = false, message = "شناسه کالا در بانک اطلاعاتی یافت نشد." });
                }
                ErrorMessage = "شناسه کالا در بانک اطلاعاتی یافت نشد.";
                return RedirectToPage(new { Tab = returnTab ?? "fav", Search = search, PageNumber = pageNumber });
            }

            // بررسی تکراری نبودن در لیست محصولات کاربر
            var existingProduct = await _context.Products
                .FirstOrDefaultAsync(p => p.Creator == username && p.Sstid == code);

            if (existingProduct != null)
            {
                var msg = $"کالای «{stuff.Title}» قبلاً در لیست کالاهای شما ثبت شده است.";
                if (IsAjaxRequest())
                {
                    return new JsonResult(new { success = false, message = msg, alreadyExists = true, code = code });
                }
                ErrorMessage = msg;
                return RedirectToPage(new { Tab = returnTab ?? "fav", Search = search, PageNumber = pageNumber });
            }

            // تبدیل نرخ مالیات به انوم VatRate
            VatRate vra = VatRate.درصد_10;
            if (stuff.VatRate == 0 || !stuff.VatTax)
            {
                vra = VatRate.معاف;
            }
            else if (stuff.VatRate == 9)
            {
                vra = VatRate.درصد_9;
            }
            else if (stuff.VatRate == 10)
            {
                vra = VatRate.درصد_10;
            }
            else if (stuff.VatRate == 16)
            {
                vra = VatRate.درصد_16;
            }

            var product = new Product
            {
                Sstid = stuff.Code,
                Sstt = stuff.Title,
                StuffType = stuff.Type,
                Vra = vra,
                Creator = username,
                Fee = 0
            };

            _context.Products.Add(product);
            await _context.SaveChangesAsync();

            await _activityLogger.LogAsync(
                "کالا/خدمت",
                "افزودن از بانک شناسه‌ها",
                $"افزودن کالا {stuff.Code} ({stuff.Title}) به لیست کالاهای کاربر",
                entityId: product.Id.ToString());

            var successMsg = $"کالای «{stuff.Title}» با موفقیت به لیست کالاهای شما اضافه شد.";

            if (IsAjaxRequest())
            {
                return new JsonResult(new
                {
                    success = true,
                    message = successMsg,
                    code = code,
                    productId = product.Id
                });
            }

            SuccessMessage = successMsg;
            return RedirectToPage(new { Tab = returnTab ?? "fav", Search = search, PageNumber = pageNumber });
        }

        public async Task<IActionResult> OnPostCreateCustomProductAsync(
            string sstid,
            string sstt,
            StuffType stuffType,
            string? mu,
            decimal? fee,
            VatRate vra)
        {
            var username = User.Identity?.Name;
            if (string.IsNullOrWhiteSpace(username))
            {
                return new JsonResult(new { success = false, message = "کاربر وارد سامانه نشده است." });
            }

            if (string.IsNullOrWhiteSpace(sstid) || sstid.Trim().Length != 13 || !sstid.Trim().All(char.IsDigit))
            {
                return new JsonResult(new { success = false, message = "شناسه کالا/خدمت باید دقیقاً یک عدد ۱۳ رقمی باشد." });
            }

            if (string.IsNullOrWhiteSpace(sstt))
            {
                return new JsonResult(new { success = false, message = "وارد کردن شرح کالا/خدمت الزامی است." });
            }

            sstid = sstid.Trim();
            sstt = sstt.Trim();
            mu = string.IsNullOrWhiteSpace(mu) ? null : mu.Trim();

            // ۱. ثبت یا به‌روزرسانی در جدول Products
            var prod = await _context.Products.FirstOrDefaultAsync(p => p.Creator == username && p.Sstid == sstid);
            if (prod == null)
            {
                prod = new Product
                {
                    Sstid = sstid,
                    Sstt = sstt,
                    StuffType = stuffType,
                    Vra = vra,
                    Creator = username,
                    Fee = fee ?? 0,
                    Mu = mu
                };
                _context.Products.Add(prod);
            }
            else
            {
                prod.Sstt = sstt;
                prod.StuffType = stuffType;
                prod.Vra = vra;
                prod.Fee = fee ?? 0;
                prod.Mu = mu;
            }

            // ۲. افزودن خودکار به UserFavoriteStuffs کاربر (بدون افزودن به جدول عمومی Stuffs جهت ایزوله‌سازی کالا برای این مشتری)
            var fav = await _context.UserFavoriteStuffs.FirstOrDefaultAsync(f => f.Username == username && f.StuffCode == sstid);
            if (fav == null)
            {
                _context.UserFavoriteStuffs.Add(new UserFavoriteStuff
                {
                    Username = username,
                    StuffCode = sstid,
                    AddedDate = DateTime.Now
                });
            }

            await _context.SaveChangesAsync();

            string? muTitle = null;
            if (!string.IsNullOrWhiteSpace(prod.Mu))
            {
                var measureUnit = await _context.MeasureUnits.FirstOrDefaultAsync(m => m.Code == prod.Mu);
                muTitle = measureUnit?.Title;
            }

            var favCount = await _context.UserFavoriteStuffs.CountAsync(f => f.Username == username);

            await _activityLogger.LogAsync(
                "کالا/خدمت",
                "ثبت دستی کالا",
                $"ثبت دستی کالا «{sstt}» با شناسه {sstid} و افزودن به کالاهای منتخب",
                entityId: prod.Id.ToString());

            return new JsonResult(new
            {
                success = true,
                message = $"کالای «{sstt}» با شناسه {sstid} با موفقیت ثبت و به کالاهای منتخب افزوده شد.",
                code = sstid,
                title = sstt,
                type = stuffType.ToString().Replace("_", " "),
                fee = prod.Fee ?? 0,
                feeFormatted = (prod.Fee.HasValue && prod.Fee > 0) ? $"{prod.Fee.Value:N0} ریال" : "۰ ریال",
                mu = prod.Mu ?? "",
                muTitle = muTitle ?? "تعیین‌نشده",
                vra = (int)vra,
                vatRatePercent = (int)vra,
                favoritesCount = favCount
            });
        }
    }
}
