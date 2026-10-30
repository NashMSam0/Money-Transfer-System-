using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Shameen.Api.Data;

namespace Shameen.Api;

public sealed class TabSessionAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    private readonly TransferDbContext _db;

    public TabSessionAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        System.Text.Encodings.Web.UrlEncoder encoder,
        TransferDbContext db) : base(options, logger, encoder) => _db = db;

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var authorization = Request.Headers.Authorization.ToString();
        if (!authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return AuthenticateResult.NoResult();

        var token = authorization[7..].Trim();
        if (token.Length < 32) return AuthenticateResult.Fail("Invalid session token.");
        var tokenHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
        var now = DateTime.UtcNow;
        var session = await _db.AuthSessions.AsNoTracking()
            .Where(s => s.TokenHash == tokenHash && s.RevokedAtUtc == null && s.ExpiresAtUtc > now)
            .Select(s => new { s.Id, s.EmployeeId })
            .SingleOrDefaultAsync(Context.RequestAborted);
        if (session is null) return AuthenticateResult.Fail("Session expired or signed out.");

        var employee = await _db.Employees.AsNoTracking().Include(e => e.Branch)
            .SingleOrDefaultAsync(e => e.Id == session.EmployeeId && e.IsActive, Context.RequestAborted);
        if (employee is null) return AuthenticateResult.Fail("Account is inactive.");

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, employee.Id.ToString()),
            new(ClaimTypes.Name, employee.Name),
            new(ClaimTypes.Email, employee.Email),
            new(ClaimTypes.Role, employee.Role),
            new("branch_id", employee.BranchId.ToString()),
            new("session_id", session.Id.ToString())
        };
        if (!string.IsNullOrWhiteSpace(employee.CollectionCountry))
        {
            claims.Add(new Claim(ClaimTypes.Role, "CollectionAgent"));
            claims.Add(new Claim("collection_country", employee.CollectionCountry));
        }
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme.Name));
        return AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name));
    }
}
