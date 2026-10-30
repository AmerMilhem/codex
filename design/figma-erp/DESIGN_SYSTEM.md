# Design system and developer notes

## Foundations

| Token | Value | Purpose |
| --- | --- | --- |
| Navy | `#102235` | Persistent navigation |
| Navy 2 | `#19334B` | Active navigation and report total row |
| Teal | `#087F78` | Primary actions and balanced state |
| Teal dark | `#046960` | Text on light success surfaces |
| Canvas | `#F3F6F8` | Workspace surface |
| White | `#FFFFFF` | Cards and controls |
| Ink | `#172A3A` | Primary text |
| Muted | `#607383` | Secondary text |
| Line | `#D7E0E7` | Field and table dividers |
| Amber | `#A96709` | Pending/attention |
| Red | `#B64143` | Error/destructive |
| Blue | `#286DA8` | Information/focus |

English uses **Inter**; Arabic uses **Noto Sans Arabic** with Arial fallback. Scale: 11, 12, 14, 18, 26, and 32 px. Use tabular numerals for finance columns. Spacing follows an 8 px grid with 4, 8, 12, 16, 24, and 32 px tokens. Radii: 6, 8, 10, and 12 px. Elevation is restrained: borders first; subtle shadow only for dialogs, drawers, and overlays. Icon direction must be inspected in RTL: navigation arrows mirror, while semantic glyphs (currency, plus, check, warning) retain meaning.

Desktop master frame is 1440 × 1024 with a 240 px sidebar, 72 px header, and 32 px content gutters. At 1280, keep numeric columns intact, allow data-grid horizontal scroll, and reduce low-priority whitespace. At 1920, cap reading/form width and distribute extra width to tables rather than stretching controls. Mobile is out of scope. The generator currently creates fixed 1440 px frames; responsive behavior is a handoff rule for the later Angular phase, not a tested Figma resize variant.

The financial display example is `100.000 JOD` (three decimal places for the JOD demo). Right-align numeric columns in both writing directions and keep numbers in LTR order inside Arabic text. Use the product's existing decimal/currency policy for implementation; UI text is never the source of accounting precision. `Debit`, `Credit`, and `Difference` remain separate fields. Balanced is teal, draft/pending amber, error red, and neutral information blue. Pair status color with text; do not rely on color alone.

Focus ring: 2 px blue, visible against light and dark surfaces. Target contrast is 4.5:1 for normal text, 3:1 for large text and non-text controls. A disabled control must have a visible disabled label and be excluded from keyboard interaction. Field errors use inline text and an announced error summary. Loading, empty, and reconciliation-failure states retain the report heading and current filters.

## Components

The **03 — Components** page contains 27 Figma component definitions with Auto Layout: four button variants in a component set, plus inputs, numeric/date controls, select, account lookup, checkbox, toggle, tabs, badges, cards, KPI cards, table, grid, pagination, breadcrumb, dialog, drawer, toast, tooltip, empty/loading states, sidebar item, and header item. These are visual reference components. Screen objects are generated separately rather than as linked component instances, so instance-level updates across screens still need a real Figma refinement pass.

Buttons: primary for the next financial action, secondary for navigation and validation, destructive for close confirmation, disabled when unauthorized. Journal entry should support Tab/Shift+Tab between fields, Enter to choose a lookup result, and an explicit add/remove row action. Server validation remains authoritative; display inline field errors and aggregate posting errors without silently clearing entered lines. A posted journal is read-only and offers traceability rather than edit controls. Reporting links go from Trial Balance account to General Ledger, then journal number to posted details.

The Arabic screens are authored separately, with the sidebar on the right and mirrored field/table sequence. Do not flip financial digits or currency codes. The English screens use the same palette, metrics, and component anatomy with left-side navigation.
