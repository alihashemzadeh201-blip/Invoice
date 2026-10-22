using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Invoices.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace Invoice.Pages.Admin.ActivityLogs
{
    [Authorize(Roles = "admin")]
    public class IndexModel : PageModel
    {
        private readonly DataContext _context;

        public IndexModel(DataContext context)
        {
            _context = context;
        }

        public IList<UserActivityLog> Logs { get; set; } = new List<UserActivityLog>();

        [BindProperty(SupportsGet = true)]
        public string? Search { get; set; }

        [BindProperty(SupportsGet = true)]
        public string? Category { get; set; }

        [BindProperty(SupportsGet = true)]
        public string? SelectedUser { get; set; }

        [BindProperty(SupportsGet = true)]
        public string? Status { get; set; }

        [BindProperty(SupportsGet = true)]
        public int PageNumber { get; set; } = 1;

        public int PageSize { get; set; } = 50;
        public int TotalItems { get; set; }
        public int TotalPages => (int)Math.Ceiling((double)TotalItems / PageSize);

        public SelectList CategoriesList { get; set; } = default!;
        public SelectList UsersList { get; set; } = default!;
        public SelectList StatusList { get; set; } = default!;

        public int TotalLogsCount { get; set; }
        public int TodayLogsCount { get; set; }
        public int InvoiceLogsCount { get; set; }
        public int ErrorLogsCount { get; set; }

        public async Task OnGetAsync()
        {
            if (PageNumber < 1) PageNumber = 1;

            var today = DateTime.Today;

            // آمار کلی
            TotalLogsCount = await _context.UserActivityLogs.CountAsync();
            TodayLogsCount = await _context.UserActivityLogs.CountAsync(l => l.Timestamp >= today);
            InvoiceLogsCount = await _context.UserActivityLogs.CountAsync(l => l.Category == "فاکتور");
            ErrorLogsCount = await _context.UserActivityLogs.CountAsync(l => l.Status == "خطا");

            // دریافت لیست کاربران و دسته‌بندی‌ها برای فیلتر
            var users = await _context.UserActivityLogs
                .Select(l => l.Username)
                .Distinct()
                .OrderBy(u => u)
                .ToListAsync();

            var categories = await _context.UserActivityLogs
                .Select(l => l.Category)
                .Distinct()
                .OrderBy(c => c)
                .ToListAsync();

            CategoriesList = new SelectList(categories);
            UsersList = new SelectList(users);

            var statuses = new List<string> { "موفق", "خطا", "هشدار" };
            StatusList = new SelectList(statuses);

            // اعمال فیلترها روی لاگ‌ها
            IQueryable<UserActivityLog> query = _context.UserActivityLogs.AsNoTracking();

            if (!string.IsNullOrWhiteSpace(Search))
            {
                var term = Search.Trim();
                query = query.Where(l => l.Description.Contains(term) ||
                                         (l.EntityId != null && l.EntityId.Contains(term)) ||
                                         l.Username.Contains(term) ||
                                         (l.ProfileTitle != null && l.ProfileTitle.Contains(term)) ||
                                         l.Action.Contains(term));
            }

            if (!string.IsNullOrWhiteSpace(Category))
            {
                query = query.Where(l => l.Category == Category);
            }

            if (!string.IsNullOrWhiteSpace(SelectedUser))
            {
                query = query.Where(l => l.Username == SelectedUser);
            }

            if (!string.IsNullOrWhiteSpace(Status))
            {
                query = query.Where(l => l.Status == Status);
            }

            TotalItems = await query.CountAsync();

            Logs = await query
                .OrderByDescending(l => l.Timestamp)
                .Skip((PageNumber - 1) * PageSize)
                .Take(PageSize)
                .ToListAsync();
        }

        public async Task<IActionResult> OnPostClearOldLogsAsync(int days = 90)
        {
            var threshold = DateTime.Now.AddDays(-days);
            var oldLogs = await _context.UserActivityLogs.Where(l => l.Timestamp < threshold).ToListAsync();
            if (oldLogs.Any())
            {
                _context.UserActivityLogs.RemoveRange(oldLogs);
                await _context.SaveChangesAsync();
            }
            return RedirectToPage();
        }
    }
}
