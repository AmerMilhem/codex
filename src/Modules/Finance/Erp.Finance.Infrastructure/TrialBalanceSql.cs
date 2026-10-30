namespace Erp.Finance.Infrastructure;

public static class TrialBalanceSql
{
    // Explicit predicates aid the index plan; FORCE RLS independently enforces the verified transaction scope.
    // No fiscal-period status filter: closing restricts writes, not historical reporting.
    public const string Accounts = """
        WITH movements AS (
          SELECT l.account_id,
            coalesce(sum(l.debit-l.credit) FILTER(WHERE j.posting_date<@start),0) AS opening,
            coalesce(sum(l.debit) FILTER(WHERE j.posting_date>=@start),0) AS period_debit,
            coalesce(sum(l.credit) FILTER(WHERE j.posting_date>=@start),0) AS period_credit
          FROM finance.journal_entries j JOIN finance.journal_lines l
            ON (l.tenant_id,l.company_id,l.journal_id)=(j.tenant_id,j.company_id,j.id)
          WHERE j.tenant_id=@tenant AND j.company_id=@company AND j.status='Posted' AND j.posting_date<=@end
          GROUP BY l.account_id
        )
        SELECT a.id,a.code,a.name,
          trim_scale(greatest(m.opening,0))::text,trim_scale(greatest(-m.opening,0))::text,
          trim_scale(m.period_debit)::text,trim_scale(m.period_credit)::text,
          trim_scale(greatest(m.opening+m.period_debit-m.period_credit,0))::text,
          trim_scale(greatest(-(m.opening+m.period_debit-m.period_credit),0))::text
        FROM movements m JOIN finance.accounts a ON a.id=m.account_id AND a.tenant_id=@tenant AND a.company_id=@company
        WHERE m.opening<>0 OR m.period_debit<>0 OR m.period_credit<>0
        ORDER BY a.code COLLATE "C",a.id
        """;

    // Independent control query: no account-master join or report-row filters. Raw ledger sums and per-journal integrity
    // remain visible even if the report projection accidentally omits an account or opposite journal errors cancel globally.
    public const string Control = """
        WITH ledger AS MATERIALIZED (
          SELECT j.id,j.posting_date,l.id AS line_id,l.account_id,l.debit,l.credit
          FROM finance.journal_entries j LEFT JOIN finance.journal_lines l
            ON (l.tenant_id,l.company_id,l.journal_id)=(j.tenant_id,j.company_id,j.id)
          WHERE j.tenant_id=@tenant AND j.company_id=@company AND j.status='Posted' AND j.posting_date<=@end
        ), account_controls AS (
          SELECT account_id,
            coalesce(sum(debit-credit) FILTER(WHERE posting_date<@start),0) AS opening,
            coalesce(sum(debit-credit),0) AS closing
          FROM ledger WHERE line_id IS NOT NULL GROUP BY account_id
        ), journal_controls AS (
          SELECT id FROM ledger GROUP BY id
          HAVING count(line_id)<2 OR coalesce(sum(debit),0)<>coalesce(sum(credit),0)
            OR count(*) FILTER(WHERE line_id IS NOT NULL AND NOT ((debit>0 AND credit=0) OR (credit>0 AND debit=0)))>0
        )
        SELECT
          trim_scale(coalesce((SELECT sum(greatest(opening,0)) FROM account_controls),0))::text,
          trim_scale(coalesce((SELECT sum(greatest(-opening,0)) FROM account_controls),0))::text,
          trim_scale(coalesce(sum(debit) FILTER(WHERE posting_date>=@start),0))::text,
          trim_scale(coalesce(sum(credit) FILTER(WHERE posting_date>=@start),0))::text,
          trim_scale(coalesce((SELECT sum(greatest(closing,0)) FROM account_controls),0))::text,
          trim_scale(coalesce((SELECT sum(greatest(-closing,0)) FROM account_controls),0))::text,
          trim_scale(coalesce(sum(debit) FILTER(WHERE posting_date<@start),0))::text,
          trim_scale(coalesce(sum(credit) FILTER(WHERE posting_date<@start),0))::text,
          trim_scale(coalesce(sum(debit),0))::text,trim_scale(coalesce(sum(credit),0))::text,
          count(DISTINCT id),count(line_id),(SELECT count(*) FROM journal_controls)
        FROM ledger
        """;
}
