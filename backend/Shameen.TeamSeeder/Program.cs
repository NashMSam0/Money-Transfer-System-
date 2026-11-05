using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Shameen.Api.Data;
using Shameen.Api.Models;

var sourceConnection = Environment.GetEnvironmentVariable("SHAMEEN_SQLSERVER_CONNECTION");
var targetConnection = Environment.GetEnvironmentVariable("DATABASE_URL");
var sharedPassword = Environment.GetEnvironmentVariable("SHAMEEN_SEED_PASSWORD");
var supervisorsOnly = args.Contains("--supervisors-only", StringComparer.OrdinalIgnoreCase);
var countryAgentsOnly = args.Contains("--country-agents-only", StringComparer.OrdinalIgnoreCase);
var cityAgentsOnly = args.Contains("--city-agents-only", StringComparer.OrdinalIgnoreCase);
if (string.IsNullOrWhiteSpace(targetConnection) || string.IsNullOrWhiteSpace(sharedPassword) || (!countryAgentsOnly && !cityAgentsOnly && string.IsNullOrWhiteSpace(sourceConnection)))
{
    Console.Error.WriteLine(countryAgentsOnly || cityAgentsOnly
        ? "The Supabase Session Pooler URI and account password must be supplied through an agent helper script."
        : "The local SQL connection, Supabase Session Pooler URI, and account password must be supplied through the helper script.");
    return 2;
}

var targetOptions = new DbContextOptionsBuilder<TransferDbContext>().UseNpgsql(ToNpgsql(targetConnection)).Options;
await using var target = new TransferDbContext(targetOptions);

if (cityAgentsOnly)
{
    var branches = await target.Branches.OrderBy(b => b.Country).ThenBy(b => b.City).ToListAsync();
    var destinations = await target.DestinationCities.AsNoTracking().OrderBy(c => c.Country).ThenBy(c => c.City).ToListAsync();
    var branchesToAdd = destinations
        .Where(city => !branches.Any(branch => branch.Country.Equals(city.Country, StringComparison.OrdinalIgnoreCase) && branch.City.Equals(city.City, StringComparison.OrdinalIgnoreCase)))
        .Select(city =>
        {
            var suffix = $", {city.Country}";
            var prefix = $"Shameen {city.City}";
            var name = prefix[..Math.Min(prefix.Length, 80 - suffix.Length)].TrimEnd() + suffix;
            return new Branch { Name = name, City = city.City, Country = city.Country };
        })
        .ToList();
    branches.AddRange(branchesToAdd);
    if (branches.Count == 0)
    {
        Console.Error.WriteLine("No hosted branches or destination cities were found. Start the API once so it can seed location data, then run this again.");
        return 8;
    }

    var employees = await target.Employees.OrderBy(e => e.Id).ToListAsync();
    var customers = await target.CustomerAccounts.AsNoTracking().Select(c => c.Email).ToListAsync();
    var agentAccounts = branches.Select(branch =>
    {
        var activeAgent = employees.FirstOrDefault(employee => employee.BranchId == branch.Id && employee.IsActive && employee.Role is "Transfer agent" or "Senior agent");
        var slug = System.Text.RegularExpressions.Regex.Replace($"{branch.Country}.{branch.City}".ToLowerInvariant(), "[^a-z0-9]+", ".").Trim('.');
        return (Branch: branch, Existing: activeAgent, Email: $"agent.{slug}@shameen.local");
    }).ToList();
    var duplicateEmails = agentAccounts.GroupBy(account => account.Email, StringComparer.OrdinalIgnoreCase).Where(group => group.Count() > 1).ToList();
    var emailConflicts = agentAccounts.Where(account => customers.Contains(account.Email, StringComparer.OrdinalIgnoreCase)
        || employees.Any(employee => employee.Email.Equals(account.Email, StringComparison.OrdinalIgnoreCase) && !ReferenceEquals(employee, account.Existing))).ToList();
    if (duplicateEmails.Count > 0 || emailConflicts.Count > 0)
    {
        Console.Error.WriteLine("No accounts were changed. Generated city-agent emails conflict with existing accounts.");
        foreach (var email in duplicateEmails.Select(group => group.Key).Concat(emailConflicts.Select(account => account.Email)).Distinct(StringComparer.OrdinalIgnoreCase))
            Console.Error.WriteLine(email);
        return 9;
    }

    var cityAccountsToCreate = agentAccounts.Count(account => account.Existing is null);
    var loginsToEnable = agentAccounts.Count(account => account.Existing is { PasswordHash: null });
    Console.WriteLine($"Found {destinations.Count} destination cities and {branches.Count} branch locations. {branchesToAdd.Count} branches need to be added; {cityAccountsToCreate} locations need an agent account; {loginsToEnable} existing active agents need a login.");
    Console.WriteLine("Existing passwords, transfer history, and float balances will be left unchanged.");
    Console.Write("Type SEED to continue: ");
    if (!string.Equals(Console.ReadLine(), "SEED", StringComparison.Ordinal))
    {
        Console.WriteLine("Cancelled. No hosted records were changed.");
        return 0;
    }

    await using var cityTransaction = await target.Database.BeginTransactionAsync();
    target.Branches.AddRange(branchesToAdd);
    await target.SaveChangesAsync();
    var cityHasher = new PasswordHasher<Employee>();
    var loginRows = new List<(string Country, string City, string Name, string Email, string Result)>();
    foreach (var account in agentAccounts)
    {
        var employee = account.Existing;
        var isNew = employee is null;
        if (employee is null)
        {
            employee = new Employee { Name = $"{account.Branch.City} Transfer Agent", Email = account.Email, Role = "Transfer agent", BranchId = account.Branch.Id, IsActive = true };
            target.Employees.Add(employee);
        }
        if (employee.PasswordHash is null) employee.PasswordHash = cityHasher.HashPassword(employee, sharedPassword!);
        loginRows.Add((account.Branch.Country, account.Branch.City, employee.Name, employee.Email, isNew ? "created" : account.Existing!.PasswordHash is null ? "login enabled" : "existing login unchanged"));
    }
    await target.SaveChangesAsync();
    await cityTransaction.CommitAsync();

    var csv = new System.Text.StringBuilder("Country,City,Name,Email,Result\r\n");
    foreach (var row in loginRows.OrderBy(row => row.Country, StringComparer.OrdinalIgnoreCase).ThenBy(row => row.City, StringComparer.OrdinalIgnoreCase))
        csv.AppendLine($"{Csv(row.Country)},{Csv(row.City)},{Csv(row.Name)},{Csv(row.Email)},{Csv(row.Result)}");
    var csvPath = Path.Combine(Environment.CurrentDirectory, "city-agent-accounts.csv");
    await File.WriteAllTextAsync(csvPath, csv.ToString());
    Console.WriteLine($"City agents complete. Ensured {branches.Count} city branches; created {branchesToAdd.Count} branches, {cityAccountsToCreate} agents, and enabled {loginsToEnable} existing agent logins.");
    Console.WriteLine($"Account list saved to {csvPath}. Passwords are not included.");
    return 0;
}

if (countryAgentsOnly)
{
    var countries = new (string Country, string City, string Name, string Email)[]
    {
        ("Zambia", "Lusaka", "Chipo Mwansa", "chipo.mwansa@shameen.local"),
        ("Zimbabwe", "Harare", "Tariro Moyo", "tariro.moyo@shameen.local"),
        ("South Africa", "Johannesburg", "Thandi Mokoena", "thandi.mokoena@shameen.local"),
        ("Tanzania", "Dar es Salaam", "Neema Mushi", "neema.mushi@shameen.local"),
        ("Kenya", "Nairobi", "Wanjiku Kamau", "wanjiku.kamau@shameen.local"),
        ("Uganda", "Kampala", "Achen Okello", "achen.okello@shameen.local"),
        ("United Kingdom", "London", "Oliver Bennett", "oliver.bennett@shameen.local")
    };
    var branches = await target.Branches.AsNoTracking().ToListAsync();
    var missingCountries = countries.Where(account => !branches.Any(branch => branch.Country.Equals(account.Country, StringComparison.OrdinalIgnoreCase))).Select(account => account.Country).Distinct().ToArray();
    if (missingCountries.Length > 0)
    {
        Console.Error.WriteLine("No hosted branch was found for: " + string.Join(", ", missingCountries));
        return 6;
    }
    var existingEmployees = await target.Employees.ToListAsync();
    var conflicts = countries.Where(account => existingEmployees.Any(employee => employee.Email.Equals(account.Email, StringComparison.OrdinalIgnoreCase) && employee.Role != "Transfer agent")).ToList();
    if (conflicts.Count > 0)
    {
        Console.Error.WriteLine("No accounts were changed. These email addresses already belong to another role: " + string.Join(", ", conflicts.Select(account => account.Email)));
        return 7;
    }
    var countryHasher = new PasswordHasher<Employee>();
    await using var countryTransaction = await target.Database.BeginTransactionAsync();
    var created = new List<(string Country, string Name, string Email, bool New)>();
    foreach (var account in countries)
    {
        var employee = existingEmployees.FirstOrDefault(item => item.Email.Equals(account.Email, StringComparison.OrdinalIgnoreCase));
        var isNew = employee is null;
        if (employee is null)
        {
            var branch = branches.FirstOrDefault(item => item.Country.Equals(account.Country, StringComparison.OrdinalIgnoreCase) && item.City.Equals(account.City, StringComparison.OrdinalIgnoreCase))
                ?? branches.First(item => item.Country.Equals(account.Country, StringComparison.OrdinalIgnoreCase));
            employee = new Employee { Name = account.Name, Email = account.Email, Role = "Transfer agent", BranchId = branch.Id, IsActive = true };
            employee.PasswordHash = countryHasher.HashPassword(employee, sharedPassword!);
            target.Employees.Add(employee);
            existingEmployees.Add(employee);
        }
        created.Add((account.Country, employee.Name, employee.Email, isNew));
    }
    await target.SaveChangesAsync();
    await countryTransaction.CommitAsync();
    Console.WriteLine("Country agents (new accounts receive the requested password; existing accounts are left unchanged):");
    foreach (var account in created) Console.WriteLine($"{account.Country}\t{account.Name}\t{account.Email}\t{(account.New ? "created" : "already existed; unchanged")}");
    return 0;
}

var sourceOptions = new DbContextOptionsBuilder<TransferDbContext>().UseSqlServer(sourceConnection!).Options;
await using var source = new TransferDbContext(sourceOptions);

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
        hostedBranch = new Branch { Name = sourceBranch.Name, City = sourceBranch.City, Country = sourceBranch.Country };
        target.Branches.Add(hostedBranch);
        hostedBranches.Add(hostedBranch);
    }
    else
    {
        hostedBranch.Name = sourceBranch.Name;
        hostedBranch.City = sourceBranch.City;
        hostedBranch.Country = sourceBranch.Country;
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

