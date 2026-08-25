using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace LoanApi.Api.Infrastructure;

[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
public sealed class LoanDbContextFactory : IDesignTimeDbContextFactory<LoanDbContext>
{
    public LoanDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<LoanDbContext>()
            .UseSqlServer("Server=localhost\\SQLEXPRESS02;Database=LoanApiDb;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=True")
            .Options;

        return new LoanDbContext(options);
    }
}
