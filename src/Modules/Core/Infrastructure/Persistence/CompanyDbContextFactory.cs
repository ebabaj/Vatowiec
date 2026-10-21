using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Vatowiec.Core.Application;

namespace Vatowiec.Core.Infrastructure.Persistence;

public sealed class CompanyDbContextFactory : IDesignTimeDbContextFactory<CompanyDbContext>
{
    public CompanyDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<CompanyDbContext>().UseSqlServer().Options, new CompanyContext(null));
}
