# NAMA design rules

A dark, calm, professional finance app (benchmarks: Mercury, the Stripe dashboard, IBM Carbon Gray). **Dark theme only** (replaces UX-05), near-black and monochrome: buttons and highlights are neutral gray, never blue. Color is used only for status (ok, warn, danger). Every screen built from M3 onwards follows this page. If a screen needs something this page doesn't cover, ask before inventing it.

## Tokens

The tokens are defined once in `web/src/styles.scss`, the only file with color values. Components use `var(--token)` and never hex.

Exceptions, because these files cannot read CSS variables:
- `web/public/google-g.svg`: the Google brand colors.
- The PWA files (UX-06): `web/public/manifest.webmanifest` and the `theme-color` meta in `web/src/index.html` use `--bg` (`#0a0a0b`) and `--surface` (`#121214`). The app icons (`web/icons/*.svg`, rendered to `web/public/icons/*.png`, also the NAMA mark) and the tab icon (`web/public/favicon.svg`, rendered to `favicon.ico`) use `--primary` (`#e4e4e7`) and `--bg`. So does the sign-in artwork (`web/public/sign-in-bg.svg`). If a token changes, change them too.

| Token | Use |
| --- | --- |
| `--bg` | Page background |
| `--surface` | Cards, sidebar, bottom bar, top bar, dialogs, menus, secondary buttons |
| `--surface-2` | Hover, inputs, table row hover |
| `--border` | Card, input and button borders, bar edges |
| `--divider` | Lines inside cards and tables |
| `--track` | Progress bar track |
| `--text` | Main text (never pure white) |
| `--text-2` | Secondary text, icons |
| `--text-muted` | Labels, captions, table headers |
| `--primary` | **Filled buttons only** (neutral light gray, no blue), with `--on-primary` dark text |
| `--primary-text` | Links (always underlined, since they are not colored), active nav, focus ring |
| `--primary-soft` | Active nav background, "In progress" chip |
| `--ok` / `--ok-bg` | Within plan |
| `--warn` / `--warn-bg` | Near limit (80–100%) |
| `--danger` / `--danger-bg` | Over plan, errors |
| `--shadow-overlay` | The only shadow: menus and dialogs |
| `--google-*` | Only the "Sign in with Google" button |

**Depth comes from the surface steps** (`--bg`, then `--surface`, then `--surface-2`), not from shadows. There are no gradients, glows or neon. Backgrounds are never `#000`, and body text is never `#FFF`.

## Typography

The font is IBM Plex Sans (400/500/600), with a `system-ui` fallback. Line height is 1.5.

| Role | Size / weight |
| --- | --- |
| Page title (`h1`) | 24 / 600 |
| Section title (`h2`) | 16 / 600 |
| Body | 14 / 400 on desktop, 15 on phones |
| Label and caption (`.caption`) | 13 / 400, `--text-muted` |
| KPI number | 24 / 600 (28 / 600 for "Remaining" on phones) |

Use at most 3 sizes per screen, not counting KPIs.

**Money:**
- Always use the `.money` class (tabular figures).
- Right-align money in tables.
- Format it with the currency code, for example "SAR 1,240".
- Never show more than 2 decimals.

## Layout and spacing

- **Spacing:** only 4, 8, 12, 16, 24 and 32px.
- **Page:** content max-width 1100px, centered. Padding is 32px on desktop and 16px on phones (this lives in the shell's `.page`).
- **Desktop:** a 240px sidebar on `--surface` with a right `--border`. The active item has a `--primary-soft` background, `--primary-text` color and 500 weight.
- **Phones (under 768px):**
  - The sidebar is replaced by a bottom bar on `--surface` with a top `--border`.
  - The bar has 5 items; Add sits in the center as a round 52px `--primary` button with a white plus.
  - The active item uses `--primary-text` and 600 weight.
  - Space is left for the iPhone home bar.
  - The Settings gear is a 44px icon button in the top bar, which also serves as the page header (17 / 600).
- **Cards:** `--surface`, 1px `--border`, 12px radius.
  - Padding is 16px on phones and 20–24px on desktop.
  - The gap between cards is 12px on phones and 16px on desktop.
  - A section card has a header (16 / 600) with a 1px `--divider` under it.
- **KPI row (desktop):** a grid of 4 equal cards (`repeat(auto-fit, minmax(200px, 1fr))`).
- **KPI on phones:** one summary card. "Remaining" is large (28 / 600), followed by a progress bar, then Income, Planned and Spent in a 3-column row.
- **Phone stacking:** every card stacks in one column on phones.

## Components

- **Buttons:**
  - Height 40px, radius 8px. Use at most **one** primary (filled) button per screen.
  - Secondary buttons are outlined: `--surface`, 1px `--border`, `--text`.
  - Text buttons use `--primary-text`.
- **Inputs:**
  - Material "outline" appearance (the app default), 40px high.
  - Always a visible label; never rely on a placeholder alone.
  - Input text is 16px on phones, so iPhone doesn't zoom.
  - Amounts use `inputmode="decimal"` (ENT-03).
- **Tables:**
  - The header is 13px `--text-muted`, and rows are 48px.
  - Dividers use `--divider` and row hover uses `--surface-2`. No zebra stripes.
  - On phones every row becomes a card (TBL-05).
- **Progress bars:** 8px high, `--track` track, fully rounded. The fill uses the status color.
- **Status chips:**
  - A pill, 12px / 500, with colored text on its soft background.
  - **Always with a text label**:
    - **Within plan** (`--ok` on `--ok-bg`).
    - **Near limit** (`--warn` on `--warn-bg`) from 80% to 100%.
    - **Over by X** (`--danger` on `--danger-bg`).
- **Category rows on phones:** 3 lines:
  1. Name and status chip.
  2. Progress bar.
  3. "SAR 1,240 of 2,000".
- **Month status (MON-01):**
  - Closed: plain `--text-2` text.
  - In progress: a 2px `--primary-text` border, the label, and a `--primary-soft` chip.
  - Upcoming (R-STA-01 "Yet to come"): a dashed `--border` border.
- **Year grid (MON-02):** it scrolls sideways inside its card, with the first column sticky. The page itself never scrolls sideways.
- **Dialogs:** `--surface`, 12px radius, `--shadow-overlay`. Full screen on phones. Never stacked (UX-04).
- **Settings navigation:** on desktop, Settings sits in the side bar under a 1px `--divider`, after the 5 main items, and shows its sections as indented items (36px, the same active style) while you are in Settings. On phones, `/settings` is one card with a row per section (icon, name, a one-line `.caption` summary, a chevron; 64px rows), and a section page starts with a "‹ Settings" back link (44px, `--primary-text`).
- **People (STG-07, admins only):** a Settings section like the others (icon `group`), hidden from the side bar and the phone list for everyone else.
  - Card "People": one row per admin (caption "Admin", no actions), then one per person who joined (the email, the caption "Joined 18 Oct 2026", and a 44px `person_remove` icon button). With nobody joined, the note "Nobody has joined yet. Create an invite link below and send it."
  - Card "Invite links": "Create invite link" (outlined) in the header, a caption on what a link does, then one row per unused link ("Created 17 Oct 2026", with "Expires on 24 Oct 2026" or "Expired on 8 Oct 2026" as the caption) and a 44px `link_off` icon button to revoke it, with no confirmation (nothing is lost). The captions sit on their own line.
  - Remove asks first in one dialog, "Remove friend@example.com?", with Cancel (focused first) and Remove (the filled button), saying that their data is kept (UX-04).
  - The new link opens in one dialog: the link in a read-only outline input (selected on focus), "Copy it now. NAMA shows it only once.", then Done (outlined) and "Copy link" (the filled button). "Copied." shows under it.
- **Plan (PLN-03):** the totals are a sideways table: 3 rows (Planned, Income, Unassigned) by the months, in equal month columns (72px minimum; it scrolls sideways below that) with each month's header and numbers centered, an exception to "right-align money" so a month and its numbers read as one column, 36px rows, row labels sticky on the left in `--text-muted`, the selected month column on `--primary-soft`, month headers as buttons (44px). The card is sticky at the top on desktop only. Categories and sub-categories are fold rows (56px) with the selected month's amount on the right and "FY total" as a caption under the name; the last saved row is on `--surface-2` for 2 seconds.
- **Sign out:** outside Settings. Desktop: an outlined button with the logout icon at the bottom of the side bar. Phones: a 44px logout icon button in the top bar, next to the gear. Both ask "Sign out?" in one dialog first.
- **Folds:** a list part that is closed until opened ("Archived (2)", "History (3)", a category's sub-categories) uses a text button with a chevron that turns 90° when open, and `aria-expanded`.
- **Dashboard (DSH, preview):**
  - The order is: the title with the caption "Preview: the full dashboard comes in Phase 2", the FY-01 banner (when shown), the FY caption, then one card each for the current month, the FY to date, and planned vs spent. There is no FY selector.
  - The current month card is the in-progress month card's header (2px `--primary-text` border, the "In progress" chip) with the month KPI block shared with Months on Tracker (`shared/month-summary.ts`).
  - FY to date: Income, Spent and Saved in 3 columns (24 / 600 on desktop, body size on phones). Saved stays neutral at 0 or more. A negative Saved is `--danger`, keeps its minus sign, and has the caption "Spent more than income" (color is never the only signal).
- **Dashboard chart (DSH-03):** a column chart in plain CSS, no chart library.
  - Each month has two columns: Planned in `--text-muted` and Spent in its status color, with rounded tops.
  - All columns share one scale: the largest planned or spent value of any month is the full 160px height. The axis shows only that value and 0, on `--divider` grid lines (the base line in `--border`).
  - A legend sits above the chart. The month names (3 letters, 12 / 400) sit under the columns; the month in progress is `--primary-text` 600, and upcoming months are `--text-muted`.
  - Each month is a button (at least 44px wide), so on phones the chart scrolls sideways inside its card; the page never does. Hover and the picked month are on `--surface-2`.
  - Color is never the only signal. Under the chart, the picked month (the month in progress at first) is written out with its status chip: "Sep 2026 · In progress · Within plan", then "Planned SAR 1,900 · Spent SAR 1,299.99". Over-plan months are listed in `--danger`: "Over plan: Aug 2026 (Over by SAR 100)". Each column button's aria-label holds the same text.
- **Banner (FY-01):** a card on `--surface` with a 2px `--primary-text` border (like the in-progress month). It has an info icon, the message as a section title, a caption, an inline checkbox, and its action as the page's one filled button (full width on phones). It is not a dialog.
- **Add form (ENT):** it fits on one phone screen without scrolling, and Save is always visible at the bottom.
  - The amount is the larger field (56px high, 22px text). The fields are 16px apart.
  - The planned indicator has its own panel. Until a sub-category is chosen, the panel shows a placeholder: an `insights` icon, "Plan for the month" and a caption saying what will appear.
  - Wide screens (1100px and up): the card is up to 1000px wide, with the panel (320px) beside the fields behind a `--divider` line. Narrower: the panel sits under the fields.
  - Phones: the fields and Save are 48px high. The panel is an inset box (`--bg`, 1px `--divider`, 8px radius) after the fields, and the full-width Save follows it. The card ends at Save (it does not stretch to the screen height); the title and the card are centered vertically together. Save is sticky, so it stays on screen when the form is taller than the screen.
  - Errors: tapping an empty field and leaving it shows nothing. A field shows its error after Save, after a server error, or once it is left with text typed wrong.
  - The edit dialog keeps the compact layout (no panel placeholder).
- **Sign-in page:** the only full-screen page; it covers the top bar too.
  - The background is `web/public/sign-in-bg.svg`, generated by `web/icons/sign-in-bg.py` (never edited by hand), original artwork made from the NAMA mark: concentric squares framing the card, each turned 4° more, fading outwards (stroke opacity, not gradients), over a faint 40px grid. It is an `<img>` with `object-fit: cover`, `alt=""` and `aria-hidden`.
  - A centered card (`--surface`, 360px max) holds the mark (40px, 3px border), "NAMA" as the `h1` (24 / 600, 0.08em letter spacing), the caption "Plan and track your spending, salary to salary.", the Google button (44px high on phones) and any error.
  - "Built by Ouf." sits under the card (13px, `--text-2`).
  - An invite link (`/invite/<token>`, AUTH-06) opens the same page with the caption "You're invited to NAMA. Sign in with Google to join." A link that no longer works shows its reason in the card's error line.
- **Sign in with Google:** follows Google's branding guidelines (light variant): a white button with a 1px `--google-border`, 4px radius, the official multicolor "G" logo (`web/public/google-g.svg`, the only file allowed to contain those brand colors) and dark text. It is the only white button in the app.
- **Icons:** Material Symbols Outlined only (the app default), 20px, `--text-2`.
- **The NAMA mark:** a black square (`--bg`) with a light border (`--primary`), square corners, nothing inside. It is the browser tab icon (`web/public/favicon.svg`, 4/32 border, and `favicon.ico`) and the logo before "NAMA" in the top bar (14px, 2px border, 8px gap; drawn in CSS, `aria-hidden`).
- **App icons (UX-06):** the NAMA mark (owner's request, replaces the "N"): a `--bg` square with a `--primary` border (1/8 of its size), centered on a full-bleed `--bg` background, because the OS rounds the corners. It is 56% of the icon; the maskable one is 50%, inside the 80% safe zone.

## Accessibility (UX-07)

- Text contrast is at least 4.5:1. The accent is neutral (no blue), so color alone never marks a link or an action; links are underlined.
- The focus ring is visible: 2px `--primary-text` on `:focus-visible`.
- Touch targets are at least 44×44px on phones: buttons, nav items, icon buttons, and chips you can tap.
- Color is never the only signal; there is always a text label or an icon.

## Checks for every screen

- Check at 360, 390 and 430px and on desktop: no sideways scrolling, no cut-off text, no overlap.
- `grep` finds no hex colors in `web/src/app`.
- Every page has an empty state (UX-03): a card with a section title that says what is missing, and a caption with links to the page that fixes it.
