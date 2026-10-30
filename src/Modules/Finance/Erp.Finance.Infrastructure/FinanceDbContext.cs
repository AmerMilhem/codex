using Erp.Finance.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Erp.Finance.Infrastructure;

public sealed class AccountingBookRow
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid CompanyId { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string Currency { get; set; } = "";
    public int MinorUnits { get; set; }
    public AccountingBookStatus Status { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public string? CreatedByIssuer { get; set; }
    public string? CreatedBySubject { get; set; }
}
public sealed class AccountRow
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid CompanyId { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public AccountStatus Status { get; set; }
    public bool PostingAllowed { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public string? CreatedByIssuer { get; set; }
    public string? CreatedBySubject { get; set; }
}
public sealed class FiscalPeriodRow
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid CompanyId { get; set; }
    public int FiscalYear { get; set; }
    public DateOnly StartsOn { get; set; }
    public DateOnly EndsOn { get; set; }
    public FiscalPeriodStatus Status { get; set; }
    public long Revision { get; set; } = 1;
    public DateTimeOffset? ClosedAt { get; set; }
    public string? ClosedByIssuer { get; set; }
    public string? ClosedBySubject { get; set; }
    public string? CloseComment { get; set; }
    public string? CloseReconciliation { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public string? CreatedByIssuer { get; set; }
    public string? CreatedBySubject { get; set; }
}
public sealed class JournalNumberRow
{
    public Guid TenantId { get; set; }
    public Guid CompanyId { get; set; }
    public int FiscalYear { get; set; }
    public long LastNumber { get; set; }
}
public sealed class FinanceDbContext(DbContextOptions<FinanceDbContext> options) : DbContext(options)
{
    public DbSet<JournalCorrection> Corrections => Set<JournalCorrection>();
    public DbSet<JournalReversal> Reversals => Set<JournalReversal>();
    public DbSet<JournalEntry> Journals => Set<JournalEntry>();
    public DbSet<JournalLine> Lines => Set<JournalLine>();
    public DbSet<AccountRow> Accounts => Set<AccountRow>();
    public DbSet<FiscalPeriodRow> Periods => Set<FiscalPeriodRow>();
    public DbSet<AccountingBookRow> Books => Set<AccountingBookRow>();
    public static DbContextOptions<FinanceDbContext> Options(Npgsql.NpgsqlConnection connection) =>
        new DbContextOptionsBuilder<FinanceDbContext>().UseNpgsql(connection, x => x.MigrationsHistoryTable("__FinanceMigrationsHistory")).Options;
    protected override void OnModelCreating(ModelBuilder b)
    {
        var book = b.Entity<AccountingBookRow>(); book.ToTable("accounting_books", "finance", t =>
        {
            t.HasCheckConstraint("book_currency_policy", "currency ~ '^[A-Z]{3}$' AND minor_units BETWEEN 0 AND 6");
            t.HasCheckConstraint("book_setup_valid", "length(code)>0 AND length(name)>0 AND status='Active' AND ((created_at IS NULL AND created_by_issuer IS NULL AND created_by_subject IS NULL) OR (created_at IS NOT NULL AND length(created_by_issuer)>0 AND length(created_by_subject)>0))");
        });
        book.HasKey(x => new { x.TenantId, x.CompanyId });
        book.HasIndex(x => x.Id).IsUnique();
        book.HasIndex(x => new { x.TenantId, x.CompanyId, x.Code }).IsUnique();
        book.Property(x => x.Code).HasMaxLength(32); book.Property(x => x.Name).HasMaxLength(200);
        book.Property(x => x.Currency).HasMaxLength(3);
        book.Property(x => x.Status).HasConversion<string>().HasMaxLength(16);
        book.Property(x => x.CreatedByIssuer).HasMaxLength(200);
        book.Property(x => x.CreatedBySubject).HasMaxLength(200);
        var account = b.Entity<AccountRow>(); account.ToTable("accounts", "finance", t =>
        {
            t.HasCheckConstraint("account_valid", "length(code)>0 AND length(name)>0 AND status IN ('Active','Inactive')");
            t.HasCheckConstraint("account_create_metadata", "(created_at IS NULL AND created_by_issuer IS NULL AND created_by_subject IS NULL) OR (created_at IS NOT NULL AND length(created_by_issuer)>0 AND length(created_by_subject)>0)");
        });
        account.HasKey(x => x.Id); account.HasAlternateKey(x => new { x.TenantId, x.CompanyId, x.Id });
        account.HasIndex(x => new { x.TenantId, x.CompanyId, x.Code }).IsUnique();
        account.Property(x => x.Code).HasMaxLength(32); account.Property(x => x.Name).HasMaxLength(200);
        account.Property(x => x.Status).HasConversion<string>().HasMaxLength(16);
        account.Property(x => x.CreatedByIssuer).HasMaxLength(200);
        account.Property(x => x.CreatedBySubject).HasMaxLength(200);
        account.HasOne<AccountingBookRow>().WithMany().HasForeignKey(x => new { x.TenantId, x.CompanyId }).OnDelete(DeleteBehavior.Restrict);
        var period = b.Entity<FiscalPeriodRow>(); period.ToTable("fiscal_periods", "finance", t =>
        {
            t.HasCheckConstraint("period_valid", "starts_on<=ends_on AND fiscal_year BETWEEN 1 AND 9999 AND status IN ('Open','Closed')");
            t.HasCheckConstraint("period_close_metadata", "revision>0 AND ((status='Open' AND revision=1 AND closed_at IS NULL AND closed_by_issuer IS NULL AND closed_by_subject IS NULL AND close_comment IS NULL AND close_reconciliation IS NULL) OR (status='Closed' AND revision=2 AND closed_at IS NOT NULL AND length(closed_by_issuer)>0 AND length(closed_by_subject)>0 AND length(close_reconciliation)>0))");
            t.HasCheckConstraint("period_create_metadata", "(created_at IS NULL AND created_by_issuer IS NULL AND created_by_subject IS NULL) OR (created_at IS NOT NULL AND length(created_by_issuer)>0 AND length(created_by_subject)>0)");
        });
        period.HasKey(x => x.Id); period.HasAlternateKey(x => new { x.TenantId, x.CompanyId, x.Id });
        period.HasIndex(x => new { x.TenantId, x.CompanyId, x.StartsOn, x.EndsOn }).IsUnique();
        period.Property(x => x.Status).HasConversion<string>().HasMaxLength(16);
        period.Property(x => x.Revision).HasDefaultValue(1).IsConcurrencyToken();
        period.Property(x => x.ClosedByIssuer).HasMaxLength(200);
        period.Property(x => x.ClosedBySubject).HasMaxLength(200);
        period.Property(x => x.CloseComment).HasMaxLength(1000);
        period.Property(x => x.CloseReconciliation).HasColumnType("text");
        period.Property(x => x.CreatedByIssuer).HasMaxLength(200);
        period.Property(x => x.CreatedBySubject).HasMaxLength(200);
        period.HasOne<AccountingBookRow>().WithMany().HasForeignKey(x => new { x.TenantId, x.CompanyId }).OnDelete(DeleteBehavior.Restrict);
        var journal = b.Entity<JournalEntry>(); journal.ToTable("journal_entries", "finance", t =>
        {
            t.HasCheckConstraint("journal_revision", "revision>0");
            t.HasCheckConstraint("journal_state", "(status='Draft' AND fiscal_period_id IS NULL AND fiscal_year IS NULL AND number_sequence IS NULL AND journal_number IS NULL AND posted_at IS NULL AND posted_by_subject IS NULL AND posted_by_issuer IS NULL) OR (status='Posted' AND posting_date IS NOT NULL AND fiscal_period_id IS NOT NULL AND fiscal_year IS NOT NULL AND number_sequence>0 AND journal_number IS NOT NULL AND posted_at IS NOT NULL AND posted_by_subject IS NOT NULL AND posted_by_issuer IS NOT NULL)");
        });
        journal.HasKey(x => x.Id); journal.HasAlternateKey(x => new { x.TenantId, x.CompanyId, x.Id });
        journal.Property(x => x.Revision).IsConcurrencyToken();
        journal.Property(x => x.Status).HasConversion<string>().HasMaxLength(16);
        journal.Property(x => x.Reference).HasMaxLength(128); journal.Property(x => x.Description).HasMaxLength(1000);
        journal.Property(x => x.Currency).HasMaxLength(3); journal.Property(x => x.RoundingPolicy).HasMaxLength(64);
        journal.Property(x => x.JournalNumber).HasMaxLength(64); journal.Property(x => x.PostedByIssuer).HasMaxLength(200); journal.Property(x => x.PostedBySubject).HasMaxLength(200);
        journal.Ignore(x => x.TotalDebit); journal.Ignore(x => x.TotalCredit);
        journal.HasIndex(x => new { x.TenantId, x.CompanyId, x.CreatedAt, x.Id });
        journal.HasIndex(x => new { x.TenantId, x.CompanyId, x.Status, x.PostingDate });
        journal.HasIndex(x => new { x.TenantId, x.CompanyId, x.FiscalYear, x.NumberSequence }).IsUnique();
        journal.HasIndex(x => new { x.TenantId, x.CompanyId, x.JournalNumber }).IsUnique();
        journal.HasOne<AccountingBookRow>().WithMany().HasForeignKey(x => new { x.TenantId, x.CompanyId }).OnDelete(DeleteBehavior.Restrict);
        journal.HasOne<FiscalPeriodRow>().WithMany().HasForeignKey(x => new { x.TenantId, x.CompanyId, x.FiscalPeriodId })
            .HasPrincipalKey(x => new { x.TenantId, x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        journal.HasMany(x => x.Lines).WithOne().HasForeignKey(x => new { x.TenantId, x.CompanyId, x.JournalId })
            .HasPrincipalKey(x => new { x.TenantId, x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Cascade);
        journal.Navigation(x => x.Lines).HasField("lines").UsePropertyAccessMode(PropertyAccessMode.Field);
        var line = b.Entity<JournalLine>(); line.ToTable("journal_lines", "finance", t =>
            t.HasCheckConstraint("line_amounts", "debit>=0 AND credit>=0 AND position BETWEEN 1 AND 200"));
        line.HasKey(x => x.Id); line.Property(x => x.Debit).HasPrecision(24, 6); line.Property(x => x.Credit).HasPrecision(24, 6);
        line.Property(x => x.Description).HasMaxLength(256);
        line.HasOne<AccountRow>().WithMany().HasForeignKey(x => new { x.TenantId, x.CompanyId, x.AccountId })
            .HasPrincipalKey(x => new { x.TenantId, x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        line.HasIndex(x => new { x.TenantId, x.CompanyId, x.JournalId, x.Position });
        var number = b.Entity<JournalNumberRow>(); number.ToTable("journal_numbers", "finance", t => t.HasCheckConstraint("number_positive", "last_number>0"));
        number.HasKey(x => new { x.TenantId, x.CompanyId, x.FiscalYear });
        number.HasOne<AccountingBookRow>().WithMany().HasForeignKey(x => new { x.TenantId, x.CompanyId }).OnDelete(DeleteBehavior.Restrict);
        var reversal = b.Entity<JournalReversal>(); reversal.ToTable("journal_reversals", "finance", t => t.HasCheckConstraint("reversal_valid", "original_journal_id<>reversal_journal_id AND length(btrim(reason)) BETWEEN 1 AND 1000"));
        reversal.HasKey(x => new { x.TenantId, x.CompanyId, x.OriginalJournalId });
        reversal.HasIndex(x => new { x.TenantId, x.CompanyId, x.ReversalJournalId }).IsUnique();
        reversal.Property(x => x.Reason).HasMaxLength(1000); reversal.Property(x => x.ActorIssuer).HasMaxLength(200); reversal.Property(x => x.ActorSubject).HasMaxLength(200);
        reversal.Property(x => x.RequestId).HasMaxLength(128); reversal.Property(x => x.CorrelationId).HasMaxLength(128);
        reversal.HasOne<JournalEntry>().WithMany().HasForeignKey(x => new { x.TenantId, x.CompanyId, x.OriginalJournalId }).HasPrincipalKey(x => new { x.TenantId, x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        reversal.HasOne<JournalEntry>().WithMany().HasForeignKey(x => new { x.TenantId, x.CompanyId, x.ReversalJournalId }).HasPrincipalKey(x => new { x.TenantId, x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        var correction = b.Entity<JournalCorrection>();
        correction.ToTable("journal_corrections", "finance", t => t.HasCheckConstraint("correction_valid", "original_journal_id<>reversal_journal_id AND original_journal_id<>replacement_journal_id AND reversal_journal_id<>replacement_journal_id AND length(btrim(reason)) BETWEEN 1 AND 1000"));
        correction.HasKey(x => x.Id);
        correction.HasIndex(x => new { x.TenantId, x.CompanyId, x.OriginalJournalId }).IsUnique();
        correction.HasIndex(x => new { x.TenantId, x.CompanyId, x.ReversalJournalId }).IsUnique();
        correction.HasIndex(x => new { x.TenantId, x.CompanyId, x.ReplacementJournalId }).IsUnique();
        correction.Property(x => x.Reason).HasMaxLength(1000);
        correction.Property(x => x.ActorIssuer).HasMaxLength(200); correction.Property(x => x.ActorSubject).HasMaxLength(200);
        correction.Property(x => x.RequestId).HasMaxLength(128); correction.Property(x => x.CorrelationId).HasMaxLength(128);
        correction.HasOne<JournalEntry>().WithMany().HasForeignKey(x => new { x.TenantId, x.CompanyId, x.OriginalJournalId }).HasPrincipalKey(x => new { x.TenantId, x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        correction.HasOne<JournalEntry>().WithMany().HasForeignKey(x => new { x.TenantId, x.CompanyId, x.ReversalJournalId }).HasPrincipalKey(x => new { x.TenantId, x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        correction.HasOne<JournalEntry>().WithMany().HasForeignKey(x => new { x.TenantId, x.CompanyId, x.ReplacementJournalId }).HasPrincipalKey(x => new { x.TenantId, x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        foreach (var entity in b.Model.GetEntityTypes())
        {
            if (entity.FindProperty("Id") is { } id) id.ValueGenerated = Microsoft.EntityFrameworkCore.Metadata.ValueGenerated.Never;
            foreach (var p in entity.GetProperties()) p.SetColumnName(System.Text.RegularExpressions.Regex.Replace(p.Name, "(?<!^)([A-Z])", "_$1").ToLowerInvariant());
        }
    }
}
public sealed class FinanceDesignTimeFactory : IDesignTimeDbContextFactory<FinanceDbContext>
{
    public FinanceDbContext CreateDbContext(string[] args) => new(FinanceDbContext.Options(new("Host=127.0.0.1;Database=design_only;Username=design_only")));
}
