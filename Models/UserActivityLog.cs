using System;
using System.ComponentModel.DataAnnotations;

namespace Invoices.Models
{
    public class UserActivityLog
    {
        public long Id { get; set; }

        [Display(Name = "زمان ثبت")]
        public DateTime Timestamp { get; set; } = DateTime.Now;

        [MaxLength(100)]
        [Display(Name = "نام کاربری")]
        public string Username { get; set; } = string.Empty;

        [MaxLength(50)]
        [Display(Name = "نقش کاربر")]
        public string? UserRole { get; set; }

        [Display(Name = "شناسه پروفایل")]
        public int? ProfileId { get; set; }

        [MaxLength(200)]
        [Display(Name = "عنوان پروفایل")]
        public string? ProfileTitle { get; set; }

        [MaxLength(100)]
        [Display(Name = "دسته‌بندی")]
        public string Category { get; set; } = string.Empty;

        [MaxLength(100)]
        [Display(Name = "نوع عملیات")]
        public string Action { get; set; } = string.Empty;

        [Display(Name = "شرح رویداد")]
        public string Description { get; set; } = string.Empty;

        [MaxLength(100)]
        [Display(Name = "شناسه رکورد / کد مالیاتی")]
        public string? EntityId { get; set; }

        [MaxLength(50)]
        [Display(Name = "آدرس IP")]
        public string? IpAddress { get; set; }

        [MaxLength(50)]
        [Display(Name = "وضعیت")]
        public string Status { get; set; } = "موفق";
    }
}
