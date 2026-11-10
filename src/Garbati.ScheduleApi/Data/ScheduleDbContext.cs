using Garbati.ScheduleApi.Entities;
using Microsoft.EntityFrameworkCore;

namespace Garbati.ScheduleApi.Data;

public class ScheduleDbContext(DbContextOptions<ScheduleDbContext> options) : DbContext(options)
{
    public DbSet<StandingInstruction> StandingInstructions => Set<StandingInstruction>();
    public DbSet<StandingInstructionRun> StandingInstructionRuns => Set<StandingInstructionRun>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<StandingInstruction>(entity =>
        {
            entity.HasKey(s => s.Id);
            entity.HasIndex(s => s.StandingInstructionId).IsUnique();
            entity.HasIndex(s => s.Name).IsUnique();
            entity.HasIndex(s => s.CustomerId);

            // Idempotency for creation, scoped per calling client - the exact same shape as LedgerApi's
            // ix_accounts_initiated_by_idempotency_key and Garbati.IdentityApi's
            // ix_customers_initiated_by_idempotency_key (confirmed against both repos' real
            // AccountService/CustomerService before copying the shape, not guessed).
            entity.HasIndex(s => new { s.InitiatedBy, s.IdempotencyKey }).IsUnique().AreNullsDistinct(false);

            entity.Property(s => s.FromAccountType).HasConversion<string>();
            entity.Property(s => s.ToAccountType).HasConversion<string>();
            entity.Property(s => s.TransferType).HasConversion<string>();
            entity.Property(s => s.InstructionType).HasConversion<string>();
            entity.Property(s => s.Priority).HasConversion<string>();
            entity.Property(s => s.RecurrenceType).HasConversion<string>();
            entity.Property(s => s.RecurrenceFrequency).HasConversion<string>();
            entity.Property(s => s.Status).HasConversion<string>();

            entity.ToTable(t => t.HasCheckConstraint("ck_standing_instructions_from_account_type",
                "\"from_account_type\" IN ('Savings', 'Loan')"));
            entity.ToTable(t => t.HasCheckConstraint("ck_standing_instructions_to_account_type",
                "\"to_account_type\" IN ('Savings', 'Loan')"));
            entity.ToTable(t => t.HasCheckConstraint("ck_standing_instructions_transfer_type",
                "\"transfer_type\" IN ('AccountTransfer', 'LoanRepayment', 'ChargePayment', 'InterestTransfer', 'LoanDownPayment')"));
            entity.ToTable(t => t.HasCheckConstraint("ck_standing_instructions_instruction_type",
                "\"instruction_type\" IN ('Fixed', 'Dues')"));
            entity.ToTable(t => t.HasCheckConstraint("ck_standing_instructions_priority",
                "\"priority\" IN ('Urgent', 'High', 'Medium', 'Low')"));
            entity.ToTable(t => t.HasCheckConstraint("ck_standing_instructions_recurrence_type",
                "\"recurrence_type\" IN ('Periodic', 'AsPerDues')"));
            entity.ToTable(t => t.HasCheckConstraint("ck_standing_instructions_recurrence_frequency",
                "\"recurrence_frequency\" IN ('Days', 'Weeks', 'Months', 'Years')"));
            entity.ToTable(t => t.HasCheckConstraint("ck_standing_instructions_status",
                "\"status\" IN ('Active', 'Disabled', 'Deleted')"));
        });

        modelBuilder.Entity<StandingInstructionRun>(entity =>
        {
            entity.HasKey(r => r.Id);
            entity.HasIndex(r => r.StandingInstructionId);
            // The run history's own idempotency guard: a repeated trigger for the same instruction on the
            // same calendar date must never create a second row (DESIGN.md 3.1's "defence in depth").
            entity.HasIndex(r => r.IdempotencyKey).IsUnique();

            entity.Property(r => r.Status).HasConversion<string>();

            entity.HasOne<StandingInstruction>()
                .WithMany()
                .HasForeignKey(r => r.StandingInstructionId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.ToTable(t => t.HasCheckConstraint("ck_standing_instruction_runs_status",
                "\"status\" IN ('Success', 'Partial', 'Failed', 'Skipped')"));
        });
    }
}
