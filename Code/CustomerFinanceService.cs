using Invoices.Models;
using Microsoft.EntityFrameworkCore;

namespace Invoice.Code
{
    public interface ICustomerFinanceService
    {
        Task<CustomerPayment> AddPaymentAsync(int profileId, long amount, PaymentMethod method, string? accountNumber = null, string? referenceNumber = null, string? description = null, int? debtId = null);
        Task<CustomerPayment> UpdatePaymentAsync(int paymentId, long amount, PaymentMethod method, string? accountNumber = null, string? referenceNumber = null, string? description = null, int? debtId = null);
        Task<bool> DeletePaymentAsync(int paymentId);
        Task<CustomerDebt> AddDebtAsync(int profileId, long amount, DateTime? dueDate = null, string? description = null);
        Task<CustomerDebt> UpdateDebtAsync(int debtId, long amount, DateTime? dueDate = null, string? description = null);
        Task<bool> DeleteDebtAsync(int debtId);
        Task<CustomerDebt> UpdateDebtStatusAsync(int debtId, DebtStatus status);
        Task<object> GetCustomerFinancialSummaryAsync(int profileId);
        Task<List<CustomerPayment>> GetCustomerPaymentsAsync(int profileId);
        Task<List<CustomerDebt>> GetCustomerDebtsAsync(int profileId);
        Task<CustomerPayment?> GetPaymentByIdAsync(int paymentId);
        Task<CustomerDebt?> GetDebtByIdAsync(int debtId);
    }

    public class CustomerFinanceService : ICustomerFinanceService
    {
        private readonly DataContext _context;
        private readonly ILogger<CustomerFinanceService> _logger;

        public CustomerFinanceService(DataContext context, ILogger<CustomerFinanceService> logger)
        {
            _context = context;
            _logger = logger;
        }

        public async Task<CustomerPayment> AddPaymentAsync(int profileId, long amount, PaymentMethod method, string? accountNumber = null, string? referenceNumber = null, string? description = null, int? debtId = null)
        {
            try
            {
                if (profileId <= 0)
                    throw new ArgumentException("شناسه پروفایل معتبر نیست");

                var profileExists = await _context.Profiles.AnyAsync(p => p.Id == profileId);
                if (!profileExists)
                    throw new ArgumentException($"پروفایل با شناسه {profileId} وجود ندارد");

                var payment = new CustomerPayment
                {
                    ProfileId = profileId,
                    Amount = amount,
                    PaymentMethod = method,
                    AccountNumber = accountNumber,
                    ReferenceNumber = referenceNumber,
                    Description = description,
                    PaymentDate = DateTime.Now,
                    RelatedDebtId = debtId
                };

                _context.CustomerPayments.Add(payment);

                if (debtId.HasValue)
                {
                    await RecalculateDebtStatusAsync(debtId.Value);
                }

                await _context.SaveChangesAsync();
                _logger.LogInformation($"پرداخت جدید برای پروفایل {profileId} ثبت شد. مبلغ: {amount}");
                return payment;
            }
            catch (Exception ex)
            {
                _logger.LogError($"خطا هنگام ثبت پرداخت: {ex.Message}");
                throw;
            }
        }

        public async Task<CustomerPayment> UpdatePaymentAsync(int paymentId, long amount, PaymentMethod method, string? accountNumber = null, string? referenceNumber = null, string? description = null, int? debtId = null)
        {
            try
            {
                var payment = await _context.CustomerPayments.FirstOrDefaultAsync(p => p.Id == paymentId);
                if (payment == null)
                    throw new ArgumentException($"پرداخت با شناسه {paymentId} وجود ندارد");

                var oldDebtId = payment.RelatedDebtId;

                payment.Amount = amount;
                payment.PaymentMethod = method;
                payment.AccountNumber = accountNumber;
                payment.ReferenceNumber = referenceNumber;
                payment.Description = description;
                payment.RelatedDebtId = debtId;

                await _context.SaveChangesAsync();

                if (oldDebtId.HasValue)
                    await RecalculateDebtStatusAsync(oldDebtId.Value);

                if (debtId.HasValue)
                    await RecalculateDebtStatusAsync(debtId.Value);

                _logger.LogInformation($"پرداخت {paymentId} با موفقیت ویرایش شد");
                return payment;
            }
            catch (Exception ex)
            {
                _logger.LogError($"خطا هنگام ویرایش پرداخت: {ex.Message}");
                throw;
            }
        }

        public async Task<bool> DeletePaymentAsync(int paymentId)
        {
            try
            {
                var payment = await _context.CustomerPayments.FirstOrDefaultAsync(p => p.Id == paymentId);
                if (payment == null)
                    return false;

                var relatedDebtId = payment.RelatedDebtId;
                _context.CustomerPayments.Remove(payment);
                await _context.SaveChangesAsync();

                if (relatedDebtId.HasValue)
                    await RecalculateDebtStatusAsync(relatedDebtId.Value);

                _logger.LogInformation($"پرداخت {paymentId} با موفقیت حذف شد");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError($"خطا هنگام حذف پرداخت: {ex.Message}");
                throw;
            }
        }

        public async Task<CustomerDebt> AddDebtAsync(int profileId, long amount, DateTime? dueDate = null, string? description = null)
        {
            try
            {
                if (profileId <= 0)
                    throw new ArgumentException("شناسه پروفایل معتبر نیست");

                var profileExists = await _context.Profiles.AnyAsync(p => p.Id == profileId);
                if (!profileExists)
                    throw new ArgumentException($"پروفایل با شناسه {profileId} وجود ندارد");

                var debt = new CustomerDebt
                {
                    ProfileId = profileId,
                    Amount = amount,
                    CreatedDate = DateTime.Now,
                    DueDate = dueDate,
                    Description = description,
                    Status = DebtStatus.Active
                };

                _context.CustomerDebts.Add(debt);
                await _context.SaveChangesAsync();
                _logger.LogInformation($"بدهی جدید برای پروفایل {profileId} ثبت شد. مبلغ: {amount}");
                return debt;
            }
            catch (Exception ex)
            {
                _logger.LogError($"خطا هنگام ثبت بدهی: {ex.Message}");
                throw;
            }
        }

        public async Task<CustomerDebt> UpdateDebtAsync(int debtId, long amount, DateTime? dueDate = null, string? description = null)
        {
            try
            {
                var debt = await _context.CustomerDebts.FirstOrDefaultAsync(d => d.Id == debtId);
                if (debt == null)
                    throw new ArgumentException($"بدهی با شناسه {debtId} وجود ندارد");

                debt.Amount = amount;
                debt.DueDate = dueDate;
                debt.Description = description;

                await _context.SaveChangesAsync();
                await RecalculateDebtStatusAsync(debtId);

                _logger.LogInformation($"بدهی {debtId} با موفقیت ویرایش شد");
                return debt;
            }
            catch (Exception ex)
            {
                _logger.LogError($"خطا هنگام ویرایش بدهی: {ex.Message}");
                throw;
            }
        }

        public async Task<bool> DeleteDebtAsync(int debtId)
        {
            try
            {
                var debt = await _context.CustomerDebts.FirstOrDefaultAsync(d => d.Id == debtId);
                if (debt == null)
                    return false;

                var relatedPayments = await _context.CustomerPayments
                    .Where(p => p.RelatedDebtId == debtId)
                    .ToListAsync();

                if (relatedPayments.Any())
                    _context.CustomerPayments.RemoveRange(relatedPayments);

                _context.CustomerDebts.Remove(debt);
                await _context.SaveChangesAsync();

                _logger.LogInformation($"بدهی {debtId} با موفقیت حذف شد");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError($"خطا هنگام حذف بدهی: {ex.Message}");
                throw;
            }
        }

        public async Task<CustomerDebt> UpdateDebtStatusAsync(int debtId, DebtStatus status)
        {
            var debt = await _context.CustomerDebts.FirstOrDefaultAsync(d => d.Id == debtId);
            if (debt == null)
                throw new ArgumentException($"بدهی با شناسه {debtId} وجود ندارد");

            debt.Status = status;
            await _context.SaveChangesAsync();
            _logger.LogInformation($"وضعیت بدهی {debtId} به {status} تغییر کرد");
            return debt;
        }

        public async Task<object> GetCustomerFinancialSummaryAsync(int profileId)
        {
            var debts = await _context.CustomerDebts
                .Where(d => d.ProfileId == profileId)
                .ToListAsync();

            var payments = await _context.CustomerPayments
                .Where(p => p.ProfileId == profileId)
                .ToListAsync();

            var totalDebt = debts.Where(d => d.Status != DebtStatus.Paid && d.Status != DebtStatus.Cancelled).Sum(d => d.Amount);
            var totalPayment = payments.Sum(p => p.Amount);
            var paidDebts = debts.Where(d => d.Status == DebtStatus.Paid).Sum(d => d.Amount);
            var activeDebts = debts.Count(d => d.Status == DebtStatus.Active);
            var partiallyPaidDebts = debts.Count(d => d.Status == DebtStatus.PartiallyPaid);

            return new
            {
                profileId,
                totalDebt,
                totalPayment,
                paidDebts,
                remainingDebt = totalDebt,
                activeDebtsCount = activeDebts,
                partiallyPaidDebtsCount = partiallyPaidDebts,
                totalDebtsCount = debts.Count,
                totalPaymentsCount = payments.Count
            };
        }

        public async Task<List<CustomerPayment>> GetCustomerPaymentsAsync(int profileId)
        {
            return await _context.CustomerPayments
                .Where(p => p.ProfileId == profileId)
                .OrderByDescending(p => p.PaymentDate)
                .ToListAsync();
        }

        public async Task<List<CustomerDebt>> GetCustomerDebtsAsync(int profileId)
        {
            return await _context.CustomerDebts
                .Where(d => d.ProfileId == profileId)
                .OrderByDescending(d => d.CreatedDate)
                .ToListAsync();
        }

        public async Task<CustomerPayment?> GetPaymentByIdAsync(int paymentId)
        {
            return await _context.CustomerPayments.FirstOrDefaultAsync(p => p.Id == paymentId);
        }

        public async Task<CustomerDebt?> GetDebtByIdAsync(int debtId)
        {
            return await _context.CustomerDebts.FirstOrDefaultAsync(d => d.Id == debtId);
        }

        private async Task RecalculateDebtStatusAsync(int debtId)
        {
            var debt = await _context.CustomerDebts.FirstOrDefaultAsync(d => d.Id == debtId);
            if (debt == null)
                return;

            var totalPaid = await _context.CustomerPayments
                .Where(p => p.RelatedDebtId == debtId)
                .SumAsync(p => (long?)p.Amount) ?? 0;

            if (totalPaid <= 0)
                debt.Status = DebtStatus.Active;
            else if (totalPaid >= debt.Amount)
                debt.Status = DebtStatus.Paid;
            else
                debt.Status = DebtStatus.PartiallyPaid;

            await _context.SaveChangesAsync();
        }
    }
}
