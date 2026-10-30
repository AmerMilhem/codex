namespace Erp.Finance.Infrastructure;

// Frozen SQL for the Setup V1 migration. Runtime has no direct book UPDATE grant.
internal static class PeriodSetupSecuritySql
{
    public const string V1 = """
        CREATE FUNCTION finance.lock_book_for_period_create() RETURNS SETOF finance.accounting_books
          LANGUAGE sql SECURITY DEFINER SET search_path=pg_catalog AS $$
          SELECT * FROM finance.accounting_books
          WHERE tenant_id=nullif(current_setting('erp.tenant_id',true),'')::uuid
            AND company_id=nullif(current_setting('erp.company_id',true),'')::uuid FOR UPDATE
        $$;

        -- V2 of the period guard uses the scoped definer lock for runtime INSERT privilege.
        CREATE OR REPLACE FUNCTION finance.guard_period_close() RETURNS trigger LANGUAGE plpgsql AS $$
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
            PERFORM 1 FROM finance.lock_book_for_period_create();
            SELECT id INTO duplicate FROM finance.fiscal_periods WHERE (tenant_id,company_id)=(NEW.tenant_id,NEW.company_id)
              AND id<>NEW.id AND daterange(starts_on,ends_on,'[]') && daterange(NEW.starts_on,NEW.ends_on,'[]') LIMIT 1;
            IF duplicate IS NOT NULL THEN RAISE EXCEPTION 'finance.period_overlap' USING ERRCODE='23514'; END IF;
          END IF;
          RETURN NEW;
        END $$;

        CREATE FUNCTION finance.guard_period_creation_metadata() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN
          IF (NEW.created_at,NEW.created_by_issuer,NEW.created_by_subject)
               IS DISTINCT FROM (OLD.created_at,OLD.created_by_issuer,OLD.created_by_subject)
            THEN RAISE EXCEPTION 'finance.period_creation_immutable' USING ERRCODE='23514'; END IF;
          RETURN NEW;
        END $$;
        CREATE TRIGGER guard_period_creation_metadata BEFORE UPDATE ON finance.fiscal_periods
          FOR EACH ROW EXECUTE FUNCTION finance.guard_period_creation_metadata();

        CREATE FUNCTION finance.require_period_creation() RETURNS trigger LANGUAGE plpgsql AS $$
        DECLARE requested audit.entries; created audit.entries;
        BEGIN
          -- Legacy rows and administrator-owned regression fixtures have no creation metadata.
          -- Runtime inserts must have it and all command effects before commit.
          IF current_user='erp_runtime' OR NEW.created_at IS NOT NULL THEN
            SELECT * INTO requested FROM audit.entries WHERE (tenant_id,company_id)=(NEW.tenant_id,NEW.company_id)
              AND entity_type='FiscalPeriod' AND entity_id=NEW.id::text AND action='FiscalPeriodCreateRequested';
            SELECT * INTO created FROM audit.entries WHERE (tenant_id,company_id)=(NEW.tenant_id,NEW.company_id)
              AND entity_type='FiscalPeriod' AND entity_id=NEW.id::text AND action='FiscalPeriodCreated';
            IF NEW.created_at IS NULL OR NEW.created_by_issuer IS NULL OR NEW.created_by_subject IS NULL
              OR requested.id IS NULL OR created.id IS NULL
              OR (requested.actor_issuer,requested.actor_subject,requested.request_id,requested.correlation_id)
                  IS DISTINCT FROM (created.actor_issuer,created.actor_subject,created.request_id,created.correlation_id)
              OR (NEW.created_by_issuer,NEW.created_by_subject) IS DISTINCT FROM (created.actor_issuer,created.actor_subject)
              OR (SELECT count(*) FROM audit.entries WHERE (tenant_id,company_id)=(NEW.tenant_id,NEW.company_id)
                  AND entity_type='FiscalPeriod' AND entity_id=NEW.id::text
                  AND action IN ('FiscalPeriodCreateRequested','FiscalPeriodCreated'))<>2
              OR (SELECT count(*) FROM integration.outbox WHERE (tenant_id,company_id)=(NEW.tenant_id,NEW.company_id)
                  AND source_id=NEW.id::text AND type='finance.fiscal-period-created.v1'
                  AND payload->>'fiscalPeriodId'=NEW.id::text
                  AND payload->>'startDate'=NEW.starts_on::text AND payload->>'endDate'=NEW.ends_on::text)<>1
            THEN RAISE EXCEPTION 'finance.period_creation_effect_missing' USING ERRCODE='23514'; END IF;
          END IF;
          RETURN NEW;
        END $$;
        CREATE CONSTRAINT TRIGGER period_creation_integrity AFTER INSERT ON finance.fiscal_periods
          DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION finance.require_period_creation();
        REVOKE ALL ON FUNCTION finance.lock_book_for_period_create(),finance.guard_period_creation_metadata(),finance.require_period_creation() FROM PUBLIC;
        GRANT EXECUTE ON FUNCTION finance.lock_book_for_period_create(),finance.guard_period_creation_metadata(),finance.require_period_creation() TO erp_runtime;
        GRANT INSERT ON finance.fiscal_periods TO erp_runtime;
        """;
}
