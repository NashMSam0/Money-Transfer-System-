using Microsoft.EntityFrameworkCore;
using Npgsql;
using Shameen.Api.Data;
using Shameen.Api.Models;

var sourceConnection = Environment.GetEnvironmentVariable("SHAMEEN_SQLSERVER_CONNECTION");
var targetConnection = Environment.GetEnvironmentVariable("DATABASE_URL");
if (string.IsNullOrWhiteSpace(sourceConnection) || string.IsNullOrWhiteSpace(targetConnection))
{
    Console.Error.WriteLine("Set SHAMEEN_SQLSERVER_CONNECTION and DATABASE_URL, then run this tool after the target API has applied migrations.");
    return 2;
}

var sourceOptions = new DbContextOptionsBuilder<TransferDbContext>().UseSqlServer(sourceConnection).Options;
var targetOptions = new DbContextOptionsBuilder<TransferDbContext>().UseNpgsql(ToNpgsql(targetConnection)).Options;
await using var source = new TransferDbContext(sourceOptions);
await using var target = new TransferDbContext(targetOptions);

var targetEmployees = await target.Employees.OrderBy(e => e.Id).ToListAsync();
var targetHasOperationalData = await target.Transactions.AnyAsync() || await target.OperatingDays.AnyAsync() || await target.FloatAdjustments.AnyAsync();
var hasOnlyInitialAdministrator = targetEmployees.Count == 1 && targetEmployees[0].Role == "Administrator";
if (targetHasOperationalData || (targetEmployees.Count > 0 && !hasOnlyInitialAdministrator))
{
    Console.Error.WriteLine("Refusing to import: target PostgreSQL contains operational records or employee accounts beyond its single initial administrator. Use a fresh database or perform a reviewed merge.");
    return 3;
}

var branches = await source.Branches.AsNoTracking().ToListAsync();
var employees = await source.Employees.AsNoTracking().ToListAsync();
var days = await source.OperatingDays.AsNoTracking().ToListAsync();
var transfers = await source.Transactions.AsNoTracking().ToListAsync();
var adjustments = await source.FloatAdjustments.AsNoTracking().ToListAsync();
var cities = await source.DestinationCities.AsNoTracking().ToListAsync();
Console.WriteLine($"Ready to copy: {branches.Count} branches, {employees.Count} employees, {days.Count} operating days, {transfers.Count} transfers, {adjustments.Count} float adjustments, {cities.Count} destination cities.");
if (hasOnlyInitialAdministrator) Console.WriteLine("The target's initial administrator account will be replaced by the source database's employee accounts.");
Console.Write("Type IMPORT to replace the target's initial setup data with these SQL Server records: ");
if (!string.Equals(Console.ReadLine(), "IMPORT", StringComparison.Ordinal))
{
    Console.WriteLine("Cancelled. No target data was changed.");
    return 0;
}

await using var transaction = await target.Database.BeginTransactionAsync();
target.Employees.RemoveRange(targetEmployees);
await target.SaveChangesAsync();
target.Branches.RemoveRange(await target.Branches.ToListAsync());
await target.SaveChangesAsync();
target.Branches.AddRange(branches.Select(x => new Branch { Id = x.Id, Name = x.Name, City = x.City }));
target.Employees.AddRange(employees.Select(x => new Employee { Id = x.Id, BranchId = x.BranchId, Name = x.Name, Email = x.Email, Role = x.Role, PasswordHash = x.PasswordHash, CollectionCountry = x.CollectionCountry, IsActive = x.IsActive, FloatAssigned = x.FloatAssigned, FloatBalance = x.FloatBalance, FloatEnteredDayId = x.FloatEnteredDayId }));
target.OperatingDays.AddRange(days.Select(x => new OperatingDay { Id = x.Id, BranchId = x.BranchId, Date = x.Date, OpeningFloat = x.OpeningFloat, OpenedAtUtc = AsUtc(x.OpenedAtUtc), OpenedByEmployeeId = x.OpenedByEmployeeId, ClosingFloat = x.ClosingFloat, ClosedAtUtc = x.ClosedAtUtc is null ? null : AsUtc(x.ClosedAtUtc.Value), ClosedByEmployeeId = x.ClosedByEmployeeId }));
target.Transactions.AddRange(transfers.Select(x => new Transfer { Id = x.Id, Reference = x.Reference, BranchId = x.BranchId, EmployeeId = x.EmployeeId, CustomerName = x.CustomerName, DestinationCountry = x.DestinationCountry, DestinationCity = x.DestinationCity, Amount = x.Amount, PayoutMethod = x.PayoutMethod, Status = x.Status, CreatedAtUtc = AsUtc(x.CreatedAtUtc), CollectedAtUtc = x.CollectedAtUtc is null ? null : AsUtc(x.CollectedAtUtc.Value), CollectionConfirmedByEmployeeId = x.CollectionConfirmedByEmployeeId, CollectionReference = x.CollectionReference }));
target.FloatAdjustments.AddRange(adjustments.Select(x => new FloatAdjustment { Id = x.Id, OperatingDayId = x.OperatingDayId, EmployeeId = x.EmployeeId, ActorEmployeeId = x.ActorEmployeeId, PreviousAmount = x.PreviousAmount, NewAmount = x.NewAmount, Reason = x.Reason, CreatedAtUtc = AsUtc(x.CreatedAtUtc) }));
await target.SaveChangesAsync();
foreach (var sql in new[] {
    """SELECT setval(pg_get_serial_sequence('"Branches"', 'Id'), COALESCE((SELECT MAX("Id") FROM "Branches"), 1), EXISTS(SELECT 1 FROM "Branches"))""",
    """SELECT setval(pg_get_serial_sequence('"Employees"', 'Id'), COALESCE((SELECT MAX("Id") FROM "Employees"), 1), EXISTS(SELECT 1 FROM "Employees"))""",
    """SELECT setval(pg_get_serial_sequence('"OperatingDays"', 'Id'), COALESCE((SELECT MAX("Id") FROM "OperatingDays"), 1), EXISTS(SELECT 1 FROM "OperatingDays"))""",
    """SELECT setval(pg_get_serial_sequence('"Transactions"', 'Id'), COALESCE((SELECT MAX("Id") FROM "Transactions"), 1), EXISTS(SELECT 1 FROM "Transactions"))""",
    """SELECT setval(pg_get_serial_sequence('"FloatAdjustments"', 'Id'), COALESCE((SELECT MAX("Id") FROM "FloatAdjustments"), 1), EXISTS(SELECT 1 FROM "FloatAdjustments"))"""
}) await target.Database.ExecuteSqlRawAsync(sql);
await transaction.CommitAsync();
Console.WriteLine("Import committed. Review the counts and opening/closing balances in the hosted app before switching operations.");
return 0;

static DateTime AsUtc(DateTime value) => value.Kind == DateTimeKind.Utc ? value : DateTime.SpecifyKind(value, DateTimeKind.Utc);
static string ToNpgsql(string value)
{
    if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || (uri.Scheme != "postgres" && uri.Scheme != "postgresql")) return value;
    var user = uri.UserInfo.Split(':', 2);
    return new NpgsqlConnectionStringBuilder { Host = uri.Host, Port = uri.Port > 0 ? uri.Port : 5432, Database = Uri.UnescapeDataString(uri.AbsolutePath.Trim('/')), Username = Uri.UnescapeDataString(user[0]), Password = user.Length > 1 ? Uri.UnescapeDataString(user[1]) : "", SslMode = SslMode.Require }.ConnectionString;
}



