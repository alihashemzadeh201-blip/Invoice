using Invoice.Code;
using Invoice.Models;
using Invoices.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Invoice.Pages.InvoiceManage
{
    public class FinalSummury
    {
        public long TotalDebt { get; set; }
        public long TotalPayment { get; set; }
        public long RemainingDebt { get; set; }

        public FinalSummury()
        {
            TotalDebt = 0L;
            TotalPayment = 0L;
            RemainingDebt = 0L;
        }
    }

    [Authorize(Roles = "admin,reseller")]
    public class CustomerLedgerModel : PageModel
    {
        private readonly DataContext _context;
        private readonly ISelectedProfile _profile;

        public long OverallTotalDebt { get; set; }
        public long OverallTotalPayment { get; set; }
        public long OverallRemainingDebt { get; set; }

        public List<Profile> Profiles { get; set; } = new List<Profile>();

        private const int DefaultPageSize = 10;

        [BindProperty(SupportsGet = true)]
        public string? SearchText { get; set; }

        [BindProperty(SupportsGet = true)]
        public int? SelectedProfileId { get; set; }

        [BindProperty(SupportsGet = true)]
        public int Page { get; set; } = 1;

        public int PageSize { get; set; } = DefaultPageSize;
        public int TotalProfilesCount { get; set; }
        public bool HasMoreProfiles => Page * PageSize < TotalProfilesCount;

        public Profile? SelectedProfile { get; set; }
        public FinalSummury? FinancialSummary { get; set; }
        public List<CustomerDebt> CustomerDebts { get; set; } = new List<CustomerDebt>();
        public List<CustomerPayment> CustomerPayments { get; set; } = new List<CustomerPayment>();

        public CustomerLedgerModel(DataContext context, ISelectedProfile profile)
        {
            _context = context;
            _profile = profile;
        }

        public async Task<IActionResult> OnGetAsync()
        {
            var query = _context.Profiles
                .Include(p => p.CreatorUser)
                .AsQueryable();

            // If reseller, show only profiles created by that reseller
            if (User.IsInRole("reseller"))
            {
                var username = User.Identity?.Name;
                query = query.Where(p => p.Creator == username);
            }

            // Compute overall totals across all accessible profiles (ignore search/paging)
            var accessibleProfileIds = await query.Select(p => p.Id).ToListAsync();
            if (accessibleProfileIds.Any())
            {
                var overallDebts = await _context.CustomerDebts
                    .Where(d => accessibleProfileIds.Contains(d.ProfileId) && d.Status != DebtStatus.Paid && d.Status != DebtStatus.Cancelled)
                    .ToListAsync();

                var overallPayments = await _context.CustomerPayments
                    .Where(p => accessibleProfileIds.Contains(p.ProfileId))
                    .ToListAsync();

                OverallTotalDebt = overallDebts.Sum(d => d.Amount);
                OverallTotalPayment = overallPayments.Sum(p => p.Amount);
                var overallRemaining = OverallTotalDebt - OverallTotalPayment;
                OverallRemainingDebt = overallRemaining < 0 ? 0 : overallRemaining;
            }
            else
            {
                OverallTotalDebt = 0;
                OverallTotalPayment = 0;
                OverallRemainingDebt = 0;
            }

            if (!string.IsNullOrEmpty(SearchText))
            {
                query = query.Where(p =>
                    p.FirstName.Contains(SearchText) ||
                    (p.Lastname != null && p.Lastname.Contains(SearchText)) ||
                    (p.TinCode != null && p.TinCode.Contains(SearchText)) ||
                    (p.CreatorUser != null && p.CreatorUser.Username.Contains(SearchText)));
            }

            TotalProfilesCount = await query.CountAsync();
            Page = Math.Max(Page, 1);

            Profiles = await query
                .OrderBy(p => p.FirstName)
                .ThenBy(p => p.Lastname)
                .Skip((Page - 1) * PageSize)
                .Take(PageSize)
                .ToListAsync();

            if (SelectedProfileId.HasValue && SelectedProfileId.Value > 0 && !Profiles.Any(p => p.Id == SelectedProfileId.Value))
            {
                var selectedProfile = await _context.Profiles
                    .Include(p => p.CreatorUser)
                    .FirstOrDefaultAsync(p => p.Id == SelectedProfileId.Value);

                if (selectedProfile != null)
                {
                    if (User.IsInRole("reseller") && selectedProfile.Creator != User.Identity?.Name)
                    {
                        return Forbid();
                    }
                    Profiles.Insert(0, selectedProfile);
                }
            }

            if (SelectedProfileId.HasValue && SelectedProfileId.Value > 0)
            {
                SelectedProfile = await _context.Profiles
                    .Include(p => p.CreatorUser)
                    .FirstOrDefaultAsync(p => p.Id == SelectedProfileId.Value);

                if (SelectedProfile != null && User.IsInRole("reseller") && SelectedProfile.Creator != User.Identity?.Name)
                {
                    return Forbid();
                }

                if (SelectedProfile != null)
                {
                    CustomerDebts = await _context.CustomerDebts
                        .Where(d => d.ProfileId == SelectedProfileId.Value)
                        .OrderByDescending(d => d.CreatedDate)
                        .ToListAsync();

                    CustomerPayments = await _context.CustomerPayments
                        .Where(p => p.ProfileId == SelectedProfileId.Value)
                        .OrderByDescending(p => p.PaymentDate)
                        .ToListAsync();

                    var totalDebt = CustomerDebts
                        .Where(d => d.Status != DebtStatus.Paid && d.Status != DebtStatus.Cancelled)
                        .Sum(d => d.Amount);

                    var totalPayment = CustomerPayments.Sum(p => p.Amount);
                    var remainingDebt = totalDebt - totalPayment;
                    if (remainingDebt < 0) remainingDebt = 0;

                    FinancialSummary = new FinalSummury
                    {
                        TotalDebt = totalDebt,
                        TotalPayment = totalPayment,
                        RemainingDebt = remainingDebt
                    };
                }
                else
                {
                    FinancialSummary = new FinalSummury();
                }
            }
            else
            {
                // No profile selected � ensure summary is initialized
                FinancialSummary = new FinalSummury();
            }

            return Page();
        }

        public async Task<IActionResult> OnGetProfilesAsync(int page = 1, int pageSize = DefaultPageSize, string? searchText = null)
        {
            var query = _context.Profiles
                .Include(p => p.CreatorUser)
                .AsQueryable();

            if (User.IsInRole("reseller"))
            {
                var username = User.Identity?.Name;
                query = query.Where(p => p.Creator == username);
            }

            if (!string.IsNullOrEmpty(searchText))
            {
                query = query.Where(p =>
                    p.FirstName.Contains(searchText) ||
                    (p.Lastname != null && p.Lastname.Contains(searchText)) ||
                    (p.TinCode != null && p.TinCode.Contains(searchText)) ||
                    (p.CreatorUser != null && p.CreatorUser.Username.Contains(searchText)));
            }

            var total = await query.CountAsync();
            page = Math.Max(page, 1);
            pageSize = Math.Max(pageSize, 5);

            var profiles = await query
                .OrderBy(p => p.FirstName)
                .ThenBy(p => p.Lastname)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(p => new {
                    p.Id,
                    p.DisplayName,
                    TinCode = p.TinCode ?? "-",
                    CreatorUsername = p.CreatorUser != null ? p.CreatorUser.Username : "ثبت نشده"
                })
                .ToListAsync();

            return new JsonResult(new
            {
                items = profiles,
                total,
                page,
                pageSize,
                hasMore = page * pageSize < total
            });
        }
    }
}

