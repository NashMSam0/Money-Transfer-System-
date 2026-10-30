using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using System.Security.Claims;
using Shameen.Api.Data;
using Shameen.Api.Models;

static string NormalizePostgresConnectionString(string value) {
    if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || (uri.Scheme != "postgres" && uri.Scheme != "postgresql")) return value;
    var credentials = uri.UserInfo.Split(':', 2);
    var builder = new Npgsql.NpgsqlConnectionStringBuilder {
        Host = uri.Host,
        Port = uri.Port > 0 ? uri.Port : 5432,
        Database = Uri.UnescapeDataString(uri.AbsolutePath.Trim('/')),
        Username = Uri.UnescapeDataString(credentials[0]),
        Password = credentials.Length > 1 ? Uri.UnescapeDataString(credentials[1]) : "",
        SslMode = Npgsql.SslMode.Require,
    };
    return builder.ConnectionString;
}

var builder = WebApplication.CreateBuilder(args);
var databaseProvider = builder.Configuration["DatabaseProvider"] ?? "SqlServer";
var isPostgres = databaseProvider.Equals("Postgres", StringComparison.OrdinalIgnoreCase) || databaseProvider.Equals("PostgreSQL", StringComparison.OrdinalIgnoreCase);
var configuredConnection = isPostgres
    ? builder.Configuration["DATABASE_URL"] ?? builder.Configuration.GetConnectionString("Transfers")
    : builder.Configuration.GetConnectionString("Transfers");
if (string.IsNullOrWhiteSpace(configuredConnection)) throw new InvalidOperationException(isPostgres ? "Configure DATABASE_URL for PostgreSQL." : "Configure ConnectionStrings:Transfers.");
var connectionString = isPostgres ? NormalizePostgresConnectionString(configuredConnection) : configuredConnection;
builder.Services.AddDbContext<TransferDbContext>(options => {
    if (isPostgres) options.UseNpgsql(connectionString);
    else options.UseSqlServer(connectionString);
});
var allowedOrigins = (builder.Configuration["Cors:AllowedOrigins"] ?? "http://localhost:4200,http://localhost:4201")
    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod().AllowCredentials()));
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(options => {
    options.Cookie.Name = ".Shameen.Session"; options.Cookie.HttpOnly = true; options.Cookie.SameSite = SameSiteMode.Lax; options.Cookie.SecurePolicy = builder.Environment.IsProduction() ? CookieSecurePolicy.Always : CookieSecurePolicy.SameAsRequest;
    options.ExpireTimeSpan = TimeSpan.FromHours(10); options.SlidingExpiration = true;
    options.Events.OnRedirectToLogin = context => { context.Response.StatusCode = StatusCodes.Status401Unauthorized; return Task.CompletedTask; };
    options.Events.OnRedirectToAccessDenied = context => { context.Response.StatusCode = StatusCodes.Status403Forbidden; return Task.CompletedTask; };
});
builder.Services.AddAuthorization(options => options.AddPolicy("AdminOnly", policy => policy.RequireRole("Administrator")));
builder.Services.AddScoped<PasswordHasher<Employee>>();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options => options.SwaggerDoc("v1", new OpenApiInfo { Title = "Shameen Investments Transfer API", Version = "v1", Description = "Transfer desk, employee float, and daily reconciliation API." }));
var app = builder.Build();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.UseSwagger();
app.UseSwaggerUI(options => options.SwaggerEndpoint("/swagger/v1/swagger.json", "Shameen Transfer API v1"));
var supportedCollectionCountries = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Kenya", "Zambia", "Zimbabwe", "South Africa", "Tanzania", "Uganda", "United Kingdom" };

await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<TransferDbContext>();
    if (isPostgres) {
        await db.Database.MigrateAsync();
        await DestinationCitySeedData.EnsureAsync(db);
    } else {
        await db.Database.EnsureCreatedAsync();
        await db.Database.ExecuteSqlRawAsync("""
        DECLARE @UpgradeExisting bit = CASE WHEN OBJECT_ID(N'dbo.Branches', N'U') IS NULL THEN 1 ELSE 0 END;
        IF OBJECT_ID(N'dbo.Branches', N'U') IS NULL
            CREATE TABLE dbo.Branches (Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_Branches PRIMARY KEY, Name nvarchar(80) NOT NULL, City nvarchar(80) NOT NULL);
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.Branches') AND name=N'IX_Branches_Name') CREATE UNIQUE INDEX IX_Branches_Name ON dbo.Branches(Name);
        IF NOT EXISTS (SELECT 1 FROM dbo.Branches WHERE Id=1)
        BEGIN
            SET IDENTITY_INSERT dbo.Branches ON;
            INSERT dbo.Branches (Id, Name, City) VALUES (1, N'Shameen Lusaka', N'Lusaka');
            IF NOT EXISTS (SELECT 1 FROM dbo.Branches WHERE Id=2) INSERT dbo.Branches (Id, Name, City) VALUES (2, N'Shameen Bulawayo', N'Bulawayo');
            IF NOT EXISTS (SELECT 1 FROM dbo.Branches WHERE Id=3) INSERT dbo.Branches (Id, Name, City) VALUES (3, N'Shameen Harare', N'Harare');
            SET IDENTITY_INSERT dbo.Branches OFF;
        END
        ELSE
        BEGIN
            IF NOT EXISTS (SELECT 1 FROM dbo.Branches WHERE Name=N'Shameen Lusaka') INSERT dbo.Branches (Name, City) VALUES (N'Shameen Lusaka', N'Lusaka');
            IF NOT EXISTS (SELECT 1 FROM dbo.Branches WHERE Name=N'Shameen Bulawayo') INSERT dbo.Branches (Name, City) VALUES (N'Shameen Bulawayo', N'Bulawayo');
            IF NOT EXISTS (SELECT 1 FROM dbo.Branches WHERE Name=N'Shameen Harare') INSERT dbo.Branches (Name, City) VALUES (N'Shameen Harare', N'Harare');
        END;
        IF COL_LENGTH(N'dbo.Employees', N'BranchId') IS NULL ALTER TABLE dbo.Employees ADD BranchId int NOT NULL CONSTRAINT DF_Employees_BranchId DEFAULT (1) WITH VALUES;
        IF COL_LENGTH(N'dbo.Employees', N'PasswordHash') IS NULL ALTER TABLE dbo.Employees ADD PasswordHash nvarchar(512) NULL;
        IF COL_LENGTH(N'dbo.Employees', N'CollectionCountry') IS NULL ALTER TABLE dbo.Employees ADD CollectionCountry nvarchar(80) NULL;
        IF COL_LENGTH(N'dbo.Employees', N'FloatEnteredDayId') IS NULL ALTER TABLE dbo.Employees ADD FloatEnteredDayId bigint NULL;
        IF OBJECT_ID(N'dbo.FloatAdjustments', N'U') IS NULL CREATE TABLE dbo.FloatAdjustments (Id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_FloatAdjustments PRIMARY KEY, OperatingDayId bigint NOT NULL, EmployeeId int NOT NULL, ActorEmployeeId int NOT NULL, PreviousAmount decimal(18,2) NOT NULL, NewAmount decimal(18,2) NOT NULL, Reason nvarchar(500) NOT NULL, CreatedAtUtc datetime2 NOT NULL, CONSTRAINT FK_FloatAdjustments_OperatingDays_OperatingDayId FOREIGN KEY (OperatingDayId) REFERENCES dbo.OperatingDays(Id), CONSTRAINT FK_FloatAdjustments_Employees_EmployeeId FOREIGN KEY (EmployeeId) REFERENCES dbo.Employees(Id), CONSTRAINT FK_FloatAdjustments_Employees_ActorEmployeeId FOREIGN KEY (ActorEmployeeId) REFERENCES dbo.Employees(Id));
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.FloatAdjustments') AND name=N'IX_FloatAdjustments_OperatingDayId_EmployeeId_CreatedAtUtc') CREATE INDEX IX_FloatAdjustments_OperatingDayId_EmployeeId_CreatedAtUtc ON dbo.FloatAdjustments(OperatingDayId, EmployeeId, CreatedAtUtc);
        IF COL_LENGTH(N'dbo.Transactions', N'BranchId') IS NULL ALTER TABLE dbo.Transactions ADD BranchId int NOT NULL CONSTRAINT DF_Transactions_BranchId DEFAULT (1) WITH VALUES;
        IF COL_LENGTH(N'dbo.Transactions', N'CollectedAtUtc') IS NULL ALTER TABLE dbo.Transactions ADD CollectedAtUtc datetime2 NULL;
        IF COL_LENGTH(N'dbo.Transactions', N'CollectionConfirmedByEmployeeId') IS NULL ALTER TABLE dbo.Transactions ADD CollectionConfirmedByEmployeeId int NULL;
        IF COL_LENGTH(N'dbo.Transactions', N'CollectionReference') IS NULL ALTER TABLE dbo.Transactions ADD CollectionReference nvarchar(120) NULL;
        IF COL_LENGTH(N'dbo.Transactions', N'DestinationCity') IS NULL ALTER TABLE dbo.Transactions ADD DestinationCity nvarchar(100) NULL;
        IF OBJECT_ID(N'dbo.DestinationCities', N'U') IS NULL CREATE TABLE dbo.DestinationCities (Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_DestinationCities PRIMARY KEY, Country nvarchar(80) NOT NULL, City nvarchar(100) NOT NULL);
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.DestinationCities') AND name=N'IX_DestinationCities_Country_City') CREATE UNIQUE INDEX IX_DestinationCities_Country_City ON dbo.DestinationCities(Country, City);
        INSERT INTO dbo.DestinationCities (Country, City)
        SELECT city.Country, city.City FROM (VALUES
            (N'Zambia', N'Lusaka'), (N'Zambia', N'Kitwe'), (N'Zambia', N'Ndola'), (N'Zambia', N'Livingstone'), (N'Zambia', N'Kabwe'), (N'Zambia', N'Chipata'), (N'Zambia', N'Solwezi'), (N'Zambia', N'Chingola'), (N'Zambia', N'Mufulira'), (N'Zambia', N'Kasama'),
            (N'South Africa', N'Johannesburg'), (N'South Africa', N'Cape Town'), (N'South Africa', N'Durban'), (N'South Africa', N'Pretoria'), (N'South Africa', N'Gqeberha'), (N'South Africa', N'Bloemfontein'), (N'South Africa', N'Polokwane'), (N'South Africa', N'Mbombela'), (N'South Africa', N'Kimberley'), (N'South Africa', N'East London'), (N'South Africa', N'Rustenburg'),
            (N'Tanzania', N'Dar es Salaam'), (N'Tanzania', N'Dodoma'), (N'Tanzania', N'Arusha'), (N'Tanzania', N'Mwanza'), (N'Tanzania', N'Zanzibar City'), (N'Tanzania', N'Mbeya'), (N'Tanzania', N'Morogoro'), (N'Tanzania', N'Tanga'), (N'Tanzania', N'Moshi'), (N'Tanzania', N'Iringa'), (N'Tanzania', N'Tabora'), (N'Tanzania', N'Kigoma'),
            (N'Kenya', N'Nairobi'), (N'Kenya', N'Mombasa'), (N'Kenya', N'Kisumu'), (N'Kenya', N'Nakuru'), (N'Kenya', N'Eldoret'), (N'Kenya', N'Thika'),
            (N'Zimbabwe', N'Harare'), (N'Zimbabwe', N'Bulawayo'), (N'Zimbabwe', N'Mutare'), (N'Zimbabwe', N'Gweru'), (N'Zimbabwe', N'Kwekwe'), (N'Zimbabwe', N'Masvingo'), (N'Zimbabwe', N'Chitungwiza'),
            (N'Uganda', N'Kampala'), (N'Uganda', N'Entebbe'), (N'Uganda', N'Jinja'), (N'Uganda', N'Mbarara'), (N'Uganda', N'Gulu'), (N'Uganda', N'Mbale'),
            (N'United Kingdom', N'London'), (N'United Kingdom', N'Birmingham'), (N'United Kingdom', N'Manchester'), (N'United Kingdom', N'Glasgow'), (N'United Kingdom', N'Leeds'), (N'United Kingdom', N'Liverpool'), (N'United Kingdom', N'Bristol')
        ) AS city(Country, City)
        WHERE NOT EXISTS (SELECT 1 FROM dbo.DestinationCities existing WHERE existing.Country=city.Country AND existing.City=city.City);
        IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name=N'FK_Transactions_Employees_CollectionConfirmedByEmployeeId') EXEC(N'ALTER TABLE dbo.Transactions ADD CONSTRAINT FK_Transactions_Employees_CollectionConfirmedByEmployeeId FOREIGN KEY (CollectionConfirmedByEmployeeId) REFERENCES dbo.Employees(Id)');
        IF COL_LENGTH(N'dbo.OperatingDays', N'BranchId') IS NULL ALTER TABLE dbo.OperatingDays ADD BranchId int NOT NULL CONSTRAINT DF_OperatingDays_BranchId DEFAULT (1) WITH VALUES;
        IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name=N'FK_Employees_Branches_BranchId') EXEC(N'ALTER TABLE dbo.Employees ADD CONSTRAINT FK_Employees_Branches_BranchId FOREIGN KEY (BranchId) REFERENCES dbo.Branches(Id)');
        IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name=N'FK_Transactions_Branches_BranchId') EXEC(N'ALTER TABLE dbo.Transactions ADD CONSTRAINT FK_Transactions_Branches_BranchId FOREIGN KEY (BranchId) REFERENCES dbo.Branches(Id)');
        IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name=N'FK_OperatingDays_Branches_BranchId') EXEC(N'ALTER TABLE dbo.OperatingDays ADD CONSTRAINT FK_OperatingDays_Branches_BranchId FOREIGN KEY (BranchId) REFERENCES dbo.Branches(Id)');
        IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.OperatingDays') AND name=N'IX_OperatingDays_Date') DROP INDEX IX_OperatingDays_Date ON dbo.OperatingDays;
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.OperatingDays') AND name=N'IX_OperatingDays_Date_BranchId') EXEC(N'CREATE UNIQUE INDEX IX_OperatingDays_Date_BranchId ON dbo.OperatingDays(Date, BranchId)');
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.Transactions') AND name=N'IX_Transactions_BranchId_CreatedAtUtc') EXEC(N'CREATE INDEX IX_Transactions_BranchId_CreatedAtUtc ON dbo.Transactions(BranchId, CreatedAtUtc)');
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.Employees') AND name=N'IX_Employees_BranchId') EXEC(N'CREATE INDEX IX_Employees_BranchId ON dbo.Employees(BranchId)');
        IF @UpgradeExisting=1
        BEGIN
            EXEC(N'UPDATE dbo.Employees SET BranchId=1 WHERE Email IN (N''amina@shameen.local'',N''daniel@shameen.local'')');
            EXEC(N'UPDATE dbo.Employees SET BranchId=2 WHERE Email=N''grace@shameen.local''');
            EXEC(N'UPDATE dbo.Employees SET BranchId=3 WHERE Email=N''peter@shameen.local''');
        END;
        EXEC(N'UPDATE dbo.Transactions SET Status=N''Awaiting collection'' WHERE Status=N''Completed'' AND CollectedAtUtc IS NULL');
        EXEC(N'UPDATE dbo.Employees SET BranchId=(SELECT Id FROM dbo.Branches WHERE Name=N''Shameen Bulawayo'') WHERE Email=N''grace@shameen.local'' AND BranchId=1');
        EXEC(N'UPDATE dbo.Employees SET BranchId=(SELECT Id FROM dbo.Branches WHERE Name=N''Shameen Harare'') WHERE Email=N''peter@shameen.local'' AND BranchId=1');
        EXEC(N'UPDATE dbo.Employees SET IsActive=1 WHERE Email=N''peter@shameen.local''');
        """);
        await DestinationCitySeedData.EnsureAsync(db);
    }
    if (!isPostgres && !await db.Employees.AnyAsync())
    {
        db.Employees.AddRange(
            new Employee { Name = "Amina Hassan", Email = "amina@shameen.local", Role = "Senior agent", BranchId = 1 },
            new Employee { Name = "Daniel Moyo", Email = "daniel@shameen.local", Role = "Transfer agent", BranchId = 1 },
            new Employee { Name = "Grace Ndlovu", Email = "grace@shameen.local", Role = "Transfer agent", BranchId = 2 },
            new Employee { Name = "Peter Okoro", Email = "peter@shameen.local", Role = "Transfer agent", BranchId = 3, IsActive = true });
        await db.SaveChangesAsync();
    }
}

ClaimsPrincipal PrincipalFor(Employee employee) {
    var claims = new List<Claim> {
        new(ClaimTypes.NameIdentifier, employee.Id.ToString()),
        new(ClaimTypes.Name, employee.Name),
        new(ClaimTypes.Email, employee.Email),
        new(ClaimTypes.Role, employee.Role),
        new("branch_id", employee.BranchId.ToString())
    };
    if (!string.IsNullOrWhiteSpace(employee.CollectionCountry)) { claims.Add(new Claim(ClaimTypes.Role, "CollectionAgent")); claims.Add(new Claim("collection_country", employee.CollectionCountry)); }
    return new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme));
}
int EmployeeIdFor(ClaimsPrincipal user) => int.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
int BranchIdFor(ClaimsPrincipal user, int? requested) => user.IsInRole("Administrator") ? requested ?? 1 : int.Parse(user.FindFirstValue("branch_id")!);
bool IsManager(ClaimsPrincipal user) => user.IsInRole("Administrator") || user.IsInRole("Supervisor");
bool CanManageBranch(ClaimsPrincipal user, int branchId) => user.IsInRole("Administrator") || (user.IsInRole("Supervisor") && BranchIdFor(user, null) == branchId);
object EmployeeView(Employee employee) => new { employee.Id, employee.BranchId, employee.Name, employee.Email, employee.Role, employee.CollectionCountry, employee.IsActive, employee.FloatAssigned, employee.FloatBalance, hasLogin = employee.PasswordHash != null };

app.MapGet("/api/auth/status", async (TransferDbContext db) => Results.Ok(new { needsAdministratorSetup = !await db.Employees.AnyAsync(e => e.Role == "Administrator") })).AllowAnonymous();
app.MapPost("/api/auth/setup", async (SetupAdminRequest request, TransferDbContext db, PasswordHasher<Employee> hasher, HttpContext context) => {
    if (string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.Email) || request.Password.Length < 12)
        return Results.ValidationProblem(new Dictionary<string, string[]> { ["account"] = ["Enter your name, a valid email, and a password of at least 12 characters."] });
    await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
    if (await db.Employees.AnyAsync(e => e.Role == "Administrator")) return Results.Conflict(new { message = "Administrator setup has already been completed. Sign in instead." });
    var email = request.Email.Trim().ToLowerInvariant();
    if (await db.Employees.AnyAsync(e => e.Email == email)) return Results.Conflict(new { message = "That email is already in use by an employee account." });
    var branch = await db.Branches.OrderBy(b => b.Id).FirstAsync();
    var admin = new Employee { Name = request.Name.Trim(), Email = email, Role = "Administrator", BranchId = branch.Id };
    admin.PasswordHash = hasher.HashPassword(admin, request.Password);
    db.Employees.Add(admin); await db.SaveChangesAsync(); await transaction.CommitAsync();
    await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, PrincipalFor(admin));
    return Results.Created("/api/auth/me", new { admin.Id, admin.Name, admin.Email, admin.Role, admin.BranchId });
}).AllowAnonymous();
app.MapPost("/api/auth/login", async (LoginRequest request, TransferDbContext db, PasswordHasher<Employee> hasher, HttpContext context) => {
    var email = request.Email.Trim().ToLowerInvariant();
    var employee = await db.Employees.SingleOrDefaultAsync(e => e.Email == email && e.IsActive);
    if (employee?.PasswordHash is null || hasher.VerifyHashedPassword(employee, employee.PasswordHash, request.Password) == PasswordVerificationResult.Failed)
        return Results.Unauthorized();
    await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, PrincipalFor(employee), new AuthenticationProperties { IsPersistent = true, ExpiresUtc = DateTimeOffset.UtcNow.AddHours(10) });
    return Results.Ok(new { employee.Id, employee.Name, employee.Email, employee.Role, employee.BranchId });
}).AllowAnonymous();
app.MapPut("/api/auth/password", async (ChangeOwnPasswordRequest request, ClaimsPrincipal user, TransferDbContext db, PasswordHasher<Employee> hasher) => {
    if (string.IsNullOrWhiteSpace(request.NewPassword) || request.NewPassword.Length < 12) return Results.ValidationProblem(new Dictionary<string, string[]> { ["newPassword"] = ["New password must be at least 12 characters."] });
    var employeeId = EmployeeIdFor(user);
    var employee = await db.Employees.SingleOrDefaultAsync(e => e.Id == employeeId && e.IsActive);
    if (employee?.PasswordHash is null || string.IsNullOrEmpty(request.CurrentPassword) || hasher.VerifyHashedPassword(employee, employee.PasswordHash, request.CurrentPassword) == PasswordVerificationResult.Failed)
        return Results.ValidationProblem(new Dictionary<string, string[]> { ["currentPassword"] = ["Current password is incorrect."] });
    employee.PasswordHash = hasher.HashPassword(employee, request.NewPassword);
    await db.SaveChangesAsync();
    return Results.NoContent();
}).RequireAuthorization();
app.MapGet("/api/auth/me", async (ClaimsPrincipal user, TransferDbContext db) => {
    var id = EmployeeIdFor(user);
    var employee = await db.Employees.Include(e => e.Branch).SingleOrDefaultAsync(e => e.Id == id && e.IsActive);
    return employee is null ? Results.Unauthorized() : Results.Ok(new { employee.Id, employee.Name, employee.Email, employee.Role, employee.BranchId, employee.CollectionCountry, branchName = employee.Branch!.Name, branchCity = employee.Branch.City });
}).RequireAuthorization();
app.MapPost("/api/auth/logout", async (HttpContext context) => { await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme); return Results.Ok(new { signedOut = true }); }).RequireAuthorization();

app.MapGet("/api/health", () => Results.Ok(new { status = "ok", service = "Shameen Investments Transfer API" })).AllowAnonymous();
app.MapGet("/api/branches", async (TransferDbContext db, ClaimsPrincipal user) => {
    var query = db.Branches.AsQueryable();
    if (!user.IsInRole("Administrator")) { var branchId = BranchIdFor(user, null); query = query.Where(b => b.Id == branchId); }
    return Results.Ok(await query.OrderBy(b => b.Id).ToListAsync());
}).RequireAuthorization();
app.MapGet("/api/day/current", async (TransferDbContext db, ClaimsPrincipal user, int? branchId) => {
    var branch = BranchIdFor(user, branchId);
    var day = DateOnly.FromDateTime(DateTime.UtcNow);
    var operatingDay = await db.OperatingDays.SingleOrDefaultAsync(d => d.Date == day && d.BranchId == branch);
    return Results.Ok(new { isOpen = operatingDay is { ClosedAtUtc: null }, operatingDay });
}).RequireAuthorization();
app.MapGet("/api/dashboard", async (TransferDbContext db, ClaimsPrincipal user, DateOnly? date, int? branchId) => {
    var branch = BranchIdFor(user, branchId);
    var day = date ?? DateOnly.FromDateTime(DateTime.UtcNow);
    var startOfDayUtc = DateTime.SpecifyKind(day.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
    var startOfNextDayUtc = DateTime.SpecifyKind(day.AddDays(1).ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
    var transactionsQuery = db.Transactions.Where(t => t.BranchId == branch && t.CreatedAtUtc >= startOfDayUtc && t.CreatedAtUtc < startOfNextDayUtc);
    var employeesQuery = db.Employees.Where(e => e.BranchId == branch && e.IsActive && e.Role != "Administrator");
    if (!IsManager(user)) { var employeeId = EmployeeIdFor(user); transactionsQuery = transactionsQuery.Where(t => t.EmployeeId == employeeId); employeesQuery = employeesQuery.Where(e => e.Id == employeeId); }
    var transactions = await transactionsQuery.ToListAsync();
    var agents = await employeesQuery.Select(e => new { e.Id, e.BranchId, e.Name, e.Email, e.Role, e.IsActive, e.FloatAssigned, e.FloatBalance }).ToListAsync();
    var operatingDay = await db.OperatingDays.SingleOrDefaultAsync(d => d.Date == day && d.BranchId == branch);
    var unassignedFloat = operatingDay is null ? 0 : Math.Max(0, operatingDay.OpeningFloat - agents.Sum(e => e.FloatAssigned));
    var sentAmount = transactions.Where(t => t.Status is "Awaiting collection" or "Collected" or "Completed").Sum(t => t.Amount);
    var collectedCount = transactions.Count(t => t.Status is "Collected" or "Completed");
    return Results.Ok(new { date = day, transferCount = transactions.Count, totalSent = sentAmount, availableFloat = agents.Sum(e => e.FloatBalance) + unassignedFloat, successRate = transactions.Count == 0 ? 100 : Math.Round(100d * collectedCount / transactions.Count), agents });
}).RequireAuthorization();
app.MapGet("/api/employees", async (TransferDbContext db, ClaimsPrincipal user, int? branchId) => {
    var query = db.Employees.Where(e => e.Role != "Administrator").AsQueryable();
    if (user.IsInRole("Administrator")) { if (branchId is { } requested) query = query.Where(e => e.BranchId == requested); }
    else if (user.IsInRole("Supervisor")) { var branchIdForSupervisor = BranchIdFor(user, null); query = query.Where(e => e.BranchId == branchIdForSupervisor); }
    else { var employeeId = EmployeeIdFor(user); query = query.Where(e => e.Id == employeeId); }
    return Results.Ok(await query.OrderBy(e => e.Name).Select(e => new { e.Id, e.BranchId, e.Name, e.Email, e.Role, e.CollectionCountry, e.IsActive, e.FloatAssigned, e.FloatBalance, hasLogin = e.PasswordHash != null }).ToListAsync());
}).RequireAuthorization();
app.MapPost("/api/employees", async (CreateEmployeeRequest request, TransferDbContext db, PasswordHasher<Employee> hasher, ClaimsPrincipal user) => {
    if (string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.Email) || !request.Email.Contains('@'))
        return Results.ValidationProblem(new Dictionary<string, string[]> { ["employee"] = ["Enter a name and valid email address."] });
    if (request.Password.Length < 12) return Results.ValidationProblem(new Dictionary<string, string[]> { ["password"] = ["Password must be at least 12 characters."] });
    var email = request.Email.Trim().ToLowerInvariant();
    if (await db.Employees.AnyAsync(e => e.Email == email)) return Results.Conflict(new { message = "An employee with this email already exists." });
    if (!CanManageBranch(user, request.BranchId)) return Results.Forbid();
    if (!await db.Branches.AnyAsync(b => b.Id == request.BranchId)) return Results.NotFound(new { message = "The selected transfer branch was not found." });
    var role = string.IsNullOrWhiteSpace(request.Role) ? "Transfer agent" : request.Role.Trim();
    if (role is not ("Transfer agent" or "Senior agent" or "Supervisor")) return Results.ValidationProblem(new Dictionary<string, string[]> { ["role"] = ["Choose Transfer agent, Senior agent, or Supervisor."] });
    if (role == "Supervisor" && !user.IsInRole("Administrator")) return Results.Forbid();
    var employee = new Employee { Name = request.Name.Trim(), Email = email, Role = role, BranchId = request.BranchId };
    employee.PasswordHash = hasher.HashPassword(employee, request.Password);
    db.Employees.Add(employee); await db.SaveChangesAsync();
    return Results.Created($"/api/employees/{employee.Id}", EmployeeView(employee));
}).RequireAuthorization();
app.MapPut("/api/employees/{id:int}/credentials", async (int id, SetEmployeeCredentialsRequest request, TransferDbContext db, PasswordHasher<Employee> hasher, ClaimsPrincipal user) => {
    if (request.Password.Length < 12) return Results.ValidationProblem(new Dictionary<string, string[]> { ["password"] = ["Password must be at least 12 characters."] });
    var employee = await db.Employees.FindAsync(id);
    if (employee is null) return Results.NotFound(new { message = "Employee not found." });
    if (employee.Role == "Administrator" || !CanManageBranch(user, employee.BranchId) || (user.IsInRole("Supervisor") && employee.Role == "Supervisor")) return Results.Forbid();
    employee.PasswordHash = hasher.HashPassword(employee, request.Password); await db.SaveChangesAsync();
    return Results.NoContent();
}).RequireAuthorization();
app.MapPut("/api/employees/{id:int}/collection-country", async (int id, AssignCollectionCountryRequest request, TransferDbContext db, ClaimsPrincipal user) => {
    var employee = await db.Employees.SingleOrDefaultAsync(e => e.Id == id);
    if (employee is null) return Results.NotFound(new { message = "Employee not found." });
    if (employee.Role == "Administrator" || !CanManageBranch(user, employee.BranchId) || (user.IsInRole("Supervisor") && employee.Role == "Supervisor")) return Results.Forbid();
    if (string.IsNullOrWhiteSpace(request.Country)) employee.CollectionCountry = null;
    else {
        var country = supportedCollectionCountries.FirstOrDefault(c => string.Equals(c, request.Country.Trim(), StringComparison.OrdinalIgnoreCase));
        if (country is null) return Results.ValidationProblem(new Dictionary<string, string[]> { ["country"] = ["Select a supported payout country."] });
        employee.CollectionCountry = country;
    }
    await db.SaveChangesAsync();
    return Results.Ok(new { employee.Id, employee.CollectionCountry });
}).RequireAuthorization();
app.MapPut("/api/transfers/{id:long}/destination-city", async (long id, UpdateDestinationCityRequest request, TransferDbContext db, ClaimsPrincipal user) => {
    var transfer = await db.Transactions.SingleOrDefaultAsync(t => t.Id == id);
    if (transfer is null) return Results.NotFound(new { message = "Transfer not found." });
    if (!user.IsInRole("Administrator") && !(user.IsInRole("Supervisor") && CanManageBranch(user, transfer.BranchId)) && transfer.EmployeeId != EmployeeIdFor(user)) return Results.Forbid();
    if (transfer.Status != "Awaiting collection") return Results.Conflict(new { message = "Destination city can only be changed while the transfer is awaiting collection." });
    if (string.IsNullOrWhiteSpace(request.DestinationCity)) return Results.ValidationProblem(new Dictionary<string, string[]> { ["destinationCity"] = ["Enter or select a destination city."] });
    var destinationCity = request.DestinationCity.Trim();
    if (destinationCity.Length > 100) return Results.ValidationProblem(new Dictionary<string, string[]> { ["destinationCity"] = ["Destination city must be 100 characters or fewer."] });
    var configuredCities = await db.DestinationCities.Where(c => c.Country == transfer.DestinationCountry).Select(c => c.City).ToListAsync();
    if (configuredCities.Count > 0) {
        var matchedCity = configuredCities.FirstOrDefault(city => string.Equals(city, destinationCity, StringComparison.OrdinalIgnoreCase));
        if (matchedCity is null) return Results.ValidationProblem(new Dictionary<string, string[]> { ["destinationCity"] = ["Select a city listed for the destination country."] });
        destinationCity = matchedCity;
    }
    transfer.DestinationCity = destinationCity;
    await db.SaveChangesAsync();
    return Results.Ok(new { transfer.Id, transfer.DestinationCountry, transfer.DestinationCity, transfer.Status });
}).RequireAuthorization();
app.MapGet("/api/destination-cities", async (TransferDbContext db) => Results.Ok(await db.DestinationCities.AsNoTracking().OrderBy(c => c.Country).ThenBy(c => c.City).Select(c => new { c.Country, c.City }).ToListAsync())).RequireAuthorization();
app.MapGet("/api/transfers", async (TransferDbContext db, ClaimsPrincipal user, DateOnly? date, int? branchId) => {
    var query = db.Transactions.AsNoTracking().AsQueryable();
    var branch = BranchIdFor(user, branchId); query = query.Where(t => t.BranchId == branch);
    if (!IsManager(user)) { var employeeId = EmployeeIdFor(user); query = query.Where(t => t.EmployeeId == employeeId); }
    if (date is { } day) {
        var startOfDayUtc = DateTime.SpecifyKind(day.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
        var startOfNextDayUtc = DateTime.SpecifyKind(day.AddDays(1).ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
        query = query.Where(t => t.CreatedAtUtc >= startOfDayUtc && t.CreatedAtUtc < startOfNextDayUtc);
    }
    return Results.Ok(await query.OrderByDescending(t => t.CreatedAtUtc).Take(500).Select(t => new { t.Id, t.Reference, t.CustomerName, t.DestinationCountry, t.DestinationCity, t.Amount, t.PayoutMethod, t.Status, t.CreatedAtUtc, t.EmployeeId, t.BranchId, t.CollectedAtUtc, t.CollectionReference, t.CollectionConfirmedByEmployeeId, sendingAgent = t.Employee!.Name, sourceBranch = t.Branch!.Name }).ToListAsync());
}).RequireAuthorization();
app.MapPost("/api/day/open", async (OpenDayRequest request, TransferDbContext db, ClaimsPrincipal user) => {
    if (request.OpeningFloat <= 0) return Results.ValidationProblem(new Dictionary<string, string[]> { ["openingFloat"] = ["Opening float must be greater than zero."] });
    var branchId = BranchIdFor(user, request.BranchId);
    if (!CanManageBranch(user, branchId)) return Results.Forbid();
    if (!await db.Branches.AnyAsync(b => b.Id == branchId)) return Results.NotFound(new { message = "The selected transfer branch was not found." });
    var day = DateOnly.FromDateTime(DateTime.UtcNow);
    if (await db.OperatingDays.AnyAsync(d => d.Date == day && d.BranchId == branchId && d.ClosedAtUtc == null)) return Results.Conflict(new { message = "An operating day is already open for this branch." });
    var record = new OperatingDay { BranchId = branchId, Date = day, OpeningFloat = request.OpeningFloat, OpenedAtUtc = DateTime.UtcNow, OpenedByEmployeeId = EmployeeIdFor(user) };
    var employees = await db.Employees.Where(e => e.BranchId == branchId && e.IsActive && e.Role != "Administrator").OrderBy(e => e.Id).ToListAsync();
    if (employees.Count == 0) return Results.Conflict(new { message = "Add an active employee before opening the day." });
    foreach (var employee in employees) { employee.FloatAssigned = 0; employee.FloatBalance = 0; employee.FloatEnteredDayId = null; }
    db.OperatingDays.Add(record); await db.SaveChangesAsync(); return Results.Created($"/api/day/{record.Id}", record);
}).RequireAuthorization();
app.MapPost("/api/float/my", async (SetFloatRequest request, TransferDbContext db, ClaimsPrincipal user) => {
    if (user.IsInRole("Administrator")) return Results.Forbid();
    if (request.Amount <= 0 || decimal.Round(request.Amount, 2) != request.Amount) return Results.ValidationProblem(new Dictionary<string, string[]> { ["amount"] = ["Enter a float greater than zero with no more than two decimal places."] });
    await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
    var employeeId = EmployeeIdFor(user); var branchId = BranchIdFor(user, null); var today = DateOnly.FromDateTime(DateTime.UtcNow);
    var operatingDay = await db.OperatingDays.SingleOrDefaultAsync(d => d.Date == today && d.BranchId == branchId && d.ClosedAtUtc == null);
    if (operatingDay is null) return Results.Conflict(new { message = "The administrator must open this branch's day before you can enter float." });
    var employee = await db.Employees.SingleOrDefaultAsync(e => e.Id == employeeId && e.BranchId == branchId && e.IsActive && e.Role != "Administrator");
    if (employee is null) return Results.NotFound(new { message = "Your active employee account was not found." });
    if (employee.FloatEnteredDayId == operatingDay.Id) return Results.Conflict(new { message = "Your float is already recorded for today. Only an administrator can change it." });
    var assignedElsewhere = await db.Employees.Where(e => e.BranchId == branchId && e.IsActive && e.Role != "Administrator" && e.Id != employeeId).SumAsync(e => e.FloatAssigned);
    var remaining = operatingDay.OpeningFloat - assignedElsewhere;
    if (request.Amount > remaining) return Results.Conflict(new { message = $"Float exceeds the unassigned branch reserve of {Math.Max(0, remaining):0.00}. Ask an administrator to increase the opening float." });
    employee.FloatAssigned = employee.FloatBalance = request.Amount; employee.FloatEnteredDayId = operatingDay.Id;
    db.FloatAdjustments.Add(new FloatAdjustment { OperatingDayId = operatingDay.Id, EmployeeId = employee.Id, ActorEmployeeId = employee.Id, PreviousAmount = 0, NewAmount = request.Amount, Reason = "Employee opening float entry", CreatedAtUtc = DateTime.UtcNow });
    await db.SaveChangesAsync(); await tx.CommitAsync();
    return Results.Ok(new { employee.Id, employee.FloatAssigned, employee.FloatBalance, locked = true });
}).RequireAuthorization();
app.MapPut("/api/employees/{id:int}/float", async (int id, SetFloatRequest request, TransferDbContext db, ClaimsPrincipal user) => {
    if (request.Amount < 0 || decimal.Round(request.Amount, 2) != request.Amount) return Results.ValidationProblem(new Dictionary<string, string[]> { ["amount"] = ["Enter zero or a positive float with no more than two decimal places."] });
    if (string.IsNullOrWhiteSpace(request.Reason)) return Results.ValidationProblem(new Dictionary<string, string[]> { ["reason"] = ["Enter a reason for the float correction."] });
    await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
    var employee = await db.Employees.SingleOrDefaultAsync(e => e.Id == id && e.Role != "Administrator");
    if (employee is null) return Results.NotFound(new { message = "Employee not found." });
    if (employee.Role == "Administrator" || !CanManageBranch(user, employee.BranchId)) return Results.Forbid();
    var today = DateOnly.FromDateTime(DateTime.UtcNow);
    var operatingDay = await db.OperatingDays.SingleOrDefaultAsync(d => d.Date == today && d.BranchId == employee.BranchId && d.ClosedAtUtc == null);
    if (operatingDay is null) return Results.Conflict(new { message = "Open the operating day before adjusting employee float." });
    var assignedToOthers = await db.Employees.Where(e => e.BranchId == employee.BranchId && e.IsActive && e.Role != "Administrator" && e.Id != id).SumAsync(e => e.FloatAssigned);
    var remainingForEmployee = operatingDay.OpeningFloat - assignedToOthers;
    if (request.Amount > remainingForEmployee) return Results.Conflict(new { message = $"Float exceeds the branch reserve available for this employee ({Math.Max(0, remainingForEmployee):0.00})." });
    var updatedBalance = employee.FloatBalance + request.Amount - employee.FloatAssigned;
    if (updatedBalance < 0) return Results.Conflict(new { message = "This correction would put the employee float below zero because transfers have already been paid. Increase the float or reconcile those transfers first." });
    var previousAmount = employee.FloatAssigned;
    employee.FloatAssigned = request.Amount; employee.FloatBalance = updatedBalance; employee.FloatEnteredDayId = operatingDay.Id;
    db.FloatAdjustments.Add(new FloatAdjustment { OperatingDayId = operatingDay.Id, EmployeeId = employee.Id, ActorEmployeeId = EmployeeIdFor(user), PreviousAmount = previousAmount, NewAmount = request.Amount, Reason = request.Reason.Trim(), CreatedAtUtc = DateTime.UtcNow });
    await db.SaveChangesAsync(); await tx.CommitAsync();
    return Results.Ok(new { employee.Id, employee.FloatAssigned, employee.FloatBalance });
}).RequireAuthorization();
app.MapPost("/api/transfers", async (CreateTransferRequest request, TransferDbContext db, ClaimsPrincipal user) => {
    if (request.Amount <= 0 || string.IsNullOrWhiteSpace(request.CustomerName)) return Results.ValidationProblem(new Dictionary<string, string[]> { ["request"] = ["Customer and a positive amount are required."] });
    if (string.IsNullOrWhiteSpace(request.DestinationCountry)) return Results.ValidationProblem(new Dictionary<string, string[]> { ["destinationCountry"] = ["Select a destination country."] });
    if (string.IsNullOrWhiteSpace(request.DestinationCity)) return Results.ValidationProblem(new Dictionary<string, string[]> { ["destinationCity"] = ["Enter or select a destination city."] });
    var destinationCountry = request.DestinationCountry.Trim();
    var destinationCity = request.DestinationCity.Trim();
    if (destinationCity.Length > 100) return Results.ValidationProblem(new Dictionary<string, string[]> { ["destinationCity"] = ["Destination city must be 100 characters or fewer."] });
    var configuredCities = await db.DestinationCities.Where(c => c.Country == destinationCountry).Select(c => c.City).ToListAsync();
    if (configuredCities.Count > 0) {
        var matchedCity = configuredCities.FirstOrDefault(city => string.Equals(city, destinationCity, StringComparison.OrdinalIgnoreCase));
        if (matchedCity is null) return Results.ValidationProblem(new Dictionary<string, string[]> { ["destinationCity"] = ["Select a city listed for the destination country."] });
        destinationCity = matchedCity;
    }
    await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
    var day = DateOnly.FromDateTime(DateTime.UtcNow);
    var branchId = BranchIdFor(user, request.BranchId);
    if (!await db.OperatingDays.AnyAsync(d => d.Date == day && d.BranchId == branchId && d.ClosedAtUtc == null)) return Results.Conflict(new { message = "Open the operating day for this branch before recording transfers." });
    var employeeId = IsManager(user) ? request.EmployeeId : EmployeeIdFor(user);
    if (!CanManageBranch(user, branchId)) return Results.Forbid();
    var employee = await db.Employees.SingleOrDefaultAsync(e => e.Id == employeeId && e.BranchId == branchId && e.IsActive);
    if (employee is null) return Results.NotFound(new { message = "Active employee was not found." });
    if (employee.FloatBalance < request.Amount) return Results.Conflict(new { message = "Insufficient employee float.", availableFloat = employee.FloatBalance });
    employee.FloatBalance -= request.Amount;
    var transfer = new Transfer { Reference = $"SI-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid():N}"[..20].ToUpperInvariant(), BranchId = branchId, EmployeeId = employee.Id, CustomerName = request.CustomerName.Trim(), DestinationCountry = destinationCountry, DestinationCity = destinationCity, Amount = request.Amount, PayoutMethod = request.PayoutMethod, Status = "Awaiting collection", CreatedAtUtc = DateTime.UtcNow };
    db.Transactions.Add(transfer); await db.SaveChangesAsync(); await tx.CommitAsync();
    return Results.Created($"/api/transfers/{transfer.Id}", new { transfer.Id, transfer.Reference, transfer.CustomerName, transfer.DestinationCountry, transfer.DestinationCity, transfer.Amount, transfer.PayoutMethod, transfer.Status, transfer.CreatedAtUtc, transfer.EmployeeId, transfer.CollectedAtUtc, transfer.CollectionReference });
}).RequireAuthorization();
app.MapGet("/api/collections", async (TransferDbContext db, ClaimsPrincipal user, DateOnly? date) => {
    var employeeId = EmployeeIdFor(user);
    var employee = await db.Employees.AsNoTracking().SingleOrDefaultAsync(e => e.Id == employeeId && e.IsActive);
    if (employee?.CollectionCountry is null) return Results.Forbid();
    var receivingCity = await db.Branches.AsNoTracking().Where(b => b.Id == employee.BranchId).Select(b => b.City).SingleOrDefaultAsync();
    if (string.IsNullOrWhiteSpace(receivingCity)) return Results.Forbid();
    var day = date ?? DateOnly.FromDateTime(DateTime.UtcNow);
    var queue = from transfer in db.Transactions.AsNoTracking()
                join branch in db.Branches.AsNoTracking() on transfer.BranchId equals branch.Id
                join sendingAgent in db.Employees.AsNoTracking() on transfer.EmployeeId equals sendingAgent.Id
                where transfer.DestinationCountry == employee.CollectionCountry
                    && transfer.DestinationCity == receivingCity
                    && transfer.CreatedAtUtc >= DateTime.SpecifyKind(day.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc)
                    && transfer.CreatedAtUtc < DateTime.SpecifyKind(day.AddDays(1).ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc)
                orderby transfer.CreatedAtUtc descending
                select new { transfer.Id, transfer.EmployeeId, transfer.BranchId, sourceBranch = branch.Name, sendingAgent = sendingAgent.Name, transfer.Reference, transfer.CustomerName, transfer.DestinationCountry, transfer.DestinationCity, transfer.Amount, transfer.PayoutMethod, transfer.Status, transfer.CreatedAtUtc, transfer.CollectedAtUtc, transfer.CollectionReference };
    return Results.Ok(new { country = employee.CollectionCountry, transfers = await queue.Take(500).ToListAsync() });
}).RequireAuthorization();
app.MapPost("/api/transfers/{id:long}/collection", async (long id, ConfirmCollectionRequest request, TransferDbContext db, ClaimsPrincipal user) => {
    await using var confirmationTransaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
    var transfer = await db.Transactions.SingleOrDefaultAsync(t => t.Id == id);
    if (transfer is null) return Results.NotFound(new { message = "Transfer not found." });
    var receiverEmployeeId = EmployeeIdFor(user);
    var receiver = await db.Employees.Include(e => e.Branch).SingleOrDefaultAsync(e => e.Id == receiverEmployeeId && e.IsActive);
    if (receiver?.CollectionCountry is null || transfer.EmployeeId == receiver.Id || !string.Equals(transfer.DestinationCountry, receiver.CollectionCountry, StringComparison.OrdinalIgnoreCase) || !string.Equals(transfer.DestinationCity, receiver.Branch?.City, StringComparison.OrdinalIgnoreCase)) return Results.Forbid();
    if (transfer.Status != "Awaiting collection") return Results.Conflict(new { message = "Only transfers awaiting collection can be confirmed." });
    if (request.CollectionReference?.Trim().Length > 120) return Results.ValidationProblem(new Dictionary<string, string[]> { ["collectionReference"] = ["Collection reference must be 120 characters or fewer."] });
    transfer.Status = "Collected"; transfer.CollectedAtUtc = DateTime.UtcNow; transfer.CollectionConfirmedByEmployeeId = receiverEmployeeId; transfer.CollectionReference = string.IsNullOrWhiteSpace(request.CollectionReference) ? null : request.CollectionReference.Trim();
    await db.SaveChangesAsync(); await confirmationTransaction.CommitAsync();
    return Results.Ok(new { transfer.Id, transfer.Reference, transfer.Status, transfer.CollectedAtUtc, transfer.CollectionReference, transfer.CollectionConfirmedByEmployeeId });
}).RequireAuthorization();
app.MapPost("/api/day/close", async (CloseDayRequest request, TransferDbContext db, ClaimsPrincipal user) => {
    var branchId = BranchIdFor(user, request.BranchId);
    if (!CanManageBranch(user, branchId)) return Results.Forbid();
    var day = DateOnly.FromDateTime(DateTime.UtcNow);
    var operatingDay = await db.OperatingDays.SingleOrDefaultAsync(d => d.Date == day && d.BranchId == branchId && d.ClosedAtUtc == null);
    if (operatingDay is null) return Results.NotFound(new { message = "No open operating day was found." });
    var branchEmployees = await db.Employees.Where(e => e.BranchId == branchId && e.IsActive && e.Role != "Administrator").ToListAsync();
    var employeeBalances = branchEmployees.Sum(e => e.FloatBalance);
    var unassignedFloat = Math.Max(0, operatingDay.OpeningFloat - branchEmployees.Sum(e => e.FloatAssigned));
    var startOfDayUtc = DateTime.SpecifyKind(day.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
    var startOfNextDayUtc = DateTime.SpecifyKind(day.AddDays(1).ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
    var dayTransfers = await db.Transactions.Where(t => t.BranchId == branchId && t.CreatedAtUtc >= startOfDayUtc && t.CreatedAtUtc < startOfNextDayUtc).ToListAsync();
    operatingDay.ClosedAtUtc = DateTime.UtcNow; operatingDay.ClosingFloat = employeeBalances + unassignedFloat; operatingDay.ClosedByEmployeeId = EmployeeIdFor(user);
    await db.SaveChangesAsync();
    return Results.Ok(new { date = day, openingFloat = operatingDay.OpeningFloat, transfers = dayTransfers.Count, sent = dayTransfers.Where(t => t.Status is "Awaiting collection" or "Collected" or "Completed").Sum(t => t.Amount), collected = dayTransfers.Where(t => t.Status is "Collected" or "Completed").Sum(t => t.Amount), closingFloat = operatingDay.ClosingFloat, closedAtUtc = operatingDay.ClosedAtUtc });
}).RequireAuthorization();
app.Run();


