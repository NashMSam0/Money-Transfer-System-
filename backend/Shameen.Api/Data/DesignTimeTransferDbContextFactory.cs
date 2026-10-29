using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Shameen.Api.Data;

public sealed class DesignTimeTransferDbContextFactory : IDesignTimeDbContextFactory<TransferDbContext>
{
    public TransferDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<TransferDbContext>()
            .UseNpgsql("Host=localhost;Database=ShameenInvestments;Username=postgres;Password=design-time-only")
            .Options;
        return new TransferDbContext(options);
    }
}
