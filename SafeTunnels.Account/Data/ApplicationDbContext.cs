using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using SafeTunnels.Account.Models;
namespace SafeTunnels.Account.Data;
public sealed class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : IdentityDbContext<IdentityUser>(options)
{
    public DbSet<Applicant> Applicants => Set<Applicant>();
    public DbSet<BankAccount> BankAccounts => Set<BankAccount>();
    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);
        b.Entity<Applicant>(e => {
            e.HasIndex(x => x.TrackingCode).IsUnique();
            e.HasIndex(x => x.IdempotencyKey).IsUnique();
            e.HasIndex(x => x.CreatedAt);
            e.HasIndex(x => new { x.LastName, x.FirstName });
            e.HasIndex(x => x.Mobile);
            e.Property(x => x.TrackingCode).HasMaxLength(32);
            e.Property(x => x.FirstName).HasMaxLength(100);
            e.Property(x => x.LastName).HasMaxLength(100);
            e.Property(x => x.Mobile).HasMaxLength(11);
            e.HasMany(x => x.BankAccounts).WithOne(x => x.Applicant).HasForeignKey(x => x.ApplicantId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<BankAccount>().Property(x => x.BankName).HasMaxLength(100);
    }
}
