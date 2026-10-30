# Open UX decisions

1. Confirm whether a demo identity selector belongs in the Figma walkthrough only or the eventual real sign-in flow. The current login screen is explicitly a prototype identity screen.
2. Confirm the exact finance dashboard API and refresh cadence before treating its KPI cards as live. Cards currently use internally consistent demo values.
3. Decide whether account search/status filtering and global search/notifications are in a later implementation scope. They are visually labeled as proposed/soon, not claimed as working today.
4. Confirm the preferred behavior for an empty company before its first accounting book exists. The setup drawer shows the intended create action, while the clickable golden route shows an existing `PRIMARY` book.
5. Confirm the final JOD display convention and localized digits before production implementation. This handoff keeps Latin digits and three decimal places in both directions for legibility and consistency with the existing demo.

Manual Journal Correction remains paused by the latest instruction and has no completed correction screen in this design.
