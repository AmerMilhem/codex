namespace Erp.Platform.Infrastructure.Persistence;

// Immutable migration V1 text. Future policy changes require another reviewed migration.
internal static class SecuritySql
{
    public const string Version1 = """
        REVOKE ALL ON SCHEMA audit,integration FROM PUBLIC;
        GRANT USAGE ON SCHEMA audit,integration TO erp_runtime;
        DO $$ DECLARE item text; BEGIN
          FOREACH item IN ARRAY ARRAY['integration.idempotency','integration.outbox','integration.inbox','audit.entries'] LOOP
            EXECUTE format('ALTER TABLE %s ENABLE ROW LEVEL SECURITY',item);
            EXECUTE format('ALTER TABLE %s FORCE ROW LEVEL SECURITY',item);
            EXECUTE format('CREATE POLICY company_scope ON %s USING
              (tenant_id=nullif(current_setting(''erp.tenant_id'',true),'''')::uuid
               AND company_id=nullif(current_setting(''erp.company_id'',true),'''')::uuid)
              WITH CHECK (tenant_id=nullif(current_setting(''erp.tenant_id'',true),'''')::uuid
               AND company_id=nullif(current_setting(''erp.company_id'',true),'''')::uuid)',item);
          END LOOP;
        END $$;
        GRANT SELECT,INSERT,UPDATE ON ALL TABLES IN SCHEMA integration TO erp_runtime;
        GRANT SELECT,INSERT ON audit.entries TO erp_runtime;
        GRANT SELECT ON public."__EFMigrationsHistory" TO erp_runtime;
        CREATE FUNCTION audit.reject_mutation() RETURNS trigger LANGUAGE plpgsql AS $$
          BEGIN RAISE EXCEPTION 'audit.immutable' USING ERRCODE='23514'; END $$;
        REVOKE ALL ON FUNCTION audit.reject_mutation() FROM PUBLIC;
        GRANT EXECUTE ON FUNCTION audit.reject_mutation() TO erp_runtime;
        CREATE TRIGGER immutable_audit BEFORE UPDATE OR DELETE ON audit.entries
          FOR EACH ROW EXECUTE FUNCTION audit.reject_mutation();
        CREATE FUNCTION integration.require_completed_receipt() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN
          IF EXISTS(SELECT 1 FROM integration.idempotency
             WHERE tenant_id=NEW.tenant_id AND company_id=NEW.company_id AND caller=NEW.caller
             AND command=NEW.command AND target=NEW.target AND key=NEW.key AND state<>'Completed')
          THEN RAISE EXCEPTION 'idempotency.incomplete_receipt' USING ERRCODE='23514'; END IF;
          RETURN NEW;
        END $$;
        REVOKE ALL ON FUNCTION integration.require_completed_receipt() FROM PUBLIC;
        GRANT EXECUTE ON FUNCTION integration.require_completed_receipt() TO erp_runtime;
        CREATE CONSTRAINT TRIGGER completed_receipt AFTER INSERT OR UPDATE ON integration.idempotency
          DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION integration.require_completed_receipt();
        """;
}
