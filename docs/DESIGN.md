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
| `--series-1` … `--series-5`, `--series-other` | Chart series only (the Dashboard bank bar, DSH-04), in this fixed order; validated with the dataviz skill's checker on `--surface` (every neighbouring pair, the wrap included). Never a status |
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
- **Desktop:** a 240px sidebar on `--surface` with a right `--border`: Dashboard, Entries, Add, Plan, Tracker, Banks. The active item has a `--primary-soft` background, `--primary-text` color and 500 weight. "Add" is a filled bar (`--primary` background, `--on-primary` text, 500 weight, 0.9 opacity on hover and while open), so the main action stands apart; it is the shell's, not the page's, so a page keeps its own one filled button.
  - Owner note 9: the first item is a fold button (`menu_open` / `menu` icon, `aria-expanded`, "Collapse the menu" / "Expand the menu"). Folded, the side bar is 72px and shows only centered icons; each item's name is its `title` tooltip and accessible name, the Settings sections hide, and Sign out is an icon (its `aria-label` says "Sign out"). The choice is remembered on the device (`nama.nav.collapsed`, guarded storage).
- **Phones (under 768px):**
  - The sidebar is replaced by a bottom bar on `--surface` with a top `--border`.
  - The bar has 5 items (Banks is reached from the top bar's Balance); Add sits in the center as a round 52px `--primary` button with a white plus (the label is for screen readers only).
  - The active item uses `--primary-text` and 600 weight.
  - Space is left for the iPhone home bar.
  - There is no Settings gear (owner note 9). A 44px menu button (`menu` icon, `aria-expanded`) starts the top bar, which also serves as the page header (17 / 600). It opens a drawer from the left (`min(300px, 85vw)`, `--surface`, a right `--border`, `--shadow-overlay`) over a scrim of `--bg` at 0.7: the NAMA mark and a 44px Close button, then 48px rows (icon and name, the active one like the side bar) for every page and Settings, then Sign out under a `--divider`. A tap outside, Escape, Close or a page closes it; the focus goes to Close when it opens and back to the menu button after. Its code loads on the first tap, so it is not in the initial bundle.
- **Cards:** `--surface`, 1px `--border`, 12px radius.
  - Padding is 16px on phones and 20–24px on desktop.
  - The gap between cards is 12px on phones and 16px on desktop.
  - A section card has a header (16 / 600) with a 1px `--divider` under it.
- **KPI row (desktop):** a grid of 4 equal cards (`repeat(auto-fit, minmax(200px, 1fr))`).
- **KPI on phones:** one summary card. "Remaining" is large (28 / 600), followed by a progress bar, then From last month, Planned, Spent and Income two by two.
- **Month KPIs (R-PLN-05):** Remaining, From last month, Planned, Spent, Income, in that order, so the row reads Remaining = From last month + Planned − Spent. "From last month" at 0 is `--text-muted`. Lines and cells show spent "of" what was available (the plan plus what carried in).
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
  - Stacked inputs get a gap of at least 4px, and 12px when they show a hint (for example a month's dates): the hint fills the line under the input, and the next outline label rises above its border.
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
- **Year grid (MON-02):** it scrolls sideways inside its card, with the first column sticky. The page itself never scrolls sideways. Owner note 9: each cell is two right-aligned lines, each value with its label in 11px `--text-muted`: "Spent 1,199.99" (500, in the status color) over "Plan 1,500" (caption; the plan plus what carried in, R-PLN-05); month columns are at least 120px (112px on phones); the hint above says the currency once. Under the Total row, behind a `--border` line, the Balance row ("Balance" with "End of month" as a caption): each month's amount with "Actual" or "Projected" under it (`--danger` below 0, "–" when unknown), and the FY column with "End of FY"; it is not a link. Until the balance is complete, one caption across the row links to Banks.
- **Tracker month (owner note 9):** under the toolbar and filters, the month picker; then the month card (MON-01 header and the month KPIs) and a grid of cards like Dashboard (`repeat(auto-fit, minmax(min(100%, 400px), 1fr))`, every card with the 2px `--primary-text` border, 16px gap, 12px on phones): Spending by day (across), Plan vs spent, Compared with last month, By bank account, Biggest entries, Latest entries. "Compared with last month" rows are 48px: the category with the change in words under it ("SAR 200 more than Aug 2026", `--warn` when more, the words carrying it), and this month's amount. "By bank account" rows: name, an 8px neutral share bar (`--text-2` on `--track`, hidden on phones), the percent, the amount. Entry lists are the 44px one-line rows (day, sub-category, amount, edit).
- **Spending by day:** a column per day in plain CSS (96px high, 2px gaps, 2px rounded tops), neutral `--text-2` columns on one scale (the biggest day is full height), today in `--primary-text` with "Today" under it, days still to come as a 2px `--track` stub, a `--border` base line, and the first and last day under the ends. Each column's `title` is its day and amount; the chart's `aria-label` lists every day with spending. The card head says the month's total and entry count, and "Biggest day: 17 Oct · SAR 799.99" on the right.
- **Month picker:** a 44px "Previous month" icon button, a 220px outline select ("Month"; each option is the month with its status or "This month" in `--text-muted`), and a "Next month" icon button; full width on phones. Plan's Planning tab and Tracker use it.
- **Tabs (Plan):** links under the FY in a row with a `--border` line under it: 44px high, 16px side padding, `--text-2`; the active one is `--primary-text`, 500, with a 2px `--primary-text` line under it and `aria-current="page"`. They scroll sideways on narrow phones rather than wrap.
- **Plan's Categories tab (STG-05, owner note 9):** the list Settings had: one card with "Add category" (outlined) in its head; a 48px row per category (the name opens its sub-categories, "n sub-categories" as a caption) with 44px Rename and Archive or Restore icon buttons; sub-categories indented under it with the same buttons and "Add sub-category" (text button); archived ones fold away under "Archived (n)".
- **Dialogs:** `--surface`, 12px radius, `--shadow-overlay`. Full screen on phones. Never stacked (UX-04).
- **Settings navigation:** on desktop, Settings sits in the side bar under a 1px `--divider`, after the 5 main items, and shows its sections as indented items (36px, the same active style) while you are in Settings. On phones, `/settings` (from the menu drawer) is one card with a row per section (icon, name, a one-line `.caption` summary, a chevron; 64px rows), and a section page starts with a "‹ Settings" back link (44px, `--primary-text`).
- **People (STG-07, admins only):** a Settings section like the others (icon `group`), hidden from the side bar and the phone list for everyone else.
  - Card "People": one row per admin (caption "Admin", no actions), then one per person who joined (the email, the caption "Joined 18 Oct 2026", and a 44px `person_remove` icon button). With nobody joined, the note "Nobody has joined yet. Create an invite link below and send it."
  - Card "Invite links": "Create invite link" (outlined) in the header, a caption on what a link does, then one row per unused link ("Created 17 Oct 2026", with "Expires on 24 Oct 2026" or "Expired on 8 Oct 2026" as the caption) and a 44px `link_off` icon button to revoke it, with no confirmation (nothing is lost). The captions sit on their own line.
  - Remove asks first in one dialog, "Remove friend@example.com?", with Cancel (focused first) and Remove (the filled button), saying that their data is kept (UX-04).
  - The new link opens in one dialog: the link in a read-only outline input (selected on focus), "Copy it now. NAMA shows it only once.", then Done (outlined) and "Copy link" (the filled button). "Copied." shows under it.
- **Plan (PLN-03):** the totals are a sideways table: 4 rows: "Income", "Planned", "Unallocated this month" (the month on its own, in its status color, a `--divider` line above it) and "Accumulated Planned Savings" (600, a `--divider` line above it; owner note 10). A computed row shows its formula under its name in 12px `--text-muted` ("Income − Planned", "Unallocated this month, added up from Aug 2026"), never as + / − / = signs. The row labels are 168px and wrap rather than grow. The note under the table (not a table caption, so it wraps) says what Unallocated is and that Accumulated Planned Savings adds up from the FY's first month. "Unallocated" is the screen word for R-PLN-03's Unassigned; the code and the API keep `unassigned`. The table runs by the months and an "FY total" column behind a `--divider` line, whose last row is Planned saved (R-PLN-04). It also sits in the card head as "Accumulated Planned Savings in FY 2026", right-aligned (caption, then 16 / 600 in the status color). It is the Allocating tab (owner note 9), so it is no longer sticky or folded. The months are in equal month columns (72px minimum; it scrolls sideways below that) with each month's header and numbers centered, an exception to "right-align money" so a month and its numbers read as one column, 36px rows, row labels sticky on the left in `--text-muted`, the selected month column on `--primary-soft`, month headers as buttons (44px). On the Planning tab, the card head has the month picker, and categories and sub-categories are fold rows (56px) with the selected month's amount on the right and "FY total" as a caption under the name; the last saved row is on `--surface-2` for 2 seconds.
- **Top bar numbers (UX-08):** "Left in Sep 2026 · 10 days left" with the month's remaining in its plan status color, and "Balance" with the total (or "Set up" as an underlined `--primary-text` word until it is complete). Each is a link (Tracker, Banks) shown as a 12px caption over a 15 / 600 value, at least 44px high, 8px radius, `--surface-2` on hover, not underlined (like the Year grid cells). Its aria-label says the numbers and where it goes; Balance's title lists each account. Desktop: in the top bar, before the gear. Phones: a strip under the top bar, `--surface` with a bottom `--border`, 48px high; Balance and its eye take what they need (at most 60%) and Left the rest, its caption ending in "…" if it does not fit; it keeps its height while it loads. Owner note 15: a 44px eye icon button (`--text-muted`) follows Balance (not while it is "Set up"): `visibility` while shown ("Hide balance"), `visibility_off` while hidden ("Show balance"). Hidden, the value is "SAR ••••" without its color, the title is empty and the aria-label says "Balance hidden". The choice is kept on the device (`localStorage`), shown by default.
- **Banks (BNK):** the page head has "Add transfer" and "Add account" (outlined; no filled button on the page). Then, when something is missing, a setup card with a 2px `--primary-text` border (like the FY-01 banner) and an info icon, listing each account without a balance with "Set balance" (outlined), and "Choose income split". Then the total card (caption, KPI, "Today, 17 Oct 2026 · 2 accounts", and one caption on what a balance is made of). Then the account cards in a grid (`repeat(auto-fill, minmax(300px, 1fr))`, one column on phones): the name as the title with 44px Edit and Archive icon buttons, the balance as a KPI (`--danger` below 0), the share as a caption, the breakdown as 32px rows with `--divider` lines and `--text-muted` operators ("+", "−", "="), the last row 600, and "See its expenses". Archived accounts fold away under the grid. The split card has one 44px row per account: name, an 8px neutral bar (`--text-2` on `--track`, because a share is no status), the percent and the amount; phones put the bar under the name. Transfers are 56px rows: the day over the year, "Main → Savings" with the note as a caption, the amount, and a 44px Edit.
- **Transfer form (TRF-01):** on Add, under the title with an "Expense | Transfer" button toggle on the right (full width on phones). From, a swap icon button, To on one row (stacked on phones, the swap turned 90°), then the larger Amount, then Date | Note. The panel beside the fields (as the planned indicator) shows "From Main SAR 4,100 → SAR 3,600" and "To Savings …", the old balance in `--text-muted` once an amount is typed and the new one 600 (`--danger` below 0), and the caption "A transfer is never spending: your plan and Tracker stay the same." Until both accounts are chosen it shows a `swap_horiz` placeholder. The edit dialog has no panel.
- **Income form (INC-01, owner note 17):** on Add, the toggle reads "Expense | Transfer | Income". The fields are exactly the Settings "Add income" dialog's (one shared component): Label, then Amount | Type, then From (Month for Once) | Until, then "Counts in" (Planned, Actual, Both) with a hint under it that says what the choice does, then Bank account for Actual or Both. Under the fields, an inset box (`--bg`, 1px `--divider`, 8px radius) with lines like the transfer panel: "Into SNB SAR 4,100 → SAR 4,600" (and "Out of …" when an edited item changes account), a caption on when the money arrives, and "Plan's income Sep 2026 SAR 17,000 → SAR 17,500" for Planned or Both; the old amount in `--text-muted`, the new one 600 (`--danger` below 0). No side panel, so the card keeps the dialog's 640px width on wide screens.
- **Sign out:** outside Settings. Desktop: an outlined button with the logout icon at the bottom of the side bar. Phones: the last row of the menu drawer (owner note 9). Both ask "Sign out?" in one dialog first.
- **Folds:** a list part that is closed until opened ("Archived (2)", "History (3)", a category's sub-categories) uses a text button with a chevron that turns 90° when open, and `aria-expanded`.
- **Dashboard (DSH):** (owner note 8, after the owner's first look: "much cleaner", every card bordered like the month, the months on top)
  - The order is: the title with the FY caption beside it, the FY-01 banner (when shown), then the cards in a grid (`repeat(auto-fit, minmax(min(100%, 400px), 1fr))`, 16px gap): Planned vs spent across the page, the current month and Projection, FY to date and Saving: plan vs projection, then Total balance across the page. Phones: one column, 12px gap.
  - **Every card** is `--surface` with the in-progress month's 2px `--primary-text` border, 12px radius, 20/24px padding (16px on phones). The card head is the h2 with a `.caption` under it and, on the right, at most one thing (a filter, a chip or a link); no divider under it.
  - One big number per card where there is one (32 / 600, 28 on phones): Remaining, the projected balance, the total balance. Other numbers are 2-by-2 KPIs (22 / 600, 17 on phones).
  - Longer explanations fold away under "How?" (a text button with a chevron and `aria-expanded`), so the cards stay short.
  - Planned vs spent: the category picker (an outline `mat-select`, 220px, full width on phones) sits in the card head; only categories with a row are offered.
  - Current month: the "In progress" chip; Remaining in its status color with the status chip beside it; the progress bar with today's marker (a 2px × 16px `--text` tick, `aria-hidden`) and "Day 21 of 30 · 10 days left" under it; From last month, Planned, Spent and Income in a row of 4 (2 by 2 on phones) above a `--divider`; then "Watch": up to 3 rows (44px, links to Tracker filtered to the sub-category, not underlined, `--surface-2` on hover) with the name, "SAR 1,300 of SAR 1,500" and the status chip, or "Every sub-category is within plan."
  - Projection (DSH-05): the caption "End of FY 2026 · 26 Jan 2027"; the projected balance (`--danger` with "Below 0" in the caption under it when negative); the breakdown rows of a Banks card (Balance today, − Still to spend in Sep 2026, + Future unallocated, = Projected balance); "How?" holds what each line is and the months yet to come. Until the balance is complete, one sentence with a link to Banks replaces it all.
  - Saving: plan vs projection (DSH-06): the chip in the head says "On track" (ok) or "SAR 500 below plan" (danger); Planned saving and Projected saving side by side; one caption on why; "How?" holds the lines that make the projected saving and a caption on which months Planned saving covers (owner note 11).
  - FY to date (DSH-02): the caption is "Jan 2026 to Sep 2026 · 9 of 12 months", or "Oct 2026 · since you started NAMA" when NAMA was set up after the FY's first month. While a month is in progress, the words under Spent call the plan's unspent rest "left this month", never "under plan" (owner note 11), and the 4th KPI is "Unspent income", not "Saved", with "SAR 1,500 still to spend in Oct 2026 · SAR 7,500 to keep (as planned)" under it (owner note 12). DSH-06's "How?" starts with the same "Unspent income" line.
- **Bank bar (DSH-04, owner note 13; a pie before):** one bar across the card, 12px high, 4px radius, split by account with a 2px `--surface` gap between parts (no chart library); each part has a `title` tooltip (name, amount, share) and the bar an aria-label with every value. Colors are `--series-1` to `--series-5` in the accounts' order (by name), so an account keeps its color whatever its size; from the 6th account on, the 5th and later share one grey "Other" part (`--series-other`). The series colors are for charts only, never for a status. Under the bar, every account above 0 is listed with its own amount (folded ones too, with a grey key): a 12px key, the name, the share in `--text-muted` and the amount, 36px rows with a `--divider` line, in columns of at least 240px and at most 3 (one on phones), so a name stays close to its amount. When some accounts share the grey part, a caption names them ("The grey part of the bar is Riyad, SNB and STC."). Accounts at 0 or below, or without a balance, are listed under "Not in the bar" (`--danger` below 0). Above it: the total as the big number and "This FY added SAR 135,950 · income minus spent in the closed months".
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
