namespace SafeTunnels.Account.Models;
public sealed class Applicant
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string TrackingCode { get; set; } = "";
    public Guid IdempotencyKey { get; set; }
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string? Mobile { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public List<BankAccount> BankAccounts { get; set; } = [];
}
