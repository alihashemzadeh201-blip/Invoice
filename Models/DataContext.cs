using Invoice.Models;
using Microsoft.EntityFrameworkCore;
using Invoices.Models;

namespace Invoices.Models
{
    public class DataContext:DbContext
    {
        public DataContext(DbContextOptions<DataContext> options)
            :base(options)
        {

        }
        public DbSet<Invoice> Invoices { get; set; }
        public DbSet<InvoiceItem> InvoiceItems { get; set; }
        public DbSet<Payment> Payments { get; set; }
        public DbSet<User> Users { get; set; }
        public DbSet<MeasureUnit> MeasureUnits { get; set; }
        public DbSet<CurrencyCode> CurrencyCodes { get; set; }
        public DbSet<CreditPeriod> CreditPeriods { get; set; }
        public DbSet<CreditRequest> CreditRequests { get; set; }
        public DbSet<Transaction> Transactions { get; set; }
        public DbSet<Setting> Settings { get; set; }
        public DbSet<Profile> Profiles { get; set; }
        public DbSet<Stuff> Stuffs { get; set; }
        public DbSet<ImportTemplate> ImportTemplates { get; set; }
        public DbSet<ImportTemplateMapping> ImportTemplateMappings { get; set; }
        public DbSet<Invoices.Models.Product>? Products { get; set; }

        public DbSet<KarpooshehRequest> KarpooshehRequests { get; set; }
        public DbSet<CustomerDebt> CustomerDebts { get; set; }
        public DbSet<CustomerPayment> CustomerPayments { get; set; }
        public DbSet<UserActivityLog> UserActivityLogs { get; set; }
        public DbSet<BaleUserState> BaleUserStates { get; set; }
        public DbSet<UserFavoriteStuff> UserFavoriteStuffs { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<MeasureUnit>(e => e.HasMany(p => p.InvoiceItems).WithOne(e => e.MuObject).HasForeignKey(e => e.Mu).HasPrincipalKey(e => e.Code));
            modelBuilder.Entity<Product>(e => e.HasOne(p=>p.MeasureUnit).WithMany().HasForeignKey(e => e.Mu).HasPrincipalKey(e => e.Code));
            modelBuilder.Entity<CurrencyCode>(e => e.HasMany(p => p.InvoiceItems).WithOne(e => e.CutObject).HasForeignKey(e => e.Cut).HasPrincipalKey(e => e.AlphabetCode));
            modelBuilder.Entity<Profile>(e => e.HasOne(p => p.CreatorUser).WithMany().HasForeignKey(p => p.Creator));
            modelBuilder.Entity<CustomerDebt>(e => e.HasOne(d => d.Profile).WithMany().HasForeignKey(d => d.ProfileId).OnDelete(DeleteBehavior.Restrict));
            modelBuilder.Entity<CustomerPayment>(e => e.HasOne(p => p.Profile).WithMany().HasForeignKey(p => p.ProfileId).OnDelete(DeleteBehavior.Restrict));
            modelBuilder.Entity<UserActivityLog>(e =>
            {
                e.HasIndex(x => x.Timestamp);
                e.HasIndex(x => x.Category);
                e.HasIndex(x => x.Username);
            });
            modelBuilder.Entity<UserFavoriteStuff>(e =>
            {
                e.HasIndex(x => new { x.Username, x.StuffCode }).IsUnique();
            });
            //modelBuilder.Entity<Profile>(e => e.HasIndex(s => s.TinCode).IsUnique());
            //modelBuilder.Entity<Stuff>(e => e.HasMany(p => p.InvoiceItems).WithOne(e => e.Stuff).HasForeignKey(e => e.Sstid).HasPrincipalKey(e => e.Code));
        }

    }
}
