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

        public IndexModel(
            DataContext context,
            IUserActivityLogger activityLogger,
            ILogger<IndexModel> logger)
        {
            _context = context;
            _activityLogger = activityLogger;
            _logger = logger;
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
            public DateTime? AddedDate { get; set; }
        }

        public List<StuffItemViewModel> Items { get; set; } = new();

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
            if (PageNumber < 1) PageNumber = 1;

            FavoritesCount = await _context.UserFavoriteStuffs
                .Where(f => f.Username == username)
                .CountAsync();

            // اگر کاربر هیچ علاقه‌مندی ندارد و تبی انتخاب نشده، به تب همه کالاها برود
            if (string.IsNullOrWhiteSpace(Tab) || (Tab == "fav" && FavoritesCount == 0 && string.IsNullOrWhiteSpace(Search)))
            {
                Tab = FavoritesCount > 0 ? "fav" : "all";
            }

            // کدهای موجود در علاقه‌مندی‌ها و در لیست محصولات کاربر
            var favoriteCodes = await _context.UserFavoriteStuffs
                .Where(f => f.Username == username)
                .ToDictionaryAsync(f => f.StuffCode, f => f.AddedDate, StringComparer.OrdinalIgnoreCase);

            var existingProductCodes = await _context.Products
                .Where(p => p.Creator == username)
                .Select(p => p.Sstid)
                .Distinct()
                .ToListAsync();
            var productCodesSet = new HashSet<string>(existingProductCodes, StringComparer.OrdinalIgnoreCase);

            if (Tab == "fav")
            {
                var favQuery = _context.UserFavoriteStuffs
                    .Include(f => f.Stuff)
                    .Where(f => f.Username == username)
                    .AsNoTracking()
                    .AsQueryable();

                if (!string.IsNullOrWhiteSpace(Search))
                {
                    var term = Search.Trim();
                    favQuery = favQuery.Where(f => f.StuffCode.Contains(term) || (f.Stuff != null && f.Stuff.Title.Contains(term)));
                }

                TotalCount = await favQuery.CountAsync();

                var favItems = await favQuery
                    .OrderByDescending(f => f.AddedDate)
                    .Skip((PageNumber - 1) * PageSize)
                    .Take(PageSize)
                    .ToListAsync();

                Items = favItems.Select(f => new StuffItemViewModel
                {
                    Code = f.StuffCode,
                    Title = f.Stuff?.Title ?? "بدون شرح",
                    VatRate = f.Stuff?.VatRate ?? 0,
                    Type = f.Stuff?.Type ?? StuffType.شناسه_عمومی_تولید_داخل_کالا,
                    VatTax = f.Stuff?.VatTax ?? true,
                    Special = f.Stuff?.Special ?? false,
                    IsFavorite = true,
                    IsInProducts = productCodesSet.Contains(f.StuffCode),
                    AddedDate = f.AddedDate
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

                Items = stuffs.Select(s => new StuffItemViewModel
                {
                    Code = s.Code,
                    Title = s.Title,
                    VatRate = s.VatRate,
                    Type = s.Type,
                    VatTax = s.VatTax,
                    Special = s.Special,
                    IsFavorite = favoriteCodes.ContainsKey(s.Code),
                    IsInProducts = productCodesSet.Contains(s.Code),
                    AddedDate = favoriteCodes.TryGetValue(s.Code, out var dt) ? dt : null
                }).ToList();
            }

            return Page();
        }

        public async Task<IActionResult> OnPostToggleFavoriteAsync(string code, string? returnTab, string? search, int pageNumber = 1)
        {
            var username = User.Identity?.Name;
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(code))
            {
                return RedirectToPage(new { Tab = returnTab ?? "fav", Search = search, PageNumber = pageNumber });
            }

            var existing = await _context.UserFavoriteStuffs
                .FirstOrDefaultAsync(f => f.Username == username && f.StuffCode == code);

            if (existing != null)
            {
                _context.UserFavoriteStuffs.Remove(existing);
                await _context.SaveChangesAsync();
                SuccessMessage = $"شناسه {code} از لیست علاقه‌مندی‌ها حذف شد.";
            }
            else
            {
                // بررسی وجود شناسه در کالاها
                var stuffExists = await _context.Stuffs.AnyAsync(s => s.Code == code);
                if (stuffExists)
                {
                    _context.UserFavoriteStuffs.Add(new UserFavoriteStuff
                    {
                        Username = username,
                        StuffCode = code,
                        AddedDate = DateTime.Now
                    });
                    await _context.SaveChangesAsync();
                    SuccessMessage = $"شناسه {code} با موفقیت به لیست علاقه‌مندی‌ها افزوده شد.";
                }
                else
                {
                    ErrorMessage = "شناسه کالای مورد نظر در بانک اطلاعاتی یافت نشد.";
                }
            }

            return RedirectToPage(new { Tab = returnTab ?? "fav", Search = search, PageNumber = pageNumber });
        }

        public async Task<IActionResult> OnPostAddToProductsAsync(string code, string? returnTab, string? search, int pageNumber = 1)
        {
            var username = User.Identity?.Name;
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(code))
            {
                return RedirectToPage(new { Tab = returnTab ?? "fav", Search = search, PageNumber = pageNumber });
            }

            var stuff = await _context.Stuffs.FindAsync(code);
            if (stuff == null)
            {
                ErrorMessage = "شناسه کالا در بانک اطلاعاتی یافت نشد.";
                return RedirectToPage(new { Tab = returnTab ?? "fav", Search = search, PageNumber = pageNumber });
            }

            // بررسی تکراری نبودن در لیست محصولات کاربر
            var existingProduct = await _context.Products
                .FirstOrDefaultAsync(p => p.Creator == username && p.Sstid == code);

            if (existingProduct != null)
            {
                ErrorMessage = $"کالای «{stuff.Title}» قبلاً در لیست کالاهای شما ثبت شده است.";
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

            SuccessMessage = $"کالای «{stuff.Title}» با موفقیت به لیست کالاهای شما اضافه شد.";

            return RedirectToPage(new { Tab = returnTab ?? "fav", Search = search, PageNumber = pageNumber });
        }
    }
}
