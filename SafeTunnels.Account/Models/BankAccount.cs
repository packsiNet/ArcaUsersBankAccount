namespace SafeTunnels.Account.Models;
public sealed class BankAccount
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ApplicantId { get; set; }
    public Applicant Applicant { get; set; } = null!;
    public string BankName { get; set; } = "";
    public string IBAN { get; set; } = "";
    public string? IbanHash { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
