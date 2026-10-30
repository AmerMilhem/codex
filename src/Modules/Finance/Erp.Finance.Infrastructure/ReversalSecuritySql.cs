namespace Erp.Finance.Infrastructure;
// Frozen V1 migration text; later changes require a new migration.
internal static class ReversalSecuritySql
{
    public const string V1 = """
        ALTER TABLE finance.journal_reversals ENABLE ROW LEVEL SECURITY;
        ALTER TABLE finance.journal_reversals FORCE ROW LEVEL SECURITY;
        CREATE POLICY company_scope ON finance.journal_reversals USING
          (tenant_id=nullif(current_setting('erp.tenant_id',true),'')::uuid AND company_id=nullif(current_setting('erp.company_id',true),'')::uuid)
          WITH CHECK (tenant_id=nullif(current_setting('erp.tenant_id',true),'')::uuid AND company_id=nullif(current_setting('erp.company_id',true),'')::uuid);
        GRANT SELECT,INSERT ON finance.journal_reversals TO erp_runtime;
        CREATE FUNCTION finance.guard_reversal() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN
          IF TG_OP<>'INSERT' THEN RAISE EXCEPTION 'finance.reversal_immutable' USING ERRCODE='23514'; END IF;
          PERFORM id FROM finance.journal_entries WHERE (tenant_id,company_id)=(NEW.tenant_id,NEW.company_id)
            AND id IN (NEW.original_journal_id,NEW.reversal_journal_id) ORDER BY id FOR UPDATE;
          IF EXISTS(SELECT 1 FROM finance.journal_reversals WHERE (tenant_id,company_id)=(NEW.tenant_id,NEW.company_id)
            AND (reversal_journal_id=NEW.original_journal_id OR original_journal_id=NEW.reversal_journal_id))
          THEN RAISE EXCEPTION 'finance.reversal_chain_not_allowed' USING ERRCODE='23514'; END IF;
          RETURN NEW;
        END $$;
        CREATE TRIGGER guard_reversal BEFORE INSERT OR UPDATE OR DELETE ON finance.journal_reversals FOR EACH ROW EXECUTE FUNCTION finance.guard_reversal();
        CREATE FUNCTION finance.require_reversal_integrity() RETURNS trigger LANGUAGE plpgsql AS $$
        DECLARE o finance.journal_entries; r finance.journal_entries;
        BEGIN
          SELECT * INTO o FROM finance.journal_entries WHERE (tenant_id,company_id,id)=(NEW.tenant_id,NEW.company_id,NEW.original_journal_id);
          SELECT * INTO r FROM finance.journal_entries WHERE (tenant_id,company_id,id)=(NEW.tenant_id,NEW.company_id,NEW.reversal_journal_id);
          IF o.status IS DISTINCT FROM 'Posted' OR r.status IS DISTINCT FROM 'Posted' OR o.currency IS DISTINCT FROM r.currency
            OR r.posting_date IS DISTINCT FROM NEW.posting_date OR r.id=o.id OR r.posted_by_issuer<>NEW.actor_issuer OR r.posted_by_subject<>NEW.actor_subject
          THEN RAISE EXCEPTION 'finance.reversal_invalid' USING ERRCODE='23514'; END IF;
          IF EXISTS(
            (SELECT position,account_id,credit,debit,description FROM finance.journal_lines WHERE (tenant_id,company_id,journal_id)=(o.tenant_id,o.company_id,o.id)
             EXCEPT ALL SELECT position,account_id,debit,credit,description FROM finance.journal_lines WHERE (tenant_id,company_id,journal_id)=(r.tenant_id,r.company_id,r.id))
            UNION ALL
            (SELECT position,account_id,debit,credit,description FROM finance.journal_lines WHERE (tenant_id,company_id,journal_id)=(r.tenant_id,r.company_id,r.id)
             EXCEPT ALL SELECT position,account_id,credit,debit,description FROM finance.journal_lines WHERE (tenant_id,company_id,journal_id)=(o.tenant_id,o.company_id,o.id)))
          THEN RAISE EXCEPTION 'finance.reversal_amount_mismatch' USING ERRCODE='23514'; END IF;
          IF (SELECT count(DISTINCT action) FROM audit.entries WHERE (tenant_id,company_id)=(o.tenant_id,o.company_id)
             AND entity_type='ManualJournal' AND entity_id=o.id::text AND action IN ('JournalReversalRequested','JournalReversed')
             AND actor_issuer=NEW.actor_issuer AND actor_subject=NEW.actor_subject AND request_id=NEW.request_id AND correlation_id=NEW.correlation_id)<>2
            OR (SELECT count(*) FROM integration.outbox WHERE (tenant_id,company_id)=(o.tenant_id,o.company_id)
             AND source_id=o.id::text AND type='finance.journal-entry-reversed.v1' AND payload->>'originalJournalId'=o.id::text
             AND payload->>'reversalJournalId'=r.id::text AND payload->>'postingDate'=r.posting_date::text)<>1
          THEN RAISE EXCEPTION 'finance.reversal_effect_missing' USING ERRCODE='23514'; END IF;
          RETURN NEW;
        END $$;
        CREATE CONSTRAINT TRIGGER reversal_integrity AFTER INSERT ON finance.journal_reversals
          DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION finance.require_reversal_integrity();
        REVOKE ALL ON FUNCTION finance.guard_reversal(),finance.require_reversal_integrity() FROM PUBLIC;
        GRANT EXECUTE ON FUNCTION finance.guard_reversal(),finance.require_reversal_integrity() TO erp_runtime;
        """;
}
