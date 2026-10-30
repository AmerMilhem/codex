-- @range_StartDate='01/01/2026' (Nullable = true) (DbType = Date)
-- @context_TenantId='1e9889b6-5ce4-4c76-a163-d405ef7804d3'
-- @context_CompanyId='356df8c8-9368-476d-a419-e95cf5508fd7'
-- @range_EndDate='01/31/2026' (Nullable = true) (DbType = Date)
SELECT a.id AS "Id", a.code AS "Code", a.name AS "Name", GREATEST(s."Opening", 0.0) AS "OpeningDebit", GREATEST(-s."Opening", 0.0) AS "OpeningCredit", s."Debit" AS "PeriodDebit", s."Credit" AS "PeriodCredit", GREATEST((s."Opening" + s."Debit") - s."Credit", 0.0) AS "ClosingDebit", GREATEST(-((s."Opening" + s."Debit") - s."Credit"), 0.0) AS "ClosingCredit"
FROM (
    SELECT j0.account_id AS "Id", COALESCE(sum(CASE
        WHEN j.posting_date < @range_StartDate THEN j0.debit - j0.credit
        ELSE 0.0
    END), 0.0) AS "Opening", COALESCE(sum(CASE
        WHEN j.posting_date >= @range_StartDate THEN j0.debit
        ELSE 0.0
    END), 0.0) AS "Debit", COALESCE(sum(CASE
        WHEN j.posting_date >= @range_StartDate THEN j0.credit
        ELSE 0.0
    END), 0.0) AS "Credit"
    FROM finance.journal_entries AS j
    INNER JOIN finance.journal_lines AS j0 ON j.tenant_id = j0.tenant_id AND j.company_id = j0.company_id AND j.id = j0.journal_id
    WHERE j.tenant_id = @context_TenantId AND j.company_id = @context_CompanyId AND j.status = 'Posted' AND j.posting_date <= @range_EndDate
    GROUP BY j0.account_id
) AS s
INNER JOIN finance.accounts AS a ON s."Id" = a.id
WHERE a.tenant_id = @context_TenantId AND a.company_id = @context_CompanyId AND (s."Opening" <> 0.0 OR s."Opening" IS NULL OR s."Debit" <> 0.0 OR s."Debit" IS NULL OR s."Credit" <> 0.0 OR s."Credit" IS NULL)
ORDER BY a.code, a.id