using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace RoastIT.Data;

/// <summary>
/// Used only by the EF tools (<c>dotnet ef migrations add ...</c>), because the MAUI project
/// can't be the startup project. Points at a throwaway local file; it is never created at runtime.
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<RoastItDbContext>
{
    public RoastItDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<RoastItDbContext>()
            .UseSqlite("Data Source=roastit-design.db3;Foreign Keys=True")
            .Options;
        return new RoastItDbContext(options);
    }
}
