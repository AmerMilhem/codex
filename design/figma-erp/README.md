# NEXORA ERP Finance — Figma handoff

This directory contains a local Figma development-plugin generator for the requested Finance design. It creates 14 ordered pages, a design-system inventory, 24 linked 1440 × 1024 desktop screens (12 English, 12 Arabic), and separate English and Arabic inspection pages. It changes no backend or Angular code.

## Open in Figma

1. Create an empty Figma **design** file.
2. In Figma, use **Plugins → Development → Import plugin from manifest…** and select [`manifest.json`](./manifest.json). If Figma asks for a development-plugin ID, assign the ID in the manifest to your local plugin registration.
3. Run **ERP Finance V1 Design Generator** from **Plugins → Development**. The plugin creates the pages in the current file. Run it once in an empty file because it appends pages.
4. Open **10 — Prototype**, select **EN / login** or **AR / login**, then use **Present**. The click route is in [`PROTOTYPE_MAP.md`](./PROTOTYPE_MAP.md).

The source is [`code.js`](./code.js). It needs only the local plugin API and installed Inter and Noto Sans Arabic fonts; it falls back to Arial for Arabic when Noto Sans Arabic is unavailable. No network or imported assets are used.

## Local verification

Run `node --check design/figma-erp/code.js` and `node design/figma-erp/smoke-test.cjs` from the repository root. The smoke test mocks the Figma API, checks ordered pages, frame count, component count, demo data, and every required route transition for both languages. It exports five SVG visual previews. `node design/figma-erp/render-previews.mjs` renders these to PNG with Playwright and a local Chromium install. This is **structural validation, not a real Figma execution or usability test**.

The Figma connector was unavailable in this environment, so no file was created in the user's Figma workspace and no live Figma link can be supplied. Actual plugin import, rendered fonts, click behavior, contrast, focus behavior, and responsive resizing must be checked in Figma before approval. The SVG/PNG previews are evidence of the generated layouts only.

The current prototype depicts the approved Finance capabilities: accounting book setup (one per company), account and fiscal-period setup, manual journals, Trial Balance, General Ledger, reversal trace placeholder, and period close. Manual Journal Correction is paused and is not presented as completed. Global search, notifications, account search/filter, and keyboard-driven data-grid editing are proposed interface treatments; they are marked as future/illustrative and need implementation later. Sales, Inventory, Procurement, Manufacturing, HR, and Payroll appear as **Coming Soon** only.

See [`DESIGN_SYSTEM.md`](./DESIGN_SYSTEM.md) for developer tokens and interaction guidance and [`OPEN_UX_DECISIONS.md`](./OPEN_UX_DECISIONS.md) for questions to resolve before Angular implementation.
