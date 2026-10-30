namespace Shameen.Api.Models;

public sealed class Branch { public int Id { get; set; } public string Name { get; set; } = ""; public string City { get; set; } = ""; public string Country { get; set; } = ""; }
public sealed class DestinationCity { public int Id { get; set; } public string Country { get; set; } = ""; public string City { get; set; } = ""; }
public sealed class Employee { public int Id { get; set; } public int BranchId { get; set; } = 1; public Branch? Branch { get; set; } public string Name { get; set; } = ""; public string Email { get; set; } = ""; public string Role { get; set; } = "Employee"; public string? PasswordHash { get; set; } public string? CollectionCountry { get; set; } public bool IsActive { get; set; } = true; public decimal FloatAssigned { get; set; } public decimal FloatBalance { get; set; } public long? FloatEnteredDayId { get; set; } public ICollection<Transfer> Transfers { get; set; } = []; }
public sealed class Transfer { public long Id { get; set; } public string Reference { get; set; } = ""; public int BranchId { get; set; } public Branch? Branch { get; set; } public int EmployeeId { get; set; } public Employee? Employee { get; set; } public string CustomerName { get; set; } = ""; public string DestinationCountry { get; set; } = ""; public string? DestinationCity { get; set; } public decimal Amount { get; set; } public string PayoutMethod { get; set; } = ""; public string Status { get; set; } = "Awaiting collection"; public DateTime CreatedAtUtc { get; set; } public DateTime? CollectedAtUtc { get; set; } public int? CollectionConfirmedByEmployeeId { get; set; } public string? CollectionReference { get; set; } }
public sealed class OperatingDay { public long Id { get; set; } public int BranchId { get; set; } public Branch? Branch { get; set; } public DateOnly Date { get; set; } public decimal OpeningFloat { get; set; } public DateTime OpenedAtUtc { get; set; } public int? OpenedByEmployeeId { get; set; } public decimal? ClosingFloat { get; set; } public DateTime? ClosedAtUtc { get; set; } public int? ClosedByEmployeeId { get; set; } }
public sealed class FloatAdjustment { public long Id { get; set; } public long OperatingDayId { get; set; } public int EmployeeId { get; set; } public int ActorEmployeeId { get; set; } public decimal PreviousAmount { get; set; } public decimal NewAmount { get; set; } public string Reason { get; set; } = ""; public DateTime CreatedAtUtc { get; set; } }
public sealed record OpenDayRequest(decimal OpeningFloat, int? EmployeeId, int BranchId);
public sealed record CreateTransferRequest(int EmployeeId, string CustomerName, string DestinationCountry, string DestinationCity, decimal Amount, string PayoutMethod, int BranchId);
public sealed record CloseDayRequest(int? EmployeeId, int BranchId);
public sealed record CreateEmployeeRequest(string Name, string Email, string Role, int BranchId, string Password);
public sealed record SetEmployeeCredentialsRequest(string Password);
public sealed record ChangeOwnPasswordRequest(string CurrentPassword, string NewPassword);
public sealed record SetFloatRequest(decimal Amount, string? Reason);
public sealed record ConfirmCollectionRequest(string? CollectionReference);
public sealed record UpdateDestinationCityRequest(string DestinationCity);
public sealed record AssignCollectionCountryRequest(string? Country);
public sealed record UpdateEmployeeLocationRequest(int BranchId);
public sealed record LoginRequest(string Email, string Password);
public sealed record SetupAdminRequest(string Name, string Email, string Password);
