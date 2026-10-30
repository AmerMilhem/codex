namespace Erp.Finance.Infrastructure;

// Frozen SQL for the Account Setup V1 migration; later changes require a new migration.
internal static class AccountSetupSecuritySql
{
    public const string V1 = """
        CREATE FUNCTION finance.lock_book_for_account_create() RETURNS SETOF finance.accounting_books
          LANGUAGE sql SECURITY DEFINER SET search_path=pg_catalog AS $$
          SELECT * FROM finance.accounting_books
          WHERE tenant_id=nullif(current_setting('erp.tenant_id',true),'')::uuid
            AND company_id=nullif(current_setting('erp.company_id',true),'')::uuid FOR UPDATE
        $$;

        CREATE FUNCTION finance.guard_account_setup() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
          IF TG_OP='UPDATE' THEN
            IF (NEW.created_at,NEW.created_by_issuer,NEW.created_by_subject)
                 IS DISTINCT FROM (OLD.created_at,OLD.created_by_issuer,OLD.created_by_subject)
              THEN RAISE EXCEPTION 'finance.account_creation_immutable' USING ERRCODE='23514'; END IF;
            RETURN NEW;
          END IF;
          IF NEW.created_at IS NOT NULL OR current_user='erp_runtime' THEN
            IF NEW.status<>'Active' OR NEW.created_at IS NULL OR length(NEW.created_by_issuer)=0
              OR length(NEW.created_by_subject)=0 OR NEW.created_by_issuer IS NULL OR NEW.created_by_subject IS NULL
              OR NEW.code !~ '^[A-Z0-9][A-Z0-9._-]{0,31}$'
              OR NEW.name<>btrim(NEW.name) OR length(NEW.name)=0
            THEN RAISE EXCEPTION 'finance.account_initial_state' USING ERRCODE='23514'; END IF;
            PERFORM 1 FROM finance.lock_book_for_account_create();
            IF EXISTS(SELECT 1 FROM finance.accounts WHERE (tenant_id,company_id)=(NEW.tenant_id,NEW.company_id)
                AND upper(btrim(code))=NEW.code)
            THEN RAISE EXCEPTION 'finance.account_duplicate' USING ERRCODE='23505', CONSTRAINT='accounts_code_normalized'; END IF;
          END IF;
          RETURN NEW;
        END $$;
        CREATE TRIGGER guard_account_setup BEFORE INSERT OR UPDATE ON finance.accounts
          FOR EACH ROW EXECUTE FUNCTION finance.guard_account_setup();

        CREATE FUNCTION finance.require_account_creation() RETURNS trigger LANGUAGE plpgsql AS $$
        DECLARE requested audit.entries; created audit.entries;
        BEGIN
          -- Legacy rows have no creator metadata. Runtime-created rows require the command's effects.
          IF current_user='erp_runtime' OR NEW.created_at IS NOT NULL THEN
            SELECT * INTO requested FROM audit.entries WHERE (tenant_id,company_id)=(NEW.tenant_id,NEW.company_id)
              AND entity_type='Account' AND entity_id=NEW.id::text AND action='AccountCreateRequested';
            SELECT * INTO created FROM audit.entries WHERE (tenant_id,company_id)=(NEW.tenant_id,NEW.company_id)
              AND entity_type='Account' AND entity_id=NEW.id::text AND action='AccountCreated';
            IF requested.id IS NULL OR created.id IS NULL
              OR (requested.actor_issuer,requested.actor_subject,requested.request_id,requested.correlation_id)
                  IS DISTINCT FROM (created.actor_issuer,created.actor_subject,created.request_id,created.correlation_id)
              OR (NEW.created_by_issuer,NEW.created_by_subject) IS DISTINCT FROM (created.actor_issuer,created.actor_subject)
              OR (SELECT count(*) FROM audit.entries WHERE (tenant_id,company_id)=(NEW.tenant_id,NEW.company_id)
                  AND entity_type='Account' AND entity_id=NEW.id::text
                  AND action IN ('AccountCreateRequested','AccountCreated'))<>2
              OR (SELECT count(*) FROM integration.outbox WHERE (tenant_id,company_id)=(NEW.tenant_id,NEW.company_id)
                  AND source_id=NEW.id::text AND type='finance.account-created.v1'
                  AND payload->>'accountId'=NEW.id::text AND payload->>'code'=NEW.code
                  AND (payload->>'postingAllowed')::boolean=NEW.posting_allowed)<>1
            THEN RAISE EXCEPTION 'finance.account_creation_effect_missing' USING ERRCODE='23514'; END IF;
          END IF;
          RETURN NEW;
        END $$;
        CREATE CONSTRAINT TRIGGER account_creation_integrity AFTER INSERT ON finance.accounts
          DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION finance.require_account_creation();
        REVOKE ALL ON FUNCTION finance.lock_book_for_account_create(),finance.guard_account_setup(),finance.require_account_creation() FROM PUBLIC;
        GRANT EXECUTE ON FUNCTION finance.lock_book_for_account_create(),finance.guard_account_setup(),finance.require_account_creation() TO erp_runtime;
        GRANT INSERT ON finance.accounts TO erp_runtime;
        """;
}
