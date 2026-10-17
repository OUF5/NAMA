# NAMA design rules

A dark, calm, professional finance app (benchmarks: Mercury, the Stripe dashboard, IBM Carbon Gray). **Dark theme only** (replaces UX-05). Every screen built from M3 onwards follows this page. If a screen needs something this page doesn't cover, ask before inventing it.

## Tokens

The tokens are defined once in `web/src/styles.scss`, the only file with color values. Components use `var(--token)` and never hex.

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
| `--primary` | **Filled buttons only**, with `--on-primary` text |
| `--primary-text` | Links, active nav, focus ring |
| `--primary-soft` | Active nav background, "In progress" chip |
| `--ok` / `--ok-bg` | Within plan |
| `--warn` / `--warn-bg` | Near limit (80–100%) |
| `--danger` / `--danger-bg` | Over plan, errors |
| `--shadow-overlay` | The only shadow: menus and dialogs |

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
  - Yet to come: a dashed `--border` border.
- **Year grid (MON-02):** it scrolls sideways inside its card, with the first column sticky. The page itself never scrolls sideways.
- **Dialogs:** `--surface`, 12px radius, `--shadow-overlay`. Full screen on phones. Never stacked (UX-04).
- **Add form (ENT):** it fits on one phone screen without scrolling, and Save is always visible at the bottom.
- **Icons:** Material Symbols Outlined only (the app default), 20px, `--text-2`.

## Accessibility (UX-07)

- Text contrast is at least 4.5:1. `--primary` is not used as text color, because it is only about 3.5:1 on `--bg`.
- The focus ring is visible: 2px `--primary-text` on `:focus-visible`.
- Touch targets are at least 44×44px on phones: buttons, nav items, icon buttons, and chips you can tap.
- Color is never the only signal; there is always a text label or an icon.

## Checks for every screen

- Check at 360, 390 and 430px and on desktop: no sideways scrolling, no cut-off text, no overlap.
- `grep` finds no hex colors in `web/src/app`.
