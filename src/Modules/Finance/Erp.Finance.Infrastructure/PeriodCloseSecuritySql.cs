namespace Erp.Finance.Infrastructure;

// Frozen migration SQL. Finance runtime does not own reference tables or these functions.
internal static class PeriodCloseSecuritySql
{
    public const string V1 = """
        DROP TRIGGER guard_period ON finance.fiscal_periods;
        DROP FUNCTION finance.guard_period();

        CREATE FUNCTION finance.guard_period_close() RETURNS trigger LANGUAGE plpgsql AS $$
        DECLARE duplicate uuid;
        BEGIN
          IF TG_OP='DELETE' THEN RAISE EXCEPTION 'finance.period_delete_forbidden' USING ERRCODE='23514'; END IF;
          IF TG_OP='INSERT' THEN
            IF NEW.status<>'Open' OR NEW.revision<>1 THEN RAISE EXCEPTION 'finance.period_initial_state' USING ERRCODE='23514'; END IF;
          ELSE
            IF OLD.status='Closed' THEN RAISE EXCEPTION 'finance.period_closed_immutable' USING ERRCODE='23514'; END IF;
            IF (NEW.id,NEW.tenant_id,NEW.company_id,NEW.fiscal_year,NEW.starts_on,NEW.ends_on)
                 IS DISTINCT FROM (OLD.id,OLD.tenant_id,OLD.company_id,OLD.fiscal_year,OLD.starts_on,OLD.ends_on)
              AND EXISTS(SELECT 1 FROM finance.journal_entries WHERE (tenant_id,company_id,fiscal_period_id)
                =(OLD.tenant_id,OLD.company_id,OLD.id) AND status='Posted')
            THEN RAISE EXCEPTION 'finance.period_history_immutable' USING ERRCODE='23514'; END IF;
            IF NEW.status IS DISTINCT FROM OLD.status AND NOT (OLD.status='Open' AND NEW.status='Closed' AND NEW.revision=OLD.revision+1)
            THEN RAISE EXCEPTION 'finance.period_transition_invalid' USING ERRCODE='23514'; END IF;
            IF NEW.status='Open' AND (NEW.revision,NEW.closed_at,NEW.closed_by_issuer,NEW.closed_by_subject,NEW.close_comment,NEW.close_reconciliation)
               IS DISTINCT FROM (OLD.revision,OLD.closed_at,OLD.closed_by_issuer,OLD.closed_by_subject,OLD.close_comment,OLD.close_reconciliation)
            THEN RAISE EXCEPTION 'finance.period_metadata_invalid' USING ERRCODE='23514'; END IF;
          END IF;
          IF TG_OP='INSERT' OR (NEW.tenant_id,NEW.company_id,NEW.starts_on,NEW.ends_on) IS DISTINCT FROM
                (OLD.tenant_id,OLD.company_id,OLD.starts_on,OLD.ends_on) THEN
            -- Serialize period definition changes on the company book; no two insertions can pass the overlap scan together.
            PERFORM 1 FROM finance.accounting_books WHERE (tenant_id,company_id)=(NEW.tenant_id,NEW.company_id) FOR UPDATE;
            SELECT id INTO duplicate FROM finance.fiscal_periods WHERE (tenant_id,company_id)=(NEW.tenant_id,NEW.company_id)
              AND id<>NEW.id AND daterange(starts_on,ends_on,'[]') && daterange(NEW.starts_on,NEW.ends_on,'[]') LIMIT 1;
            IF duplicate IS NOT NULL THEN RAISE EXCEPTION 'finance.period_overlap' USING ERRCODE='23514'; END IF;
          END IF;
          RETURN NEW;
        END $$;
        CREATE TRIGGER guard_period_close BEFORE INSERT OR UPDATE OR DELETE ON finance.fiscal_periods
          FOR EACH ROW EXECUTE FUNCTION finance.guard_period_close();

        CREATE FUNCTION finance.require_period_close() RETURNS trigger LANGUAGE plpgsql AS $$
        DECLARE requested audit.entries; closed audit.entries;
        BEGIN
          SELECT * INTO requested FROM audit.entries WHERE (tenant_id,company_id)=(NEW.tenant_id,NEW.company_id)
            AND entity_type='FiscalPeriod' AND entity_id=NEW.id::text AND action='FiscalPeriodCloseRequested';
          SELECT * INTO closed FROM audit.entries WHERE (tenant_id,company_id)=(NEW.tenant_id,NEW.company_id)
            AND entity_type='FiscalPeriod' AND entity_id=NEW.id::text AND action='FiscalPeriodClosed';
          IF requested.id IS NULL OR closed.id IS NULL
            OR NEW.close_reconciliation::jsonb->>'passed'<>'true'
            OR (requested.actor_issuer,requested.actor_subject,requested.request_id,requested.correlation_id)
                IS DISTINCT FROM (closed.actor_issuer,closed.actor_subject,closed.request_id,closed.correlation_id)
            OR (NEW.closed_by_issuer,NEW.closed_by_subject) IS DISTINCT FROM (closed.actor_issuer,closed.actor_subject)
            OR (SELECT count(*) FROM audit.entries WHERE (tenant_id,company_id)=(NEW.tenant_id,NEW.company_id)
                AND entity_type='FiscalPeriod' AND entity_id=NEW.id::text
                AND action IN ('FiscalPeriodCloseRequested','FiscalPeriodClosed'))<>2
            OR (SELECT count(*) FROM integration.outbox WHERE (tenant_id,company_id)=(NEW.tenant_id,NEW.company_id)
                AND type='finance.fiscal-period-closed.v1' AND source_id=NEW.id::text
                AND payload->>'fiscalPeriodId'=NEW.id::text
                AND payload->>'startDate'=NEW.starts_on::text AND payload->>'endDate'=NEW.ends_on::text)<>1
          THEN RAISE EXCEPTION 'finance.period_close_effect_missing' USING ERRCODE='23514'; END IF;
          RETURN NEW;
        END $$;
        CREATE CONSTRAINT TRIGGER period_close_integrity AFTER UPDATE ON finance.fiscal_periods
          DEFERRABLE INITIALLY DEFERRED FOR EACH ROW
          WHEN (OLD.status='Open' AND NEW.status='Closed') EXECUTE FUNCTION finance.require_period_close();
        REVOKE ALL ON FUNCTION finance.guard_period_close(),finance.require_period_close() FROM PUBLIC;
        GRANT EXECUTE ON FUNCTION finance.guard_period_close(),finance.require_period_close() TO erp_runtime;
        GRANT UPDATE(status,revision,closed_at,closed_by_issuer,closed_by_subject,close_comment,close_reconciliation) ON finance.fiscal_periods TO erp_runtime;
        """;
}
