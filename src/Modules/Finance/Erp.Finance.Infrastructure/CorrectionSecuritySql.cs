namespace Erp.Finance.Infrastructure;

internal static class CorrectionSecuritySql
{
    public const string V1 = """
        ALTER TABLE finance.journal_corrections ENABLE ROW LEVEL SECURITY;
        ALTER TABLE finance.journal_corrections FORCE ROW LEVEL SECURITY;
        CREATE POLICY company_scope ON finance.journal_corrections USING
          (tenant_id=nullif(current_setting('erp.tenant_id',true),'')::uuid AND company_id=nullif(current_setting('erp.company_id',true),'')::uuid)
          WITH CHECK (tenant_id=nullif(current_setting('erp.tenant_id',true),'')::uuid AND company_id=nullif(current_setting('erp.company_id',true),'')::uuid);
        GRANT SELECT,INSERT ON finance.journal_corrections TO erp_runtime;
        CREATE FUNCTION finance.guard_correction() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN
          IF TG_OP<>'INSERT' THEN RAISE EXCEPTION 'finance.correction_immutable' USING ERRCODE='23514'; END IF;
          PERFORM id FROM finance.journal_entries WHERE (tenant_id,company_id)=(NEW.tenant_id,NEW.company_id)
            AND id IN (NEW.original_journal_id,NEW.reversal_journal_id,NEW.replacement_journal_id) ORDER BY id FOR UPDATE;
          IF EXISTS(SELECT 1 FROM finance.journal_corrections WHERE (tenant_id,company_id)=(NEW.tenant_id,NEW.company_id)
            AND ARRAY[original_journal_id,reversal_journal_id,replacement_journal_id] && ARRAY[NEW.original_journal_id,NEW.reversal_journal_id,NEW.replacement_journal_id])
          THEN RAISE EXCEPTION 'finance.correction_chain_not_allowed' USING ERRCODE='23514'; END IF;
          RETURN NEW;
        END $$;
        CREATE TRIGGER guard_correction BEFORE INSERT OR UPDATE OR DELETE ON finance.journal_corrections FOR EACH ROW EXECUTE FUNCTION finance.guard_correction();
        CREATE FUNCTION finance.require_correction_integrity() RETURNS trigger LANGUAGE plpgsql AS $$
        DECLARE o finance.journal_entries; r finance.journal_entries; p finance.journal_entries;
        BEGIN
          SELECT * INTO o FROM finance.journal_entries WHERE (tenant_id,company_id,id)=(NEW.tenant_id,NEW.company_id,NEW.original_journal_id);
          SELECT * INTO r FROM finance.journal_entries WHERE (tenant_id,company_id,id)=(NEW.tenant_id,NEW.company_id,NEW.reversal_journal_id);
          SELECT * INTO p FROM finance.journal_entries WHERE (tenant_id,company_id,id)=(NEW.tenant_id,NEW.company_id,NEW.replacement_journal_id);
          IF o.status IS DISTINCT FROM 'Posted' OR r.status IS DISTINCT FROM 'Posted' OR p.status IS DISTINCT FROM 'Posted'
            OR o.currency IS DISTINCT FROM r.currency OR o.currency IS DISTINCT FROM p.currency
            OR r.posting_date IS DISTINCT FROM NEW.posting_date OR p.posting_date IS DISTINCT FROM NEW.posting_date
            OR r.posted_by_subject IS DISTINCT FROM NEW.actor_subject OR p.posted_by_subject IS DISTINCT FROM NEW.actor_subject
            OR NOT EXISTS(SELECT 1 FROM finance.journal_reversals WHERE (tenant_id,company_id,original_journal_id,reversal_journal_id)=(NEW.tenant_id,NEW.company_id,o.id,r.id)
              AND request_id=NEW.request_id AND correlation_id=NEW.correlation_id)
            OR EXISTS(SELECT 1 FROM finance.journal_reversals WHERE (tenant_id,company_id)=(NEW.tenant_id,NEW.company_id) AND (original_journal_id=p.id OR reversal_journal_id=p.id))
          THEN RAISE EXCEPTION 'finance.correction_invalid' USING ERRCODE='23514'; END IF;
          IF (SELECT count(DISTINCT action) FROM audit.entries WHERE (tenant_id,company_id)=(NEW.tenant_id,NEW.company_id)
            AND entity_type='ManualJournal' AND entity_id=o.id::text AND action IN ('JournalCorrectionRequested','JournalCorrectionCompleted')
            AND actor_issuer=NEW.actor_issuer AND actor_subject=NEW.actor_subject AND request_id=NEW.request_id AND correlation_id=NEW.correlation_id)<>2
            OR (SELECT count(*) FROM integration.outbox WHERE (tenant_id,company_id)=(NEW.tenant_id,NEW.company_id)
              AND type='finance.journal-corrected.v1' AND source_id=o.id::text AND payload->>'correctionId'=NEW.id::text
              AND payload->>'originalJournalId'=o.id::text AND payload->>'reversalJournalId'=r.id::text AND payload->>'replacementJournalId'=p.id::text)<>1
          THEN RAISE EXCEPTION 'finance.correction_effect_missing' USING ERRCODE='23514'; END IF;
          RETURN NEW;
        END $$;
        CREATE CONSTRAINT TRIGGER correction_integrity AFTER INSERT ON finance.journal_corrections
          DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION finance.require_correction_integrity();
        CREATE FUNCTION finance.reject_reversal_of_corrected_journal() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN
          IF EXISTS(SELECT 1 FROM finance.journal_corrections WHERE (tenant_id,company_id)=(NEW.tenant_id,NEW.company_id)
             AND (original_journal_id=NEW.original_journal_id OR reversal_journal_id=NEW.original_journal_id OR replacement_journal_id=NEW.original_journal_id))
          THEN RAISE EXCEPTION 'finance.correction_chain_not_allowed' USING ERRCODE='23514'; END IF;
          RETURN NEW;
        END $$;
        CREATE TRIGGER reject_reversal_of_corrected_journal BEFORE INSERT ON finance.journal_reversals
          FOR EACH ROW EXECUTE FUNCTION finance.reject_reversal_of_corrected_journal();
        REVOKE ALL ON FUNCTION finance.guard_correction(),finance.require_correction_integrity(),finance.reject_reversal_of_corrected_journal() FROM PUBLIC;
        GRANT EXECUTE ON FUNCTION finance.guard_correction(),finance.require_correction_integrity(),finance.reject_reversal_of_corrected_journal() TO erp_runtime;
        """;
}
