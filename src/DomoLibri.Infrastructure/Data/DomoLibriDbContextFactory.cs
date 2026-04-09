using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace DomoLibri.Infrastructure.Data;

public class DomoLibriDbContextFactory : IDesignTimeDbContextFactory<DomoLibriDbContext>
{
    public DomoLibriDbContext CreateDbContext(string[] args)
    {
        // 1. Localiza o arquivo appsettings.json no projeto API
        var basePath = Path.Combine(Directory.GetCurrentDirectory(), "..", "DomoLibri.Api");
        
        IConfigurationRoot configuration = new ConfigurationBuilder()
            .SetBasePath(basePath)
            .AddJsonFile("appsettings.json")
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var optionsBuilder = new DbContextOptionsBuilder<DomoLibriDbContext>();
        var connectionString = configuration.GetConnectionString("DefaultConnection");

        optionsBuilder.UseNpgsql(connectionString);

        // 2. Fornece uma implementação "dummy" de ITenantProvider para tempo de design
        return new DomoLibriDbContext(optionsBuilder.Options, new DesignTimeTenantProvider());
    }

    private class DesignTimeTenantProvider : ITenantProvider
    {
        // Em tempo de design (migrations), não há tenant ativo.
        public Guid? GetTenantId() => null;
    }
}
