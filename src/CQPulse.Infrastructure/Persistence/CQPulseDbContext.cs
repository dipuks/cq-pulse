using CQPulse.Domain.Entities;
using CQPulse.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace CQPulse.Infrastructure.Persistence;

public sealed class CQPulseDbContext(DbContextOptions<CQPulseDbContext> options)
    : IdentityDbContext<ApplicationUser>(options)
{
    public const string RowVersionProperty = "RowVersion";
    public const string ActiveRunIndexName = "UX_TestRuns_ActiveTarget";

    public DbSet<Project> Projects => Set<Project>();
    public DbSet<TestSuite> TestSuites => Set<TestSuite>();
    public DbSet<TestCase> TestCases => Set<TestCase>();
    public DbSet<TestRun> TestRuns => Set<TestRun>();
    public DbSet<TestCaseResult> TestCaseResults => Set<TestCaseResult>();
    public DbSet<ExecutionLog> ExecutionLogs => Set<ExecutionLog>();
    public DbSet<FailureAnalysis> FailureAnalyses => Set<FailureAnalysis>();
    public DbSet<DecisionEvaluation> DecisionEvaluations => Set<DecisionEvaluation>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<Project>(entity =>
        {
            entity.ToTable("Projects");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Description).HasMaxLength(2_000);
            entity.HasIndex(x => new { x.Status, x.Name });
        });

        builder.Entity<TestSuite>(entity =>
        {
            entity.ToTable("TestSuites");
            entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Description).HasMaxLength(2_000);
            entity.HasIndex(x => new { x.ProjectId, x.Name }).IsUnique();
            entity.HasOne(x => x.Project).WithMany(x => x.TestSuites).HasForeignKey(x => x.ProjectId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<TestCase>(entity =>
        {
            entity.ToTable("TestCases");
            entity.Property(x => x.Name).HasMaxLength(300).IsRequired();
            entity.Property(x => x.Description).HasMaxLength(4_000);
            entity.HasIndex(x => new { x.TestSuiteId, x.Enabled, x.Name });
            entity.HasOne(x => x.TestSuite).WithMany(x => x.TestCases).HasForeignKey(x => x.TestSuiteId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<TestRun>(entity =>
        {
            entity.ToTable("TestRuns");
            entity.Property(x => x.RunLabel).HasMaxLength(200).IsRequired();
            entity.Property(x => x.ConfigurationSnapshot).HasColumnType("nvarchar(max)").IsRequired();
            entity.Property(x => x.ErrorSummary).HasMaxLength(4_000);
            entity.Property(x => x.CancellationReason).HasMaxLength(1_000);
            // Optimistic concurrency: the worker and cancel requests update the same run concurrently.
            entity.Property<byte[]>(RowVersionProperty).IsRowVersion();
            entity.HasIndex(x => new { x.ProjectId, x.Status, x.RequestedAtUtc });
            // At most one queued/running run per target (a null suite means "all suites" and is treated as one target).
            entity.HasIndex(x => new { x.ProjectId, x.TestSuiteId })
                .IsUnique()
                .HasFilter("[Status] IN (0, 1)")
                .HasDatabaseName(ActiveRunIndexName);
            entity.HasOne(x => x.Project).WithMany(x => x.TestRuns).HasForeignKey(x => x.ProjectId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.TestSuite).WithMany(x => x.TestRuns).HasForeignKey(x => x.TestSuiteId)
                .OnDelete(DeleteBehavior.Restrict);
            // Rerun link: the original run cannot be deleted while reruns reference it.
            entity.HasOne<TestRun>().WithMany().HasForeignKey(x => x.RerunOfRunId)
                .OnDelete(DeleteBehavior.NoAction);
        });

        builder.Entity<TestCaseResult>(entity =>
        {
            entity.ToTable("TestCaseResults");
            entity.Property(x => x.TestCaseNameSnapshot).HasMaxLength(300).IsRequired();
            entity.Property(x => x.ErrorMessage).HasMaxLength(4_000);
            entity.HasIndex(x => new { x.TestRunId, x.Outcome });
            entity.HasOne(x => x.TestRun).WithMany(x => x.Results).HasForeignKey(x => x.TestRunId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.TestCase).WithMany(x => x.Results).HasForeignKey(x => x.TestCaseId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<FailureAnalysis>(entity =>
        {
            entity.ToTable("FailureAnalyses");
            entity.Property(x => x.Provider).HasMaxLength(64).IsRequired();
            entity.Property(x => x.InputDigest).HasMaxLength(64).IsFixedLength().IsRequired();
            entity.Property(x => x.CorrelationId).HasMaxLength(64).IsRequired();
            entity.Property(x => x.RequestedByUserId).HasMaxLength(450);
            entity.Property(x => x.LastError).HasMaxLength(2_000);
            entity.Property(x => x.ExternalAnalysisId).HasMaxLength(64);
            entity.Property(x => x.Severity).HasMaxLength(16);
            entity.Property(x => x.Summary).HasMaxLength(2_000);
            entity.Property(x => x.TopCategory).HasMaxLength(100);
            entity.Property(x => x.FindingsJson).HasColumnType("nvarchar(max)");
            entity.Property(x => x.RecommendationsJson).HasColumnType("nvarchar(max)");
            entity.Property(x => x.SignatureSeveritiesJson).HasColumnType("nvarchar(max)");
            entity.HasIndex(x => new { x.TestRunId, x.RequestedAtUtc });
            entity.HasIndex(x => x.Status);
            // Analyses are derived data: deleting a run removes its analyses.
            entity.HasOne(x => x.TestRun).WithMany().HasForeignKey(x => x.TestRunId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<DecisionEvaluation>(entity =>
        {
            entity.ToTable("DecisionEvaluations");
            entity.Property(x => x.Provider).HasMaxLength(64).IsRequired();
            entity.Property(x => x.CorrelationId).HasMaxLength(64).IsRequired();
            entity.Property(x => x.RequestedByUserId).HasMaxLength(450);
            entity.Property(x => x.Question).HasMaxLength(500).IsRequired();
            entity.Property(x => x.RequestJson).HasColumnType("nvarchar(max)").IsRequired();
            entity.Property(x => x.EvidenceJson).HasColumnType("nvarchar(max)").IsRequired();
            entity.Property(x => x.EvidenceNotesJson).HasColumnType("nvarchar(max)").IsRequired();
            entity.Property(x => x.ExternalEvaluationId).HasMaxLength(64);
            entity.Property(x => x.ResultJson).HasColumnType("nvarchar(max)");
            entity.Property(x => x.LastError).HasMaxLength(2_000);
            entity.HasIndex(x => new { x.TestRunId, x.RequestedAtUtc });
            entity.HasIndex(x => x.ProjectId);
            // Evaluations are derived data: deleting a run removes them.
            entity.HasOne(x => x.TestRun).WithMany().HasForeignKey(x => x.TestRunId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<ExecutionLog>(entity =>
        {
            entity.ToTable("ExecutionLogs");
            entity.Property(x => x.Message).HasMaxLength(4_000).IsRequired();
            entity.HasIndex(x => new { x.TestRunId, x.TimestampUtc });
            entity.HasOne(x => x.TestRun).WithMany(x => x.Logs).HasForeignKey(x => x.TestRunId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.TestCaseResult).WithMany(x => x.Logs).HasForeignKey(x => x.TestCaseResultId)
                .OnDelete(DeleteBehavior.SetNull);
        });
    }
}
