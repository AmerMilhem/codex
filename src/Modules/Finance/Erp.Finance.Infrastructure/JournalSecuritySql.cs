namespace Erp.Finance.Infrastructure;

// Frozen migration text. Later security changes require a new migration.
internal static class JournalSecuritySql
{
    public const string V1 = """
        REVOKE ALL ON SCHEMA finance FROM PUBLIC;
        GRANT USAGE ON SCHEMA finance TO erp_runtime;
        DO $$ DECLARE item text; BEGIN
          FOREACH item IN ARRAY ARRAY['accounting_books','accounts','fiscal_periods','journal_entries','journal_lines','journal_numbers'] LOOP
            EXECUTE format('ALTER TABLE finance.%I ENABLE ROW LEVEL SECURITY',item);
            EXECUTE format('ALTER TABLE finance.%I FORCE ROW LEVEL SECURITY',item);
            EXECUTE format('CREATE POLICY company_scope ON finance.%I USING
              (tenant_id=nullif(current_setting(''erp.tenant_id'',true),'''')::uuid AND company_id=nullif(current_setting(''erp.company_id'',true),'''')::uuid)
              WITH CHECK (tenant_id=nullif(current_setting(''erp.tenant_id'',true),'''')::uuid AND company_id=nullif(current_setting(''erp.company_id'',true),'''')::uuid)',item);
          END LOOP;
        END $$;
        GRANT SELECT ON finance.accounting_books,finance.accounts,finance.fiscal_periods TO erp_runtime;
        GRANT SELECT,INSERT,UPDATE ON finance.journal_entries,finance.journal_numbers TO erp_runtime;
        GRANT SELECT,INSERT,UPDATE,DELETE ON finance.journal_lines TO erp_runtime;
        GRANT SELECT ON public."__FinanceMigrationsHistory" TO erp_runtime;

        -- PostgreSQL row locks require UPDATE privilege. Narrow, fixed-search-path lock functions
        -- let runtime keep read-only reference data grants. Owner remains subject to FORCE RLS;
        -- explicit scope predicates also apply. No dynamic SQL or user-selected object names.
        CREATE FUNCTION finance.lock_book() RETURNS SETOF finance.accounting_books LANGUAGE sql SECURITY DEFINER SET search_path=pg_catalog AS $$
          SELECT * FROM finance.accounting_books WHERE tenant_id=nullif(current_setting('erp.tenant_id',true),'')::uuid
            AND company_id=nullif(current_setting('erp.company_id',true),'')::uuid FOR SHARE
        $$;
        CREATE FUNCTION finance.lock_periods(d date) RETURNS SETOF finance.fiscal_periods LANGUAGE sql SECURITY DEFINER SET search_path=pg_catalog AS $$
          SELECT * FROM finance.fiscal_periods WHERE tenant_id=nullif(current_setting('erp.tenant_id',true),'')::uuid
            AND company_id=nullif(current_setting('erp.company_id',true),'')::uuid AND d BETWEEN starts_on AND ends_on ORDER BY id FOR SHARE
        $$;
        CREATE FUNCTION finance.lock_accounts(ids uuid[]) RETURNS SETOF finance.accounts LANGUAGE sql SECURITY DEFINER SET search_path=pg_catalog AS $$
          SELECT * FROM finance.accounts WHERE tenant_id=nullif(current_setting('erp.tenant_id',true),'')::uuid
            AND company_id=nullif(current_setting('erp.company_id',true),'')::uuid AND id=ANY(ids) ORDER BY id FOR SHARE
        $$;

        CREATE FUNCTION finance.guard_journal() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN
          IF TG_OP='DELETE' THEN RAISE EXCEPTION 'finance.journal_immutable' USING ERRCODE='23514'; END IF;
          IF TG_OP='INSERT' THEN
            IF NEW.status<>'Draft' OR NEW.revision<>1 THEN RAISE EXCEPTION 'finance.draft_required' USING ERRCODE='23514'; END IF;
          ELSE
            IF OLD.status='Posted' THEN RAISE EXCEPTION 'finance.journal_immutable' USING ERRCODE='23514'; END IF;
            IF (NEW.id,NEW.tenant_id,NEW.company_id,NEW.created_at,NEW.rounding_policy) IS DISTINCT FROM
               (OLD.id,OLD.tenant_id,OLD.company_id,OLD.created_at,OLD.rounding_policy) OR NEW.revision<>OLD.revision+1
            THEN RAISE EXCEPTION 'finance.journal_revision' USING ERRCODE='23514'; END IF;
            IF NEW.status='Posted' AND (NEW.posting_date,NEW.currency,NEW.reference,NEW.description) IS DISTINCT FROM
               (OLD.posting_date,OLD.currency,OLD.reference,OLD.description)
            THEN RAISE EXCEPTION 'finance.post_requires_saved_draft' USING ERRCODE='23514'; END IF;
          END IF;
          IF NEW.rounding_policy<>'internal-to-even-v1' THEN RAISE EXCEPTION 'finance.rounding_policy' USING ERRCODE='23514'; END IF;
          RETURN NEW;
        END $$;
        CREATE TRIGGER guard_journal BEFORE INSERT OR UPDATE OR DELETE ON finance.journal_entries FOR EACH ROW EXECUTE FUNCTION finance.guard_journal();

        CREATE FUNCTION finance.guard_line() RETURNS trigger LANGUAGE plpgsql AS $$ DECLARE parent finance.journal_entries; BEGIN
          IF TG_OP='UPDATE' AND (NEW.id,NEW.tenant_id,NEW.company_id,NEW.journal_id) IS DISTINCT FROM (OLD.id,OLD.tenant_id,OLD.company_id,OLD.journal_id)
            THEN RAISE EXCEPTION 'finance.line_identity_immutable' USING ERRCODE='23514'; END IF;
          IF TG_OP='DELETE' THEN
            SELECT * INTO parent FROM finance.journal_entries WHERE (tenant_id,company_id,id)=(OLD.tenant_id,OLD.company_id,OLD.journal_id) FOR UPDATE;
          ELSE
            SELECT * INTO parent FROM finance.journal_entries WHERE (tenant_id,company_id,id)=(NEW.tenant_id,NEW.company_id,NEW.journal_id) FOR UPDATE;
          END IF;
          IF parent.id IS NULL OR parent.status<>'Draft' THEN RAISE EXCEPTION 'finance.journal_immutable' USING ERRCODE='23514'; END IF;
          IF TG_OP='DELETE' THEN RETURN OLD; END IF; RETURN NEW;
        END $$;
        CREATE TRIGGER guard_line BEFORE INSERT OR UPDATE OR DELETE ON finance.journal_lines FOR EACH ROW EXECUTE FUNCTION finance.guard_line();

        CREATE FUNCTION finance.require_posting_integrity() RETURNS trigger LANGUAGE plpgsql AS $$
        DECLARE j finance.journal_entries; p finance.fiscal_periods; b finance.accounting_books;
          line_count bigint; debit_total numeric; credit_total numeric; invalid_count bigint; a finance.accounts;
        BEGIN
          SELECT * INTO j FROM finance.journal_entries WHERE (tenant_id,company_id,id)=(NEW.tenant_id,NEW.company_id,NEW.id);
          IF j.status<>'Posted' THEN RETURN NEW; END IF;
          SELECT * INTO b FROM finance.lock_book();
          SELECT * INTO p FROM finance.lock_periods(j.posting_date) WHERE id=j.fiscal_period_id;
          IF b.currency IS DISTINCT FROM j.currency OR p.id IS NULL OR p.status<>'Open' OR p.fiscal_year<>j.fiscal_year
            OR j.posting_date NOT BETWEEN p.starts_on AND p.ends_on
            OR (SELECT count(*) FROM finance.fiscal_periods WHERE tenant_id=j.tenant_id AND company_id=j.company_id AND j.posting_date BETWEEN starts_on AND ends_on)<>1
          THEN RAISE EXCEPTION 'finance.posting_context_invalid' USING ERRCODE='23514'; END IF;
          FOR a IN SELECT * FROM finance.lock_accounts(ARRAY(SELECT account_id FROM finance.journal_lines WHERE (tenant_id,company_id,journal_id)=(j.tenant_id,j.company_id,j.id))) LOOP
            IF a.status<>'Active' OR NOT a.posting_allowed THEN RAISE EXCEPTION 'finance.account_invalid' USING ERRCODE='23514'; END IF;
          END LOOP;
          SELECT count(*),coalesce(sum(debit),0),coalesce(sum(credit),0),
            count(*) FILTER(WHERE NOT ((debit>0 AND credit=0) OR (credit>0 AND debit=0)) OR debit<>round(debit,b.minor_units) OR credit<>round(credit,b.minor_units))
            INTO line_count,debit_total,credit_total,invalid_count FROM finance.journal_lines
            WHERE (tenant_id,company_id,journal_id)=(j.tenant_id,j.company_id,j.id);
          IF line_count<2 OR line_count>200 OR invalid_count>0 OR debit_total<>credit_total OR debit_total>=1000000000000000000
            THEN RAISE EXCEPTION 'finance.journal_unbalanced_or_invalid' USING ERRCODE='23514'; END IF;
          IF j.journal_number<>('MJ-'||j.fiscal_year::text||'-'||CASE WHEN length(j.number_sequence::text)<6 THEN lpad(j.number_sequence::text,6,'0') ELSE j.number_sequence::text END)
            THEN RAISE EXCEPTION 'finance.journal_number_invalid' USING ERRCODE='23514'; END IF;
          IF NOT EXISTS(SELECT 1 FROM audit.entries WHERE (tenant_id,company_id)=(j.tenant_id,j.company_id)
              AND entity_type='ManualJournal' AND entity_id=j.id::text AND action='JournalPosted')
            OR (SELECT count(*) FROM integration.outbox WHERE (tenant_id,company_id)=(j.tenant_id,j.company_id)
              AND source_id=j.id::text AND type='finance.journal-entry-posted.v1')<>1
          THEN RAISE EXCEPTION 'finance.posting_effect_missing' USING ERRCODE='23514'; END IF;
          RETURN NEW;
        END $$;
        CREATE CONSTRAINT TRIGGER posting_integrity AFTER INSERT OR UPDATE ON finance.journal_entries
          DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION finance.require_posting_integrity();

        CREATE FUNCTION finance.guard_book() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN
          IF EXISTS(SELECT 1 FROM finance.journal_entries WHERE (tenant_id,company_id)=(OLD.tenant_id,OLD.company_id))
            AND (NEW.tenant_id,NEW.company_id,NEW.currency,NEW.minor_units) IS DISTINCT FROM (OLD.tenant_id,OLD.company_id,OLD.currency,OLD.minor_units)
          THEN RAISE EXCEPTION 'finance.book_policy_in_use' USING ERRCODE='23514'; END IF; RETURN NEW;
        END $$;
        CREATE TRIGGER guard_book BEFORE UPDATE ON finance.accounting_books FOR EACH ROW EXECUTE FUNCTION finance.guard_book();
        CREATE FUNCTION finance.guard_period() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN
          IF EXISTS(SELECT 1 FROM finance.journal_entries WHERE (tenant_id,company_id,fiscal_period_id)=(OLD.tenant_id,OLD.company_id,OLD.id) AND status='Posted')
            AND (NEW.id,NEW.tenant_id,NEW.company_id,NEW.starts_on,NEW.ends_on,NEW.fiscal_year) IS DISTINCT FROM (OLD.id,OLD.tenant_id,OLD.company_id,OLD.starts_on,OLD.ends_on,OLD.fiscal_year)
          THEN RAISE EXCEPTION 'finance.period_history_immutable' USING ERRCODE='23514'; END IF; RETURN NEW;
        END $$;
        CREATE TRIGGER guard_period BEFORE UPDATE ON finance.fiscal_periods FOR EACH ROW EXECUTE FUNCTION finance.guard_period();
        REVOKE ALL ON ALL FUNCTIONS IN SCHEMA finance FROM PUBLIC;
        GRANT EXECUTE ON ALL FUNCTIONS IN SCHEMA finance TO erp_runtime;
        """;
}
