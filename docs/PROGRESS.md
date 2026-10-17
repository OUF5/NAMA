# NAMA progress

## Status

- **M0 Skeleton and pipeline: done (17 Oct 2026).** A push to `main` deployed commit `c1205d6`. https://nama-b6bda.web.app returns 200, and `/api/health` returns `{"status":"ok","version":"c1205d60220e24b1d2ad98f025ce79fef2f75f1f"}`. One-time setup is complete: Firestore (default) database in `me-central1`, Workload Identity for `OUF5/NAMA`, 5 GitHub variables, and the Artifact Registry cleanup policy (keep 5 newest).
- **M1 Sign-in: done (17 Oct 2026).** All three "Done when" checks pass on production (https://nama-b6bda.web.app):
  - The owner's email signs in and lands on Setup (owner confirmed).
  - Another email sees "Access not allowed" (owner confirmed).
  - `/api/me` without a token returns 401 (CI smoke test and curl).
  - The first deploy was commit `b199650`; all CI steps passed.
- **Design foundation: deployed (17 Oct 2026)**, commit `d4a552f`, approved by the owner. Dark theme only, near-black and monochrome, IBM Plex Sans, the Google sign-in button. Rules are in [docs/DESIGN.md](DESIGN.md).
- **M2 Domain core: done (17 Oct 2026).** `api/Domain` has `FiscalCalendar`, `Money` and `IncomeCalculator`, with tests in `FiscalCalendarTests`, `MoneyTests` and `IncomeCalculatorTests`. They cover every example in sections 3 and 4.1. No endpoint, web or package changes.
  - "Done when": the CI run for commit `18b979c` passed all 142 tests ("Passed! - Failed: 0, Passed: 142"), and every later deploy and smoke-test step passed too.
  - Locally, `dotnet test api.tests` also passed 142 of 142, and `npm run build` passed.
- **M3 Setup wizard: done (17 Oct 2026).** The 6-step wizard, `GET /api/setup/preview`, `POST /api/setup` (one Firestore batch) and the full `GET /api/me` with the UX-02 top bar. Commit `013388c` deployed; all CI steps passed (`dotnet test`: 184 of 184).
  - "Done when", locally (emulator): the owner finished the wizard and landed on Plan; the emulator held the profile, FY 2026 with 12 months, 2 income items, 2 accounts and 2 categories.
  - "Done when", production (https://nama-b6bda.web.app): the owner finished the wizard and landed on Plan, with the top bar showing the project and the current month. The Cloud Run logs show the preview calls (200), `POST /api/setup` 201 in 0.64 s and `GET /api/me` 200. Firestore holds the profile, `fiscalYears/2026-01` "FY 2026" (25 Jan 2026 to 24 Jan 2027, 12 months), the salary and one additional income item from `2026-01`, 2 bank accounts and 4 categories, all with `createdAt`/`updatedAt` (checked read-only).
  - Locally: `npm run build` passed (with the bundle budget warning below), and `npx ng test --watch=false` passed 32 of 32 (including a wizard spec for the Add buttons, after the owner found that Add reloaded the page).
  - The `aspnet:10.0` image is Ubuntu 24.04 with `tzdata` and `tzdata-legacy` 2026c (checked with `docker run`). Production "today" works: `/api/me` returned the current month.
  - The owner ran `./infra/setup.sh firestore` before the deploy (`roles/datastore.user` confirmed on the Compute Engine default service account).
- Next: M4 Plan, in a fresh session.

## Decisions

- 17 Oct 2026: Firebase project ID is `nama-b6bda`. Default Hosting URL `https://nama-b6bda.web.app`.
- 17 Oct 2026: Claude did PRD "Step 1" (CLAUDE.md, docs/PRD.md, this file, `git init -b main`).
- 17 Oct 2026: M0 was built in the same session as Step 1 (owner's choice).
- 17 Oct 2026: Every Angular 22 release requires Node `^22.22.3 || ^24.15.0 || >=26`. The laptop had Node 24.7 (Homebrew), and `brew install node@24` failed with a Homebrew lock bug around openssl@3/openssl@4. The system Node was left at 24.7. The M0 build used the official Node 24.22.0 tarball from nodejs.org (checksum verified) in a temporary folder. CI uses Node 24 via `actions/setup-node`.
- 17 Oct 2026: Test dependency `Microsoft.AspNetCore.Mvc.Testing` 10.0.13. Reason: the official way to call the API in-memory in a test (health endpoint test). Approved with the M0 plan.
- 17 Oct 2026: GitHub Action versions checked against their latest releases: `actions/checkout@v7`, `actions/setup-dotnet@v6`, `actions/setup-node@v7`, `google-github-actions/auth@v3`, `google-github-actions/deploy-cloudrun@v3`. The gcloud flags `--no-invoker-iam-check`, `--cpu-boost`, `--min-instances` and `--max-instances` were checked in the gcloud reference.
- 17 Oct 2026: In the workflow, `ALLOWED_EMAILS` is quoted in `env_vars`, because `deploy-cloudrun` treats unquoted commas as separators. This differs from the PRD 7.9 sketch.
- 17 Oct 2026: The smoke test also checks that `/api/health` returns this push's commit id (the M0 "Done when" check).
- 17 Oct 2026: `infra/setup.sh` adds two things to the PRD 7.12 role set, both from the Cloud Run "deploy from source" docs: `roles/serviceusage.serviceUsageConsumer` for the deploy service account, and `roles/run.builder` for the Compute Engine default service account (Cloud Build uses it to build). It also enables the `iam`, `sts` and `cloudresourcemanager` APIs (required by the Workload Identity Federation docs).
- 17 Oct 2026: GitHub repo is `OUF5/NAMA`. The Workload Identity condition and binding are case-sensitive and must use `OUF5/NAMA` (the first setup run used `OUF5/nama`, and the owner fixed it by hand).
- 17 Oct 2026, M1: `GET /api/me` has no Firestore yet. It returns `setupCompleted: false` and null for the profile, current FY id and current month key. Firestore comes with the setup write in M3 (owner's choice).
- 17 Oct 2026, M1: Sign out is in Settings (AUTH-04), and the no-access page has "Use another account". Settings stays behind `setupGuard` (owner's choice).
- 17 Oct 2026, M1: New libraries. `Microsoft.AspNetCore.Authentication.JwtBearer` 10.0.13 is the token check named in PRD 7.2. `firebase` ^13.0.0 is for client sign-in (PRD 7.2); only `firebase/app` and `firebase/auth` are imported. 13.0.0's breaking changes are in AI Logic and Firestore only (owner chose 13 over 12.19.0).
- 17 Oct 2026, M1: Every endpoint is protected by the authorization fallback policy: a valid token, a `sub` claim, `email_verified` true, and an allowlisted email. `/api/health` opts out with `AllowAnonymous`. A missing or invalid token gets 401; a valid token that is not allowed gets 403.
- 17 Oct 2026, M1: Errors are ProblemDetails (`AddProblemDetails` + `UseStatusCodePages`). Authentication and authorization are added explicitly after the no-store middleware, so 401 and 403 also send `Cache-Control: no-store`.
- 17 Oct 2026, M1: The API refuses to start without `FIREBASE_PROJECT_ID`. For local runs it is set in `launchSettings.json` (a public value). `ALLOWED_EMAILS` is set on the command line, so no email is committed.
- 17 Oct 2026, M1: The allowlist parser drops surrounding `"` characters. The deploy-cloudrun README does not say whether the quotes we put around `ALLOWED_EMAILS` are kept in the value.
- 17 Oct 2026, M1: The sign-in popup uses `prompt=select_account`, so "Use another account" always shows the Google account chooser.
- 17 Oct 2026, M1: The interceptor and `setupGuard` wait for `authStateReady()`. Guards of one route run at the same time, so without this a page reload could call `/api/me` before Firebase restores the session, get 401 and sign the user out.
- 17 Oct 2026, M1: The navigation and the Settings gear are hidden until setup is complete (owner's choice), because `setupGuard` sends every item back to Setup. They are hidden on sign-in, no-access and Setup. `/setup` also loads `/api/me` (`allowedGuard`), so reloading it still checks the allowlist (found in the owner's browser test).
- 17 Oct 2026, M1: The smoke test also checks that `/api/me` without a token returns 401 (the M1 "Done when" check).
- 17 Oct 2026: **Dark theme only (replaces UX-05)** (owner's decision). There is no light theme and no `prefers-color-scheme` switching. The tokens (colors, the single overlay shadow, the font) live only in `web/src/styles.scss`, together with the Material overrides; components use the tokens and never hex. The component rules are in [docs/DESIGN.md](DESIGN.md). The font is IBM Plex Sans (Google Fonts link, no npm package). In the brief, the "white" surfaces, the light secondary-button colors and the light table hover were mapped to the dark tokens (owner's choice). The focus ring and the active nav text use `--primary-text`, because `--primary` is about 3.5:1 on `--bg` (below 4.5:1 for text).
- 17 Oct 2026: The surfaces are neutral near-black instead of blue-tinted dark gray (owner's request): page `#0a0a0b`, cards and bars `#121214`, hover and inputs `#1a1a1d`. They are not pure `#000`, which the design rules forbid because it makes text glare. Only the six surface tokens changed; text, accent and status colors are the same.
- 17 Oct 2026: No blue (owner's request). Buttons, the active nav and the focus ring are neutral gray: a filled button is light gray `#e4e4e7` with dark text, and secondary buttons are gray outlined. Color is used only for status (ok, warn, danger). The sign-in button follows Google's branding (light variant): white, with the official "G" logo in `web/public/google-g.svg`. Its text stays "Sign in with Google".
- 17 Oct 2026: The design work so far is the foundation only: tokens, Material overrides, typography, the shell, sign-in and no-access. Every screen built from M3 onwards must follow docs/DESIGN.md (chips, KPI cards, category rows, month cards, year grid, tables as cards on phones, the Add form).
- 17 Oct 2026: `<mat-icon>` now uses the Material Symbols Outlined font that `index.html` loads (`MatIconRegistry.setDefaultFontSetClass`). Before, it asked for the "Material Icons" font, which was never loaded, so icons probably showed as words. Inputs default to the outline appearance (`MAT_FORM_FIELD_DEFAULT_OPTIONS`).
- 17 Oct 2026, M2: **Salary change rule (R-INC-02)** (owner's choice). The new salary runs open-ended from the effective month. The salary item running into that month ends the month before. Salary items that start at or after the effective month are replaced. Example: 10,000 from Jan and 12,000 from Jun, then 11,000 from Mar, gives Jan–Feb 10,000 and Mar onward 11,000. The new item's label is "Salary".
- 17 Oct 2026, M2: `FiscalCalendar.FindPeriod` (a date's FY and month, or null outside every FY) is built in M2 for R-CAL-09 and R-STA-03 (owner's choice).
- 17 Oct 2026, M2: Inside `api/Domain`, dates are `DateOnly` and month keys are `YYYY-MM` strings. They become `YYYY-MM-DD` strings at the Firestore and DTO edge (M3).
- 17 Oct 2026, M2: An FY stores the settings it was created with. A transition FY stores the new start month (for example 7 for the Jan to Jun 2027 transition) and `isTransition` true. R-CAL-06 is applied to transition FYs unchanged ("FY 2027").
- 17 Oct 2026, M2: `Money.IsValidAmount` takes `allowZero`, because SET-01 allows a salary of 0. Which callers allow 0 is decided in M3 and M4. The decimals check uses `amount * 100` being whole, not `decimal.Scale`, so `1.500` counts as 2 decimals.
- 17 Oct 2026, M2: The `Today` tests use a small `TimeProvider` subclass, not the `Microsoft.Extensions.TimeProvider.Testing` package (no new dependency).
- 17 Oct 2026: `firestore.indexes.json` starts empty. The `entries (fyId asc, date desc)` index is added with the entries work (M6).

- 17 Oct 2026, M3: New library `Google.Cloud.Firestore` 4.5.0 (PRD 7.2 data access). The `FirestoreDb` is built on first use with `EmulatorDetection.EmulatorOrProduction`; `launchSettings.json` sets `FIRESTORE_EMULATOR_HOST=localhost:8080`, so `dotnet run` never touches production.
- 17 Oct 2026, M3: **Live preview endpoint** (owner's choice, adds to PRD 7.5): `GET /api/setup/preview?fiscalStartMonth=&paydayDay=&timezone=` returns the first FY and the current month from `FiscalCalendar`, so the month and FY rules exist only in `api/Domain`. The Review step reuses it.
- 17 Oct 2026, M3: **Wizard income months** (owner's choice): salary and Monthly items run from the FY's first month, open ended; Once items are in the current month. There are no month pickers in the wizard. "Total monthly income" = salary + Monthly items; Once items are listed on their own.
- 17 Oct 2026, M3: `allowZero: true` only for the wizard salary (SET-01). Additional income items must be more than 0. The salary change in M8 decides for itself.
- 17 Oct 2026, M3: **Currency list** (owner's choice): SAR (default), QAR, AED, USD, EUR, GBP. `Money.Currencies` in the API and `CURRENCIES` in `web/src/app/shared/money.ts` must stay equal.
- 17 Oct 2026, M3: An additional income label is trimmed and 1 to 40 characters; duplicates are allowed (owner's choice). Names (4.7) live in `api/Domain/Names.cs`.
- 17 Oct 2026, M3: **Sign out in the wizard** (owner's choice): a text button under the wizard card, because Settings is not reachable before setup. AUTH-04 is unchanged.
- 17 Oct 2026, M3: **UX-02 top bar** (owner's choice): desktop shows `NAMA · project name` and then "Sep 2026 · 27 Sep to 26 Oct"; phones show the same on two lines (the first line is cut with "…"). Only "NAMA" before setup. The month part is hidden when no FY contains today.
- 17 Oct 2026, M3: `/api/me` also returns `currentPeriod` (key, label, start and end date) for UX-02. `POST /api/setup` answers 201 with the same body, so the web app needs no second call.
- 17 Oct 2026, M3: The setup handler checks for an existing profile first, and the batch *creates* the profile document, so a second setup fails as a whole (409) even in a race.
- 17 Oct 2026, M3: Document ids for incomes, bank accounts and categories are GUIDs (32 hex characters) made by the API. `lastBankAccountId` is null after setup (the Add form in M5 picks the default).
- 17 Oct 2026, M3: Validation errors use the C# property names (`ProjectName`, `BankAccounts[0]`), because the built-in validation reports `[Range]` and `[Required]` errors that way.
- 17 Oct 2026, M3: The wizard draft (SET-03) is in `localStorage` under `nama.setup.<uid>`, removed on Finish and on sign-out. FY start month and payday have no defaults, so both are chosen on purpose.
- 17 Oct 2026, M3: `/setup` sends a user who is already set up to the Dashboard (`allowedGuard`).
- 17 Oct 2026, M3: The outline input default moved from `app.config.ts` to `web/src/app/shared/form-field.ts` (`OUTLINE_FORM_FIELDS`), provided by each page with inputs. Importing the token in `app.config.ts` put all of form-field and `@angular/forms` (about 125 kB) into the initial bundle.
- 17 Oct 2026, M3: Cloud Run runs as the Compute Engine default service account, which had only `roles/run.builder` (the org enforces `iam.automaticIamGrantsForDefaultServiceAccounts`). `./infra/setup.sh firestore` grants it `roles/datastore.user`; the owner runs it before the first M3 deploy.
- 17 Oct 2026, M3: Endpoint tests use an in-memory `IUserStore` (CI has no emulator). The Firestore store is checked by hand against the emulator.

## Open questions

- M4: can a plan amount be 0 (to clear a month), or must the month be removed? R-MON-02 says greater than 0, but R-PLN-01 counts a missing amount as 0.
- Two FYs can share a label. Example: a transition FY "FY 2027" (Jan to Jun 2027), then the start month changes again before the next rollover, giving a second transition "FY 2027" (Jul to Sep 2027). The ids differ. Should the label get a suffix?

- The Angular specs (interceptor, guards, app) run locally with `npx ng test --watch=false`, but CI only builds. Add `npm test` to the workflow?
- The initial bundle is 513.27 kB raw (130.98 kB transferred), over the 500 kB warning budget; the error budget is 1 MB. It was 474.88 kB in M2. The growth is shared Angular core and CDK code that the wizard uses, which stays in modules the initial bundle already loads. Raise the warning budget, or accept the warning?
- The wizard has no cap on the number of accounts, categories or income items. Firestore allows 500 writes per batch, so a setup with more than about 490 items would fail. Add a cap?
- Run the Firestore emulator in CI (Java and firebase-tools in the workflow), so the Firestore store is tested automatically?
- Open sign-up instead of the allowlist? Anyone with a Google account could then use the app, which would change AUTH-02. The owner wants to revisit this later. If so, it needs abuse and cost protection at the same time (for example a user cap), because the site is public and the budget alert never stops spending. Until then, people are added through the `ALLOWED_EMAILS` GitHub variable.