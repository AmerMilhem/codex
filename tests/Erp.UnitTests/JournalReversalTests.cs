using Erp.Finance.Domain;
using Erp.Kernel;
namespace Erp.UnitTests;

public sealed class JournalReversalTests
{
    private static JournalEntry Original(bool posted = true)
    {
        var accounts = new[] { new AccountSnapshot(Guid.NewGuid(), AccountStatus.Active, true), new AccountSnapshot(Guid.NewGuid(), AccountStatus.Active, true) };
        var date = new DateOnly(2026, 1, 1); var now = DateTimeOffset.UtcNow;
        var j = JournalEntry.Create(Guid.NewGuid(), Guid.NewGuid(), date, "JOD", "REF", "Original", [new(null, accounts[0].Id, 0.001m, 0, "First"), new(null, accounts[1].Id, 0, 0.001m, "Second")], now);
        if (posted) j.Post(1, new("JOD", 3), [new(Guid.NewGuid(), 2026, FiscalPeriodStatus.Open, date, date)], accounts, 1, "tests", "actor", now);
        return j;
    }
    [Fact]
    public void Reverse_copies_stored_values_to_new_lines_without_changing_original()
    {
        var o = Original(); var r = JournalReversal.CreateOpposite(o, 2, false, false, new(2026, 2, 1), "Correction", DateTimeOffset.UtcNow);
        Assert.Equal(JournalStatus.Posted, o.Status); Assert.Equal(2, o.Revision); Assert.Equal(new DateOnly(2026, 1, 1), o.PostingDate); Assert.Equal(0.001m, o.Lines[0].Debit);
        Assert.Equal(0.001m, r.Lines[0].Credit); Assert.Equal(0.001m, r.Lines[1].Debit); Assert.Equal(o.Currency, r.Currency); Assert.NotEqual(o.Id, r.Id); Assert.Equal("First", r.Lines[0].Description);
    }
    [Theory]
    [InlineData(false, false, false, "FIN_JOURNAL_NOT_POSTED")]
    [InlineData(true, true, false, "FIN_JOURNAL_ALREADY_REVERSED")]
    [InlineData(true, false, true, "FIN_JOURNAL_REVERSAL_NOT_ALLOWED")]
    public void Eligibility_has_stable_errors(bool posted, bool reversed, bool isReversal, string error)
    {
        var o = Original(posted); Assert.Equal(error, Assert.Throws<FoundationException>(() => JournalReversal.CreateOpposite(o, o.Revision, reversed, isReversal, new(2026, 2, 1), "Correction", DateTimeOffset.UtcNow)).Error.Id);
    }
    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t\n")]
    public void Reason_is_required(string reason) => Assert.Equal(Errors.VALIDATION_FAILED, Assert.Throws<FoundationException>(() => JournalReversal.CreateOpposite(Original(), 2, false, false, new(2026, 2, 1), reason, DateTimeOffset.UtcNow)).Error);
    [Fact]
    public void Missing_date_and_stale_revision_are_rejected()
    {
        Assert.Equal(ReversalErrors.DateInvalid, Assert.Throws<FoundationException>(() => JournalReversal.CreateOpposite(Original(), 2, false, false, null, "Correction", DateTimeOffset.UtcNow)).Error);
        Assert.Equal(Errors.DOC_CONCURRENCY_CONFLICT, Assert.Throws<FoundationException>(() => JournalReversal.CreateOpposite(Original(), 1, false, false, new(2026, 2, 1), "Correction", DateTimeOffset.UtcNow)).Error);
    }
}
