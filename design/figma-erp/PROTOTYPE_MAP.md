# Interactive prototype map

Start on **10 — Prototype**. The same route exists in English (`EN / …`) and Arabic (`AR / …`):

`login → dashboard → book → periods → accounts → journalDraft → journalValid → journalPosted → trialBalance → generalLedger → postedDetail → dashboard`

The journal path uses **Validate** then **Post**. The report path uses the account drilldown and the journal drilldown. The sidebar links to Finance screens, and the header language control switches to the corresponding screen in the other language. The return to Dashboard uses sidebar navigation. Actions in this prototype navigate between fixed example states; they do not mutate live accounting data.

Demo scope: Jordan Demo Manufacturing Co., one `PRIMARY` accounting book in JOD, six accounts (`1100`, `1200`, `2100`, `4100`, `5100`, `6100`), and journal `MJ-2026-000001` on 2026-10-14. The journal debits `6100 Office Expense` 100.000 JOD and credits `1100 Cash` 100.000 JOD. Trial Balance shows matching period and closing debit/credit of 100.000 JOD; General Ledger shows the expense account's 100.000 JOD debit movement and running balance.

The Finance Setup page also has separate create-book, create-account, and create-period drawer designs and a period-close confirmation design. These are inspection states, not a simulated persistence flow. Search, notifications, and future modules are marked as forthcoming. The standalone accounting book screen reflects the current one-book-per-company V1 policy.
