using Invoice.Code;
using Invoices.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Invoice.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class CustomerFinanceController : ControllerBase
    {
        private readonly ICustomerFinanceService _financeService;
        private readonly ILogger<CustomerFinanceController> _logger;
        private readonly DataContext _context;

        public CustomerFinanceController(ICustomerFinanceService financeService, ILogger<CustomerFinanceController> logger, DataContext context)
        {
            _financeService = financeService;
            _logger = logger;
            _context = context;
        }

        /// <summary>
        /// افزودن پرداخت جدید برای مشتری
        /// </summary>
        [HttpPost("payment")]
        public async Task<IActionResult> AddPayment([FromBody] AddPaymentRequest request)
        {
            try
            {
                if (!ModelState.IsValid)
                    return BadRequest(ModelState);

                // reseller can only add payments for their own profiles
                if (User.IsInRole("reseller"))
                {
                    var username = User.Identity?.Name;
                    var owns = await _context.Profiles.AnyAsync(p => p.Id == request.ProfileId && p.Creator == username);
                    if (!owns)
                        return Unauthorized(new { success = false, message = "دسترسی غیرمجاز" });
                }

                var payment = await _financeService.AddPaymentAsync(
                    request.ProfileId,
                    request.Amount,
                    request.PaymentMethod,
                    request.AccountNumber,
                    request.ReferenceNumber,
                    request.Description,
                    request.RelatedDebtId
                );

                return Ok(new { success = true, message = "پرداخت با موفقیت ثبت شد", payment });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { success = false, message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError($"خطا: {ex.Message}");
                return StatusCode(500, new { success = false, message = "خطایی رخ داد" });
            }
        }

        /// <summary>
        /// افزودن بدهی جدید برای مشتری
        /// </summary>
        [HttpPost("debt")]
        public async Task<IActionResult> AddDebt([FromBody] AddDebtRequest request)
        {
            try
            {
                if (!ModelState.IsValid)
                    return BadRequest(ModelState);

                // reseller can only add debts for their own profiles
                if (User.IsInRole("reseller"))
                {
                    var username = User.Identity?.Name;
                    var owns = await _context.Profiles.AnyAsync(p => p.Id == request.ProfileId && p.Creator == username);
                    if (!owns)
                        return Unauthorized(new { success = false, message = "دسترسی غیرمجاز" });
                }

                var debt = await _financeService.AddDebtAsync(
                    request.ProfileId,
                    request.Amount,
                    string.IsNullOrEmpty(request.DueDatePersian) ? (DateTime?)null : MyDate.GetEnglishDate(request.DueDatePersian),
                    request.Description
                );

                return Ok(new { success = true, message = "بدهی با موفقیت ثبت شد", debt });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { success = false, message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError($"خطا: {ex.Message}");
                return StatusCode(500, new { success = false, message = "خطایی رخ داد" });
            }
        }

        /// <summary>
        /// دریافت خلاصه وضعیت مالی مشتری
        /// </summary>
        [HttpGet("summary/{profileId}")]
        public async Task<IActionResult> GetFinancialSummary(int profileId)
        {
            try
            {
                if (User.IsInRole("reseller"))
                {
                    var username = User.Identity?.Name;
                    var owns = await _context.Profiles.AnyAsync(p => p.Id == profileId && p.Creator == username);
                    if (!owns)
                        return Unauthorized(new { success = false, message = "دسترسی غیرمجاز" });
                }

                var summary = await _financeService.GetCustomerFinancialSummaryAsync(profileId);
                return Ok(new { success = true, data = summary });
            }
            catch (Exception ex)
            {
                _logger.LogError($"خطا: {ex.Message}");
                return StatusCode(500, new { success = false, message = "خطایی رخ داد" });
            }
        }

        /// <summary>
        /// دریافت لیست پرداخت‌های مشتری
        /// </summary>
        [HttpGet("payments/{profileId}")]
        public async Task<IActionResult> GetCustomerPayments(int profileId)
        {
            try
            {
                if (User.IsInRole("reseller"))
                {
                    var username = User.Identity?.Name;
                    var owns = await _context.Profiles.AnyAsync(p => p.Id == profileId && p.Creator == username);
                    if (!owns)
                        return Unauthorized(new { success = false, message = "دسترسی غیرمجاز" });
                }

                var payments = await _financeService.GetCustomerPaymentsAsync(profileId);
                return Ok(new { success = true, data = payments });
            }
            catch (Exception ex)
            {
                _logger.LogError($"خطا: {ex.Message}");
                return StatusCode(500, new { success = false, message = "خطایی رخ داد" });
            }
        }

        /// <summary>
        /// دریافت لیست بدهی‌های مشتری
        /// </summary>
        [HttpGet("debts/{profileId}")]
        public async Task<IActionResult> GetCustomerDebts(int profileId)
        {
            try
            {
                if (User.IsInRole("reseller"))
                {
                    var username = User.Identity?.Name;
                    var owns = await _context.Profiles.AnyAsync(p => p.Id == profileId && p.Creator == username);
                    if (!owns)
                        return Unauthorized(new { success = false, message = "دسترسی غیرمجاز" });
                }

                var debts = await _financeService.GetCustomerDebtsAsync(profileId);
                return Ok(new { success = true, data = debts });
            }
            catch (Exception ex)
            {
                _logger.LogError($"خطا: {ex.Message}");
                return StatusCode(500, new { success = false, message = "خطایی رخ داد" });
            }
        }

        /// <summary>
        /// دریافت جزئیات پرداخت
        /// </summary>
        [HttpGet("payment/{paymentId}")]
        public async Task<IActionResult> GetPayment(int paymentId)
        {
            try
            {
                var payment = await _financeService.GetPaymentByIdAsync(paymentId);
                if (payment == null)
                    return NotFound(new { success = false, message = "پرداخت یافت نشد" });

                if (User.IsInRole("reseller"))
                {
                    var username = User.Identity?.Name;
                    var profile = await _context.Profiles.FindAsync(payment.ProfileId);
                    if (profile == null || profile.Creator != username)
                        return Unauthorized(new { success = false, message = "دسترسی غیرمجاز" });
                }

                return Ok(new { success = true, data = payment });
            }
            catch (Exception ex)
            {
                _logger.LogError($"خطا: {ex.Message}");
                return StatusCode(500, new { success = false, message = "خطایی رخ داد" });
            }
        }

        /// <summary>
        /// دریافت جزئیات بدهی
        /// </summary>
        [HttpGet("debt/{debtId}")]
        public async Task<IActionResult> GetDebt(int debtId)
        {
            try
            {
                var debt = await _financeService.GetDebtByIdAsync(debtId);
                if (debt == null)
                    return NotFound(new { success = false, message = "بدهی یافت نشد" });

                if (User.IsInRole("reseller"))
                {
                    var username = User.Identity?.Name;
                    var profile = await _context.Profiles.FindAsync(debt.ProfileId);
                    if (profile == null || profile.Creator != username)
                        return Unauthorized(new { success = false, message = "دسترسی غیرمجاز" });
                }

                return Ok(new { success = true, data = debt });
            }
            catch (Exception ex)
            {
                _logger.LogError($"خطا: {ex.Message}");
                return StatusCode(500, new { success = false, message = "خطایی رخ داد" });
            }
        }

        /// <summary>
        /// ویرایش پرداخت
        /// </summary>
        [HttpPut("payment/{paymentId}")]
        public async Task<IActionResult> UpdatePayment(int paymentId, [FromBody] UpdatePaymentRequest request)
        {
            try
            {
                if (!ModelState.IsValid)
                    return BadRequest(ModelState);

                if (User.IsInRole("reseller"))
                {
                    var username = User.Identity?.Name;
                    var existingPayment = await _context.CustomerPayments.FindAsync(paymentId);
                    if (existingPayment == null)
                        return NotFound(new { success = false, message = $"پرداخت با شناسه {paymentId} وجود ندارد" });

                    var profile = await _context.Profiles.FindAsync(existingPayment.ProfileId);
                    if (profile == null || profile.Creator != username)
                        return Unauthorized(new { success = false, message = "دسترسی غیرمجاز" });
                }

                var payment = await _financeService.UpdatePaymentAsync(
                    paymentId,
                    request.Amount,
                    request.PaymentMethod,
                    request.AccountNumber,
                    request.ReferenceNumber,
                    request.Description,
                    request.RelatedDebtId
                );

                return Ok(new { success = true, message = "پرداخت با موفقیت ویرایش شد", payment });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { success = false, message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError($"خطا: {ex.Message}");
                return StatusCode(500, new { success = false, message = "خطایی رخ داد" });
            }
        }

        /// <summary>
        /// حذف پرداخت
        /// </summary>
        [HttpDelete("payment/{paymentId}")]
        public async Task<IActionResult> DeletePayment(int paymentId)
        {
            try
            {
                if (User.IsInRole("reseller"))
                {
                    var username = User.Identity?.Name;
                    var existingPayment = await _context.CustomerPayments.FindAsync(paymentId);
                    if (existingPayment == null)
                        return NotFound(new { success = false, message = $"پرداخت با شناسه {paymentId} وجود ندارد" });

                    var profile = await _context.Profiles.FindAsync(existingPayment.ProfileId);
                    if (profile == null || profile.Creator != username)
                        return Unauthorized(new { success = false, message = "دسترسی غیرمجاز" });
                }

                var deleted = await _financeService.DeletePaymentAsync(paymentId);
                if (!deleted)
                    return NotFound(new { success = false, message = $"پرداخت با شناسه {paymentId} وجود ندارد" });

                return Ok(new { success = true, message = "پرداخت با موفقیت حذف شد" });
            }
            catch (Exception ex)
            {
                _logger.LogError($"خطا: {ex.Message}");
                return StatusCode(500, new { success = false, message = "خطایی رخ داد" });
            }
        }

        /// <summary>
        /// ویرایش بدهی
        /// </summary>
        [HttpPut("debt/{debtId}")]
        public async Task<IActionResult> UpdateDebt(int debtId, [FromBody] UpdateDebtRequest request)
        {
            try
            {
                if (!ModelState.IsValid)
                    return BadRequest(ModelState);

                if (User.IsInRole("reseller"))
                {
                    var username = User.Identity?.Name;
                    var existingDebt = await _context.CustomerDebts.FindAsync(debtId);
                    if (existingDebt == null)
                        return NotFound(new { success = false, message = $"بدهی با شناسه {debtId} وجود ندارد" });

                    var profile = await _context.Profiles.FindAsync(existingDebt.ProfileId);
                    if (profile == null || profile.Creator != username)
                        return Unauthorized(new { success = false, message = "دسترسی غیرمجاز" });
                }

                var debt = await _financeService.UpdateDebtAsync(
                    debtId,
                    request.Amount,
                    string.IsNullOrEmpty(request.DueDatePersian) ? (DateTime?)null : MyDate.GetEnglishDate(request.DueDatePersian),
                    request.Description
                );

                return Ok(new { success = true, message = "بدهی با موفقیت ویرایش شد", debt });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { success = false, message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError($"خطا: {ex.Message}");
                return StatusCode(500, new { success = false, message = "خطایی رخ داد" });
            }
        }

        /// <summary>
        /// حذف بدهی
        /// </summary>
        [HttpDelete("debt/{debtId}")]
        public async Task<IActionResult> DeleteDebt(int debtId)
        {
            try
            {
                if (User.IsInRole("reseller"))
                {
                    var username = User.Identity?.Name;
                    var existingDebt = await _context.CustomerDebts.FindAsync(debtId);
                    if (existingDebt == null)
                        return NotFound(new { success = false, message = $"بدهی با شناسه {debtId} وجود ندارد" });

                    var profile = await _context.Profiles.FindAsync(existingDebt.ProfileId);
                    if (profile == null || profile.Creator != username)
                        return Unauthorized(new { success = false, message = "دسترسی غیرمجاز" });
                }

                var deleted = await _financeService.DeleteDebtAsync(debtId);
                if (!deleted)
                    return NotFound(new { success = false, message = $"بدهی با شناسه {debtId} وجود ندارد" });

                return Ok(new { success = true, message = "بدهی با موفقیت حذف شد" });
            }
            catch (Exception ex)
            {
                _logger.LogError($"خطا: {ex.Message}");
                return StatusCode(500, new { success = false, message = "خطایی رخ داد" });
            }
        }

        /// <summary>
        /// تغییر وضعیت بدهی
        /// </summary>
        [HttpPut("debt/{debtId}/status")]
        public async Task<IActionResult> UpdateDebtStatus(int debtId, [FromBody] UpdateDebtStatusRequest request)
        {
            try
            {
                // Ensure reseller can only update debts of their own profiles
                if (User.IsInRole("reseller"))
                {
                    var username = User.Identity?.Name;
                    var existingDebt = await _context.CustomerDebts.FindAsync(debtId);
                    if (existingDebt == null)
                        return NotFound(new { success = false, message = $"بدهی با شناسه {debtId} وجود ندارد" });
                    var profile = await _context.Profiles.FindAsync(existingDebt.ProfileId);
                    if (profile == null || profile.Creator != username)
                        return Unauthorized(new { success = false, message = "دسترسی غیرمجاز" });
                }

                var debt = await _financeService.UpdateDebtStatusAsync(debtId, request.Status);
                return Ok(new { success = true, message = "وضعیت بدهی با موفقیت تغییر کرد", debt });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { success = false, message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError($"خطا: {ex.Message}");
                return StatusCode(500, new { success = false, message = "خطایی رخ داد" });
            }
        }
    }

    // Request Models
    public class AddPaymentRequest
    {
        public int ProfileId { get; set; }
        public long Amount { get; set; }
        public PaymentMethod PaymentMethod { get; set; }
        public string? AccountNumber { get; set; }
        public string? ReferenceNumber { get; set; }
        public string? Description { get; set; }
        public int? RelatedDebtId { get; set; }
    }

    public class AddDebtRequest
    {
        public int ProfileId { get; set; }
        public long Amount { get; set; }
        public string? DueDatePersian { get; set; }
        public string? Description { get; set; }
    }

    public class UpdatePaymentRequest
    {
        public long Amount { get; set; }
        public PaymentMethod PaymentMethod { get; set; }
        public string? AccountNumber { get; set; }
        public string? ReferenceNumber { get; set; }
        public string? Description { get; set; }
        public int? RelatedDebtId { get; set; }
    }

    public class UpdateDebtRequest
    {
        public long Amount { get; set; }
        public string? DueDatePersian { get; set; }
        public string? Description { get; set; }
    }

    public class UpdateDebtStatusRequest
    {
        public DebtStatus Status { get; set; }
    }
}
