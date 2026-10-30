-- Disposable schema ONLY. Runtime role is not owner, superuser or BYPASSRLS.
DO $$ BEGIN IF NOT EXISTS(SELECT 1 FROM pg_roles WHERE rolname='poc_runtime') THEN
 CREATE ROLE poc_runtime LOGIN PASSWORD 'disposable-poc-only' NOSUPERUSER NOBYPASSRLS; END IF; END $$;
DROP SCHEMA IF EXISTS poc CASCADE;
CREATE SCHEMA poc;
GRANT USAGE ON SCHEMA poc TO poc_runtime;
CREATE TABLE poc.scoped(id uuid PRIMARY KEY,tenant_id uuid NOT NULL,company_id uuid NOT NULL,label text NOT NULL);
CREATE TABLE poc.periods(tenant_id uuid,company_id uuid,id int,closed bool NOT NULL,PRIMARY KEY(tenant_id,company_id,id));
CREATE TABLE poc.accounts(tenant_id uuid,company_id uuid,id text,active bool NOT NULL,PRIMARY KEY(tenant_id,company_id,id));
CREATE TABLE poc.documents(id uuid PRIMARY KEY,tenant_id uuid NOT NULL,company_id uuid NOT NULL,source text NOT NULL,amount numeric(24,6) NOT NULL CHECK(amount>0),state text NOT NULL,UNIQUE(tenant_id,company_id,source),UNIQUE(tenant_id,company_id,id));
CREATE TABLE poc.audit(id uuid PRIMARY KEY,tenant_id uuid NOT NULL,company_id uuid NOT NULL,source text NOT NULL,actor text NOT NULL,event_kind text NOT NULL,rule_version text NOT NULL,UNIQUE(tenant_id,company_id,source));
CREATE TABLE poc.journals(id uuid PRIMARY KEY,tenant_id uuid NOT NULL,company_id uuid NOT NULL,source text NOT NULL,period_id int NOT NULL,UNIQUE(tenant_id,company_id,source),UNIQUE(tenant_id,company_id,id),FOREIGN KEY(tenant_id,company_id,period_id) REFERENCES poc.periods);
CREATE TABLE poc.lines(id uuid PRIMARY KEY,tenant_id uuid NOT NULL,company_id uuid NOT NULL,journal_id uuid NOT NULL,account text NOT NULL,debit numeric(24,6) NOT NULL DEFAULT 0,credit numeric(24,6) NOT NULL DEFAULT 0,CHECK(debit>=0 AND credit>=0 AND ((debit>0 AND credit=0) OR (credit>0 AND debit=0))),FOREIGN KEY(tenant_id,company_id,journal_id) REFERENCES poc.journals(tenant_id,company_id,id),FOREIGN KEY(tenant_id,company_id,account) REFERENCES poc.accounts);
CREATE TABLE poc.outbox(id uuid PRIMARY KEY,tenant_id uuid NOT NULL,company_id uuid NOT NULL,source text NOT NULL,kind text NOT NULL,done bool NOT NULL DEFAULT false,lease_id uuid,lease_until timestamptz,attempts int NOT NULL DEFAULT 0,UNIQUE(tenant_id,company_id,source,kind));
CREATE TABLE poc.inbox(tenant_id uuid,company_id uuid,consumer text,event_id uuid,payload_hash text NOT NULL,PRIMARY KEY(tenant_id,company_id,consumer,event_id));
CREATE TABLE poc.consumer_effects(tenant_id uuid,company_id uuid,event_id uuid,amount int NOT NULL,PRIMARY KEY(tenant_id,company_id,event_id));
CREATE TABLE poc.idempotency(tenant_id uuid,company_id uuid,caller text,command text,target text,key text,fingerprint text NOT NULL,result_id uuid NOT NULL,PRIMARY KEY(tenant_id,company_id,caller,command,target,key));
CREATE TABLE poc.stock(tenant_id uuid,company_id uuid,product text,on_hand numeric(24,8) NOT NULL,reserved numeric(24,8) NOT NULL DEFAULT 0,value numeric(24,6) NOT NULL,revision bigint NOT NULL DEFAULT 0,PRIMARY KEY(tenant_id,company_id,product),CHECK(on_hand>=0 AND reserved>=0 AND reserved<=on_hand));
CREATE TABLE poc.reservations(id uuid PRIMARY KEY,tenant_id uuid NOT NULL,company_id uuid NOT NULL,product text NOT NULL,quantity numeric(24,8) NOT NULL CHECK(quantity<>0),FOREIGN KEY(tenant_id,company_id,product) REFERENCES poc.stock);
CREATE TABLE poc.stock_ledger(id uuid PRIMARY KEY,tenant_id uuid NOT NULL,company_id uuid NOT NULL,product text NOT NULL,source text NOT NULL,qty numeric(24,8) NOT NULL,value numeric(24,6) NOT NULL,sequence bigint NOT NULL,UNIQUE(tenant_id,company_id,product,source),UNIQUE(tenant_id,company_id,id),FOREIGN KEY(tenant_id,company_id,product) REFERENCES poc.stock);
CREATE TABLE poc.valuation_ledger(id uuid PRIMARY KEY,tenant_id uuid NOT NULL,company_id uuid NOT NULL,movement_id uuid NOT NULL,delta numeric(24,6) NOT NULL,FOREIGN KEY(tenant_id,company_id,movement_id) REFERENCES poc.stock_ledger(tenant_id,company_id,id));
CREATE TABLE poc.open_items(id uuid PRIMARY KEY,tenant_id uuid NOT NULL,company_id uuid NOT NULL,original numeric(24,6) NOT NULL,UNIQUE(tenant_id,company_id,id));
CREATE TABLE poc.allocations(id uuid PRIMARY KEY,tenant_id uuid NOT NULL,company_id uuid NOT NULL,item_id uuid NOT NULL,amount numeric(24,6) NOT NULL CHECK(amount>0),FOREIGN KEY(tenant_id,company_id,item_id) REFERENCES poc.open_items(tenant_id,company_id,id));
CREATE TABLE poc.wip(id uuid PRIMARY KEY,tenant_id uuid NOT NULL,company_id uuid NOT NULL,policy text NOT NULL,source text NOT NULL,delta numeric(24,6) NOT NULL,UNIQUE(tenant_id,company_id,policy,source));
CREATE TABLE poc.migration_demo(id int PRIMARY KEY,tenant_id uuid NOT NULL,company_id uuid NOT NULL,old_name text NOT NULL);
CREATE FUNCTION poc.check_journal() RETURNS trigger LANGUAGE plpgsql AS $$
DECLARE j uuid; t uuid; c uuid; n int; d numeric; cr numeric;
BEGIN
 IF TG_TABLE_NAME='journals' THEN j:=NEW.id; ELSE j:=NEW.journal_id; END IF;
 t:=NEW.tenant_id;c:=NEW.company_id;
 SELECT count(*),coalesce(sum(debit),0),coalesce(sum(credit),0) INTO n,d,cr FROM poc.lines WHERE tenant_id=t AND company_id=c AND journal_id=j;
 IF n<2 OR d<>cr THEN RAISE EXCEPTION 'journal.unbalanced' USING ERRCODE='23514'; END IF;
 RETURN NEW;
END $$;
CREATE CONSTRAINT TRIGGER balanced_header AFTER INSERT ON poc.journals DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION poc.check_journal();
CREATE CONSTRAINT TRIGGER balanced_lines AFTER INSERT ON poc.lines DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION poc.check_journal();
CREATE FUNCTION poc.required_effects() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN
 IF NOT EXISTS(SELECT 1 FROM poc.journals WHERE tenant_id=NEW.tenant_id AND company_id=NEW.company_id AND source=NEW.source)
 OR NOT EXISTS(SELECT 1 FROM poc.open_items WHERE tenant_id=NEW.tenant_id AND company_id=NEW.company_id AND id=NEW.id AND original=NEW.amount)
 OR NOT EXISTS(SELECT 1 FROM poc.outbox WHERE tenant_id=NEW.tenant_id AND company_id=NEW.company_id AND source=NEW.source)
 OR NOT EXISTS(SELECT 1 FROM poc.audit WHERE tenant_id=NEW.tenant_id AND company_id=NEW.company_id AND source=NEW.source)
 THEN RAISE EXCEPTION 'posting.required_effect_missing' USING ERRCODE='23514'; END IF;RETURN NEW;END $$;
CREATE CONSTRAINT TRIGGER required_posting AFTER INSERT ON poc.documents DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION poc.required_effects();
CREATE FUNCTION poc.immutable() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION 'posted.immutable' USING ERRCODE='23514'; END $$;
DO $$ DECLARE tab text; BEGIN
 FOREACH tab IN ARRAY ARRAY['journals','lines','stock_ledger','valuation_ledger','wip','audit'] LOOP
  EXECUTE format('CREATE TRIGGER immutable BEFORE UPDATE OR DELETE ON poc.%I FOR EACH ROW EXECUTE FUNCTION poc.immutable()',tab);
 END LOOP;
 FOR tab IN SELECT tablename FROM pg_tables WHERE schemaname='poc' LOOP
  EXECUTE format('ALTER TABLE poc.%I ENABLE ROW LEVEL SECURITY',tab);
  EXECUTE format('ALTER TABLE poc.%I FORCE ROW LEVEL SECURITY',tab);
  EXECUTE format('CREATE POLICY scope ON poc.%I USING (tenant_id=nullif(current_setting(''app.tenant'',true),'''')::uuid AND company_id=nullif(current_setting(''app.company'',true),'''')::uuid) WITH CHECK (tenant_id=nullif(current_setting(''app.tenant'',true),'''')::uuid AND company_id=nullif(current_setting(''app.company'',true),'''')::uuid)',tab);
 END LOOP;
END $$;
GRANT SELECT,INSERT,UPDATE,DELETE ON ALL TABLES IN SCHEMA poc TO poc_runtime;
REVOKE UPDATE,DELETE ON poc.journals,poc.lines,poc.stock_ledger,poc.valuation_ledger,poc.wip,poc.audit FROM poc_runtime;
