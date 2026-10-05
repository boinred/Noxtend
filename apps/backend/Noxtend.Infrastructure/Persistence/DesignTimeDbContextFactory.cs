using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Noxtend.Infrastructure.Persistence;

/// <summary>
/// Design-time only — used by `dotnet ef migrations`.
///
/// Without this, the CLI boots the Api host to find a DbContext, and the host refuses
/// to start without real connection strings (§10.3 keeps them in a k8s Secret). Scaffolding
/// a migration would then require production-shaped configuration on a developer machine.
///
/// The connection string here is never used to connect: migrations are generated from the
/// model, not the database. It only tells the provider which SQL dialect to emit.
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<NoxtendDbContext>
{
    public NoxtendDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<NoxtendDbContext>()
            .UseSqlServer("Server=design-time;Database=Noxtend;Trusted_Connection=False;")
            .Options;

        return new NoxtendDbContext(options);
    }
}
