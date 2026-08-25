using LoanApi.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace LoanApi.Api.Infrastructure;

public sealed class LoanDbContext(DbContextOptions<LoanDbContext> options) : DbContext(options)
{
    public DbSet<Accountant> Accountants => Set<Accountant>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Loan> Loans => Set<Loan>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var accountants = modelBuilder.Entity<Accountant>();
        accountants.HasIndex(x => x.Username).IsUnique();
        accountants.Property(x => x.FirstName).HasMaxLength(80).IsRequired();
        accountants.Property(x => x.LastName).HasMaxLength(80).IsRequired();
        accountants.Property(x => x.Username).HasMaxLength(50).IsRequired();
        accountants.Property(x => x.PasswordHash).HasMaxLength(100).IsRequired();

        var users = modelBuilder.Entity<User>();
        users.HasIndex(x => x.Username).IsUnique();
        users.HasIndex(x => x.Email).IsUnique();
        users.Property(x => x.FirstName).HasMaxLength(80).IsRequired();
        users.Property(x => x.LastName).HasMaxLength(80).IsRequired();
        users.Property(x => x.Username).HasMaxLength(50).IsRequired();
        users.Property(x => x.Email).HasMaxLength(254).IsRequired();
        users.Property(x => x.PasswordHash).HasMaxLength(100).IsRequired();
        users.Property(x => x.MonthlyIncome).HasPrecision(18, 2);

        var loans = modelBuilder.Entity<Loan>();
        loans.Property(x => x.Amount).HasPrecision(18, 2);
        loans.Property(x => x.Currency).HasMaxLength(3).IsRequired();
        loans.Property(x => x.LoanType).HasConversion<string>().HasMaxLength(20);
        loans.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        loans.HasIndex(x => x.UserId);
        loans.ToTable(table =>
        {
            table.HasCheckConstraint("CK_Loans_Amount", "Amount > 0");
            table.HasCheckConstraint("CK_Loans_Period", "Period BETWEEN 1 AND 360");
        });

        users.HasMany(x => x.Loans)
            .WithOne(x => x.User)
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        var auditLogs = modelBuilder.Entity<AuditLog>();
        auditLogs.Property(x => x.Username).HasMaxLength(50);
        auditLogs.Property(x => x.Method).HasMaxLength(10).IsRequired();
        auditLogs.Property(x => x.Path).HasMaxLength(500).IsRequired();
        auditLogs.Property(x => x.Action).HasMaxLength(300);
        auditLogs.Property(x => x.IpAddress).HasMaxLength(64);
        auditLogs.Property(x => x.UserAgent).HasMaxLength(512);
        auditLogs.HasIndex(x => x.TimestampUtc);
        auditLogs.HasIndex(x => x.UserId);
    }
}
