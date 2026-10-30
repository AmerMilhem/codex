namespace Erp.Finance.Infrastructure;

// Frozen SQL for Book Setup V1. This preserves one company book; no multi-ledger capability.
internal static class AccountingBookSetupSecuritySql
{
    public const string V1 = """
        CREATE FUNCTION finance.lock_company_for_book_create() RETURNS void
          LANGUAGE sql SECURITY DEFINER SET search_path=pg_catalog AS $$
          SELECT pg_advisory_xact_lock(hashtextextended(
            coalesce(current_setting('erp.tenant_id',true),'')||':'||
            coalesce(current_setting('erp.company_id',true),''), 11872))
        $$;

        CREATE FUNCTION finance.guard_book_setup() RETURNS trigger LANGUAGE plpgsql AS $$
        DECLARE expected_units int;
        BEGIN
          IF TG_OP='UPDATE' THEN
            IF (NEW.id,NEW.tenant_id,NEW.company_id,NEW.code,NEW.name,NEW.status,
                NEW.created_at,NEW.created_by_issuer,NEW.created_by_subject)
                 IS DISTINCT FROM (OLD.id,OLD.tenant_id,OLD.company_id,OLD.code,OLD.name,OLD.status,
                     OLD.created_at,OLD.created_by_issuer,OLD.created_by_subject)
            THEN RAISE EXCEPTION 'finance.book_identity_immutable' USING ERRCODE='23514'; END IF;
            RETURN NEW;
          END IF;
          IF NEW.created_at IS NOT NULL OR current_user='erp_runtime' THEN
            expected_units := CASE NEW.currency
              WHEN 'JOD' THEN 3 WHEN 'USD' THEN 2 WHEN 'EUR' THEN 2 WHEN 'GBP' THEN 2
              WHEN 'SAR' THEN 2 WHEN 'AED' THEN 2 WHEN 'KWD' THEN 3 WHEN 'BHD' THEN 3
              WHEN 'OMR' THEN 3 WHEN 'JPY' THEN 0 ELSE NULL END;
            IF NEW.status<>'Active' OR NEW.created_at IS NULL OR NEW.created_by_issuer IS NULL
              OR NEW.created_by_subject IS NULL OR length(NEW.created_by_issuer)=0
              OR length(NEW.created_by_subject)=0 OR NEW.code !~ '^[A-Z0-9][A-Z0-9._-]{0,31}$'
              OR NEW.name<>btrim(NEW.name) OR length(NEW.name)=0
              OR expected_units IS NULL OR NEW.minor_units<>expected_units
            THEN RAISE EXCEPTION 'finance.book_initial_state' USING ERRCODE='23514'; END IF;
            PERFORM finance.lock_company_for_book_create();
          END IF;
          RETURN NEW;
        END $$;
        CREATE TRIGGER guard_book_setup BEFORE INSERT OR UPDATE ON finance.accounting_books
          FOR EACH ROW EXECUTE FUNCTION finance.guard_book_setup();

        CREATE FUNCTION finance.require_book_creation() RETURNS trigger LANGUAGE plpgsql AS $$
        DECLARE requested audit.entries; created audit.entries;
        BEGIN
          IF current_user='erp_runtime' OR NEW.created_at IS NOT NULL THEN
            SELECT * INTO requested FROM audit.entries WHERE (tenant_id,company_id)=(NEW.tenant_id,NEW.company_id)
              AND entity_type='AccountingBook' AND entity_id=NEW.id::text AND action='AccountingBookCreateRequested';
            SELECT * INTO created FROM audit.entries WHERE (tenant_id,company_id)=(NEW.tenant_id,NEW.company_id)
              AND entity_type='AccountingBook' AND entity_id=NEW.id::text AND action='AccountingBookCreated';
            IF requested.id IS NULL OR created.id IS NULL
              OR (requested.actor_issuer,requested.actor_subject,requested.request_id,requested.correlation_id)
                  IS DISTINCT FROM (created.actor_issuer,created.actor_subject,created.request_id,created.correlation_id)
              OR (NEW.created_by_issuer,NEW.created_by_subject) IS DISTINCT FROM (created.actor_issuer,created.actor_subject)
              OR (SELECT count(*) FROM audit.entries WHERE (tenant_id,company_id)=(NEW.tenant_id,NEW.company_id)
                  AND entity_type='AccountingBook' AND entity_id=NEW.id::text
                  AND action IN ('AccountingBookCreateRequested','AccountingBookCreated'))<>2
              OR (SELECT count(*) FROM integration.outbox WHERE (tenant_id,company_id)=(NEW.tenant_id,NEW.company_id)
                  AND source_id=NEW.id::text AND type='finance.accounting-book-created.v1'
                  AND payload->>'accountingBookId'=NEW.id::text AND payload->>'code'=NEW.code
                  AND payload->>'functionalCurrency'=NEW.currency)<>1
            THEN RAISE EXCEPTION 'finance.book_creation_effect_missing' USING ERRCODE='23514'; END IF;
          END IF;
          RETURN NEW;
        END $$;
        CREATE CONSTRAINT TRIGGER book_creation_integrity AFTER INSERT ON finance.accounting_books
          DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION finance.require_book_creation();
        REVOKE ALL ON FUNCTION finance.lock_company_for_book_create(),finance.guard_book_setup(),finance.require_book_creation() FROM PUBLIC;
        GRANT EXECUTE ON FUNCTION finance.lock_company_for_book_create(),finance.guard_book_setup(),finance.require_book_creation() TO erp_runtime;
        GRANT INSERT ON finance.accounting_books TO erp_runtime;
        """;
}
