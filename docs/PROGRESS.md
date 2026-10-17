# NAMA progress

## Status

- **M0 Skeleton and pipeline: done (17 Oct 2026).** A push to `main` deployed commit `c1205d6`. https://nama-b6bda.web.app returns 200, and `/api/health` returns `{"status":"ok","version":"c1205d60220e24b1d2ad98f025ce79fef2f75f1f"}`. One-time setup is complete: Firestore (default) database in `me-central1`, Workload Identity for `OUF5/NAMA`, 5 GitHub variables, and the Artifact Registry cleanup policy (keep 5 newest).
- **M1 Sign-in: done (17 Oct 2026).** All three "Done when" checks pass on production (https://nama-b6bda.web.app):
  - The owner's email signs in and lands on Setup (owner confirmed).
  - Another email sees "Access not allowed" (owner confirmed).
  - `/api/me` without a token returns 401 (CI smoke test and curl).
  - The first deploy was commit `b199650`; all CI steps passed.
- **Design foundation: deployed (17 Oct 2026)**, commit `d4a552f`, approved by the owner. Dark theme only, near-black and monochrome, IBM Plex Sans, the Google sign-in button. Rules are in [docs/DESIGN.md](DESIGN.md).
- Next: M2 Domain core, in a fresh session.

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
- 17 Oct 2026: `firestore.indexes.json` starts empty. The `entries (fyId asc, date desc)` index is added with the entries work (M6).

## Open questions

- UX-02 (project name and current month label in the top bar) needs profile data, so it moves to M3. Until then the top bar shows only "NAMA".
- Before setup is finished there is no way to sign out, because Settings is behind `setupGuard`. Should the M3 wizard show Sign out?
- The Angular specs (interceptor, guards, app) run locally with `npx ng test --watch=false`, but CI only builds. Add `npm test` to the workflow?
- The initial bundle is 474.88 kB raw, close to the 500 kB warning budget, because `firebase/auth` is in it. Later milestones may cross the warning.
- Open sign-up instead of the allowlist? Anyone with a Google account could then use the app, which would change AUTH-02. The owner wants to revisit this later. If so, it needs abuse and cost protection at the same time (for example a user cap), because the site is public and the budget alert never stops spending. Until then, people are added through the `ALLOWED_EMAILS` GitHub variable.