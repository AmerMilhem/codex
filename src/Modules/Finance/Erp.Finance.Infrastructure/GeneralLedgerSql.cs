namespace Erp.Finance.Infrastructure;

public static class GeneralLedgerSql
{
    public const string Source = """
        FROM finance.journal_entries j JOIN finance.journal_lines l
          ON (l.tenant_id,l.company_id,l.journal_id)=(j.tenant_id,j.company_id,j.id)
        WHERE j.tenant_id=@tenant AND j.company_id=@company AND l.account_id=@account
          AND j.status='Posted'
        """;
    public const string Opening = "SELECT trim_scale(coalesce(sum(l.debit-l.credit),0))::text " + Source + " AND j.posting_date<@start";
    public const string Summary = """
        SELECT count(*),trim_scale(coalesce(sum(l.debit),0))::text,trim_scale(coalesce(sum(l.credit),0))::text
        """ + " " + Source + " AND j.posting_date BETWEEN @start AND @end";
    // Window evaluated over ALL period rows before LIMIT/OFFSET. Unique line ID is the final tie-breaker.
    // Running net is independent of page size and never recomputed from only the visible page.
    public const string Movements = """
        SELECT l.id,j.id,l.position,j.posting_date,j.journal_number,j.reference,l.description,j.description,
          trim_scale(l.debit)::text,trim_scale(l.credit)::text,
          trim_scale(@opening::numeric + sum(l.debit-l.credit) OVER (
            ORDER BY j.posting_date,j.id,l.position,l.id ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW))::text
        """ + " " + Source + """
         AND j.posting_date BETWEEN @start AND @end
        ORDER BY j.posting_date,j.id,l.position,l.id LIMIT @limit OFFSET @offset
        """;
    // Reuse the actual Trial Balance projection, independently of GL sums/window, within the SAME snapshot.
    public const string Trial = "SELECT * FROM (" + TrialBalanceSql.Accounts + ") trial WHERE id=@account";
}
