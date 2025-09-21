using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace VBTrader.Infrastructure.Database;

public class VBTraderDbContextFactory : IDesignTimeDbContextFactory<VBTraderDbContext>
{
    public VBTraderDbContext CreateDbContext(string[] args)
    {
        // Build configuration to read from appsettings.json
        var configurationBuilder = new ConfigurationBuilder()
            .SetBasePath(Path.Combine(Directory.GetCurrentDirectory(), "..", "VBTrader.UI"))
            .AddJsonFile("appsettings.json", optional: false);

        var configuration = configurationBuilder.Build();

        // Get connection string
        var connectionString = configuration.GetConnectionString("DefaultConnection");

        if (string.IsNullOrEmpty(connectionString))
        {
            throw new InvalidOperationException("Connection string 'DefaultConnection' not found in appsettings.json");
        }

        // Create DbContext options
        var optionsBuilder = new DbContextOptionsBuilder<VBTraderDbContext>();
        optionsBuilder.UseNpgsql(connectionString);

        return new VBTraderDbContext(optionsBuilder.Options);
    }
}