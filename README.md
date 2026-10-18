# NAMA

Personal fiscal-year spending tracker. Scope and rules: [docs/PRD.md](docs/PRD.md). Progress: [docs/PROGRESS.md](docs/PROGRESS.md). Design rules: [docs/DESIGN.md](docs/DESIGN.md).

- `api/`: ASP.NET Core Minimal API (.NET 10), runs on Cloud Run (`me-central1`)
- `api.tests/`: xUnit tests
- `web/`: Angular 22 + Angular Material, served by Firebase Hosting, which rewrites `/api/**` to Cloud Run

## Requirements

- .NET SDK 10
- Node 24.15 or newer (Angular 22 requires it)
- Firebase CLI (`npm install -g firebase-tools`)
- Java JDK 11 or newer (for the Firestore emulator)
- Google Cloud CLI (`gcloud`, for the one-time setup)
- Docker (optional, to run the production container locally)

## Local development

Local runs use the Firestore emulator and never touch production data.

```sh
# 1. Firestore emulator on localhost:8080
firebase emulators:start --only firestore

# 2. API on http://localhost:5080. launchSettings.json sets FIREBASE_PROJECT_ID and
#    FIRESTORE_EMULATOR_HOST=localhost:8080; the allowlist is given on the command line.
cd api && ALLOWED_EMAILS=you@gmail.com dotnet run

# 3. Web on http://localhost:4200; /api is proxied to the API (web/proxy.conf.json)
cd web && npm install && npm start
```

Real Google sign-in works on `localhost`. The Firebase web config (public, not a secret) is in [web/src/environments/environment.ts](web/src/environments/environment.ts).

Tests and build:

```sh
dotnet test api.tests                    # API and domain tests (CI runs these)
cd web && npm run build                  # production build (CI runs this)
cd web && npx ng test --watch=false      # Angular component tests (local only)
```

Run the production container locally:

```sh
docker build -t nama-api api
docker run --rm -p 8080:8080 -e APP_VERSION=local -e FIREBASE_PROJECT_ID=nama-b6bda -e ALLOWED_EMAILS=you@gmail.com nama-api
curl http://localhost:8080/api/health
```

## Environment variables (API)

| Name | Purpose |
| --- | --- |
| `PORT` | Port to listen on. Set by Cloud Run |
| `APP_VERSION` | Git commit returned by `GET /api/health`. Set by the workflow |
| `FIREBASE_PROJECT_ID` | Firebase project whose ID tokens are accepted. Required: the API does not start without it |
| `ALLOWED_EMAILS` | Comma-separated emails allowed to use the app, ignoring case. Empty means nobody |
| `FIRESTORE_EMULATOR_HOST` | Local only (`localhost:8080`, from `launchSettings.json`). When set, the API uses the emulator instead of production Firestore |

## Deploy

Every push to `main` runs [.github/workflows/deploy.yml](.github/workflows/deploy.yml): API tests, web build, deploy the API to Cloud Run (`--max-instances=1`, `--min-instances=0`, `--no-invoker-iam-check`), deploy Hosting and the Firestore rules and indexes, then smoke tests on `https://nama-b6bda.web.app` (`/api/health` returns the pushed commit, `/api/me` without a token returns 401). There is only one environment: production.

GitHub repository variables (Settings → Secrets and variables → Actions → Variables): `GCP_PROJECT_ID`, `GCP_REGION`, `WIF_PROVIDER`, `DEPLOY_SA`, `ALLOWED_EMAILS`. No secrets are needed. To allow another person, add their email to `ALLOWED_EMAILS` and push.

## One-time setup

1. Firebase console for project `nama-b6bda`:
   - Upgrade to Blaze. Create a 5 USD budget alert scoped to this project.
   - Authentication → Sign-in method → enable Google.
   - Project settings → General → Your apps → add a Web app. Its config goes in `web/src/environments/environment.ts` (`firebase apps:sdkconfig WEB --project nama-b6bda` prints it).
   - Firestore → create the (default) database in Native mode, location `me-central1`. The location cannot be changed later.
2. Install the Google Cloud CLI and run `gcloud auth login`.
3. Run `GITHUB_REPO=your-github-user/nama ./infra/setup.sh` and add the GitHub variables it prints.
4. Run `./infra/setup.sh firestore` (lets Cloud Run read and write Firestore).
5. Push to `main`.
6. After the first successful deploy, run `./infra/setup.sh cleanup` (keeps the 5 newest API images).

## Install on a phone or desktop (UX-06)

- iPhone (Safari): open https://nama-b6bda.web.app, tap Share → Add to Home Screen.
- Android (Chrome) and desktop (Chrome, Edge): open the site, then the browser menu → Install (or "Add to Home screen").

There is no service worker, so the browser never offers an automatic install banner, and the app needs a connection. The icons in `web/public/icons` are rendered from `web/icons/*.svg` on macOS:

```sh
qlmanage -t -s 512 -o /tmp/nama-icons web/icons/nama-icon.svg web/icons/nama-icon-maskable.svg
sips -s format png /tmp/nama-icons/nama-icon.svg.png --out web/public/icons/icon-512.png
sips -s format png /tmp/nama-icons/nama-icon-maskable.svg.png --out web/public/icons/icon-maskable-512.png
sips -z 192 192 /tmp/nama-icons/nama-icon.svg.png --out web/public/icons/icon-192.png
sips -z 180 180 /tmp/nama-icons/nama-icon.svg.png --out web/public/icons/apple-touch-icon.png
```

The sign-in background `web/public/sign-in-bg.svg` is generated; change `web/icons/sign-in-bg.py` and run `cd web && python3 icons/sign-in-bg.py` (it checks the XML before writing).

The browser tab icon is the NAMA mark, `web/public/favicon.svg` (a black square with a light border), with a 32px `favicon.ico` fallback. `qlmanage` draws an SVG at its own size, so render a 512px copy:

```sh
sed 's/width="32" height="32" viewBox/width="512" height="512" viewBox/' web/public/favicon.svg > /tmp/nama-icons/favicon-512.svg
qlmanage -t -s 512 -o /tmp/nama-icons /tmp/nama-icons/favicon-512.svg
sips -z 32 32 /tmp/nama-icons/favicon-512.svg.png --out /tmp/nama-icons/favicon-32.png
sips -s format ico /tmp/nama-icons/favicon-32.png --out web/public/favicon.ico
```

## Skipped Nice items

- **Seed data script** (PRD 7.11): not built. Local data comes from going through the wizard against the emulator.
- **UX-05 light theme with dark mode**: replaced by "dark theme only" (owner's decision, docs/DESIGN.md).
- **Success criteria** (PRD 1): not measured.
- **UX-06 automatic install banner**: not offered, because there is no service worker (owner's choice). Installing from the browser menu and Add to Home Screen work.

Every other Nice item is built: R-PLN-03 (shown as "Unassigned"), archive for accounts, categories and sub-categories (4.6, STG-04, STG-05), the wizard's suggestion chips (5.2 step 5), SET-03, PLN-02, PLN-03, PLN-06, the one-tap Add (5.4), the ENT-01 live preview and colors, ENT-02, TBL-04, TBL-06 (also NFR-05), MON-02 auto-expand, MON-04, STG-06, UX-03, UX-04 and UX-06.
