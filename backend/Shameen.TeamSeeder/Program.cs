using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Shameen.Api.Data;
using Shameen.Api.Models;

var sourceConnection = Environment.GetEnvironmentVariable("SHAMEEN_SQLSERVER_CONNECTION");
var targetConnection = Environment.GetEnvironmentVariable("DATABASE_URL");
var sharedPassword = Environment.GetEnvironmentVariable("SHAMEEN_SEED_PASSWORD");
var supervisorsOnly = args.Contains("--supervisors-only", StringComparer.OrdinalIgnoreCase);
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
var sourceEmployees = await source.Employees.AsNoTracking().Include(e => e.Branch).Where(e => e.Role != "Administrator" && e.Role != "Supervisor").OrderBy(e => e.Id)
    .Select(e => new { e.Name, e.Email, e.Role, BranchCity = e.Branch!.City, e.CollectionCountry, e.IsActive })
    .ToListAsync();
var teamEmployees = sourceEmployees.Where(_ => !supervisorsOnly).ToList();

if (sourceBranches.Count == 0 || (!supervisorsOnly && teamEmployees.Count == 0))
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

var unmappedEmployees = teamEmployees.Count(e => !branchByCity.ContainsKey(e.BranchCity));
if (unmappedEmployees > 0)
{
    Console.Error.WriteLine($"Refusing to continue: {unmappedEmployees} source employee accounts have no matching source branch city.");
    return 4;
}

var hostedEmployees = await target.Employees.Where(e => e.Role != "Administrator").ToListAsync();
var hostedByEmail = hostedEmployees.ToDictionary(e => e.Email, StringComparer.OrdinalIgnoreCase);
var accountsToCreate = teamEmployees.Count(e => !hostedByEmail.ContainsKey(e.Email));
var accountsToUpdate = teamEmployees.Count - accountsToCreate;
var supervisorAccounts = branchByCity.Select(pair => {
    var slug = System.Text.RegularExpressions.Regex.Replace(pair.Key.ToLowerInvariant(), "[^a-z0-9]+", ".").Trim('.');
    var email = $"supervisor.{slug}@shameen.local";
    return (City: pair.Key, Branch: pair.Value, Email: email);
}).ToList();
if (supervisorAccounts.Select(account => account.Email).Distinct(StringComparer.OrdinalIgnoreCase).Count() != supervisorAccounts.Count)
{
    Console.Error.WriteLine("Refusing to continue: two branch cities produce the same supervisor email address.");
    return 5;
}
var conflictingSupervisors = supervisorAccounts.Where(account => hostedByEmail.TryGetValue(account.Email, out var employee) && employee.Role != "Supervisor").ToList();
if (conflictingSupervisors.Count > 0)
{
    Console.Error.WriteLine($"Refusing to continue: {conflictingSupervisors.Count} generated supervisor email(s) already belong to non-supervisor accounts.");
    return 5;
}
var supervisorsToCreate = supervisorAccounts.Count(account => !hostedByEmail.ContainsKey(account.Email));
Console.WriteLine($"Ready to ensure {sourceBranches.Count} branches, {teamEmployees.Count} team accounts, and {supervisorAccounts.Count} city supervisors ({accountsToCreate} team accounts and {supervisorsToCreate} supervisors new).");
Console.WriteLine("The hosted administrator is preserved. Operating days, transfers, and float adjustments will not be changed.");
Console.Write("Type SEED to create/update branches and team accounts and supervisors only: ");
if (!string.Equals(Console.ReadLine(), "SEED", StringComparison.Ordinal))
{
    Console.WriteLine("Cancelled. No hosted records were changed.");
    return 0;
}

await using var transaction = await target.Database.BeginTransactionAsync();
await target.SaveChangesAsync();
var hasher = new PasswordHasher<Employee>();
foreach (var sourceEmployee in teamEmployees)
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

foreach (var account in supervisorAccounts)
{
    if (!hostedByEmail.TryGetValue(account.Email, out var supervisor))
    {
        supervisor = new Employee { Email = account.Email };
        target.Employees.Add(supervisor);
        hostedByEmail[account.Email] = supervisor;
    }
    supervisor.Name = $"City Supervisor - {account.City}";
    supervisor.Role = "Supervisor";
    supervisor.BranchId = account.Branch.Id;
    supervisor.CollectionCountry = null;
    supervisor.IsActive = true;
    supervisor.FloatAssigned = 0;
    supervisor.FloatBalance = 0;
    supervisor.FloatEnteredDayId = null;
    supervisor.PasswordHash = hasher.HashPassword(supervisor, sharedPassword);
}

await target.SaveChangesAsync();
await transaction.CommitAsync();
var supervisorCsv = new System.Text.StringBuilder("City,Name,Email\r\n");
foreach (var account in supervisorAccounts.OrderBy(account => account.City, StringComparer.OrdinalIgnoreCase))
    supervisorCsv.AppendLine($"{Csv(account.City)},{Csv($"City Supervisor - {account.City}")},{Csv(account.Email)}");
var supervisorCsvPath = Path.Combine(Environment.CurrentDirectory, "city-supervisor-accounts.csv");
await File.WriteAllTextAsync(supervisorCsvPath, supervisorCsv.ToString());
Console.WriteLine($"Seed complete. Ensured {sourceBranches.Count} branches, {teamEmployees.Count} employee accounts, and {supervisorAccounts.Count} city supervisors. No operational history was copied.");
Console.WriteLine($"Supervisor login list saved to {supervisorCsvPath} (email addresses only; no passwords included).");
return 0;

static string Csv(string value) => "\"" + value.Replace("\"", "\"\"") + "\"";

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

