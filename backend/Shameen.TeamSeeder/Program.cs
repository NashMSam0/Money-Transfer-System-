using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Shameen.Api.Data;
using Shameen.Api.Models;

var sourceConnection = Environment.GetEnvironmentVariable("SHAMEEN_SQLSERVER_CONNECTION");
var targetConnection = Environment.GetEnvironmentVariable("DATABASE_URL");
var sharedPassword = Environment.GetEnvironmentVariable("SHAMEEN_SEED_PASSWORD");
if (string.IsNullOrWhiteSpace(sourceConnection) || string.IsNullOrWhiteSpace(targetConnection) || string.IsNullOrWhiteSpace(sharedPassword))
{
    Console.Error.WriteLine("The local SQL connection, Supabase Session Pooler URI, and account password must be supplied through the helper script.");
    return 2;
}

var sourceOptions = new DbContextOptionsBuilder<TransferDbContext>().UseSqlServer(sourceConnection).Options;
var targetOptions = new DbContextOptionsBuilder<TransferDbContext>().UseNpgsql(ToNpgsql(targetConnection)).Options;
await using var source = new TransferDbContext(sourceOptions);
await using var target = new TransferDbContext(targetOptions);

var sourceBranches = await source.Branches.AsNoTracking().OrderBy(b => b.Id).ToListAsync();
var sourceEmployees = await source.Employees.AsNoTracking().Include(e => e.Branch).Where(e => e.Role != "Administrator").OrderBy(e => e.Id)
    .Select(e => new { e.Name, e.Email, e.Role, BranchCity = e.Branch!.City, e.CollectionCountry, e.IsActive })
    .ToListAsync();

if (sourceBranches.Count == 0 || sourceEmployees.Count == 0)
{
    Console.Error.WriteLine("The local database did not contain branches and non-administrator employee accounts to seed.");
    return 3;
}

var hostedBranches = await target.Branches.OrderBy(b => b.Id).ToListAsync();
var branchByCity = new Dictionary<string, Branch>(StringComparer.OrdinalIgnoreCase);
foreach (var sourceBranch in sourceBranches)
{
    var hostedBranch = hostedBranches.FirstOrDefault(b => string.Equals(b.City, sourceBranch.City, StringComparison.OrdinalIgnoreCase));
    if (hostedBranch is null)
    {
        hostedBranch = new Branch { Name = sourceBranch.Name, City = sourceBranch.City };
        target.Branches.Add(hostedBranch);
        hostedBranches.Add(hostedBranch);
    }
    else
    {
        hostedBranch.Name = sourceBranch.Name;
        hostedBranch.City = sourceBranch.City;
    }
    branchByCity[sourceBranch.City] = hostedBranch;
}

var unmappedEmployees = sourceEmployees.Count(e => !branchByCity.ContainsKey(e.BranchCity));
if (unmappedEmployees > 0)
{
    Console.Error.WriteLine($"Refusing to continue: {unmappedEmployees} source employee accounts have no matching source branch city.");
    return 4;
}

var hostedEmployees = await target.Employees.Where(e => e.Role != "Administrator").ToListAsync();
var hostedByEmail = hostedEmployees.ToDictionary(e => e.Email, StringComparer.OrdinalIgnoreCase);
var accountsToCreate = sourceEmployees.Count(e => !hostedByEmail.ContainsKey(e.Email));
var accountsToUpdate = sourceEmployees.Count - accountsToCreate;
Console.WriteLine($"Ready to ensure {sourceBranches.Count} branches and {sourceEmployees.Count} non-administrator accounts ({accountsToCreate} new, {accountsToUpdate} existing).");
Console.WriteLine("The hosted administrator is preserved. Operating days, transfers, and float adjustments will not be changed.");
Console.Write("Type SEED to create/update branches and team accounts only: ");
if (!string.Equals(Console.ReadLine(), "SEED", StringComparison.Ordinal))
{
    Console.WriteLine("Cancelled. No hosted records were changed.");
    return 0;
}

await using var transaction = await target.Database.BeginTransactionAsync();
await target.SaveChangesAsync();
var hasher = new PasswordHasher<Employee>();
foreach (var sourceEmployee in sourceEmployees)
{
    if (!hostedByEmail.TryGetValue(sourceEmployee.Email, out var employee))
    {
        employee = new Employee { Email = sourceEmployee.Email };
        target.Employees.Add(employee);
        hostedByEmail[sourceEmployee.Email] = employee;
    }

    employee.Name = sourceEmployee.Name;
    employee.Role = sourceEmployee.Role;
    employee.BranchId = branchByCity[sourceEmployee.BranchCity].Id;
    employee.CollectionCountry = sourceEmployee.CollectionCountry;
    employee.IsActive = sourceEmployee.IsActive;
    employee.FloatAssigned = 0;
    employee.FloatBalance = 0;
    employee.FloatEnteredDayId = null;
    employee.PasswordHash = hasher.HashPassword(employee, sharedPassword);
}

await target.SaveChangesAsync();
await transaction.CommitAsync();
Console.WriteLine($"Seed complete. Ensured {sourceBranches.Count} branches and {sourceEmployees.Count} employee accounts. No operational history was copied.");
return 0;

static string ToNpgsql(string value)
{
    if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || (uri.Scheme != "postgres" && uri.Scheme != "postgresql")) return value;
    var user = uri.UserInfo.Split(':', 2);
    return new NpgsqlConnectionStringBuilder
    {
        Host = uri.Host,
        Port = uri.Port > 0 ? uri.Port : 5432,
        Database = Uri.UnescapeDataString(uri.AbsolutePath.Trim('/')),
        Username = Uri.UnescapeDataString(user[0]),
        Password = user.Length > 1 ? Uri.UnescapeDataString(user[1]) : "",
        SslMode = SslMode.Require
    }.ConnectionString;
}

