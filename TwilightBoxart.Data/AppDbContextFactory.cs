using Microsoft.EntityFrameworkCore.Design;

namespace TwilightBoxart.Data;

/// <summary>
/// Design-time only: what <c>dotnet ef migrations add</c> uses. The connection string is hardcoded
/// (and never opened) so scaffolding never depends on the web host's configuration being loadable.
/// </summary>
public class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite("Data Source=twilightboxart.db")
            .Options;
        return new AppDbContext(options);
    }
}
