using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Vatowiec.Core.Infrastructure.Persistence;

// Fabryki projektowe służą do generowania migracji/skryptów bez połączenia z SQL.
public sealed class SystemDbContextFactory : IDesignTimeDbContextFactory<SystemDbContext>
{
    public SystemDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<SystemDbContext>().UseSqlServer().Options);
}
