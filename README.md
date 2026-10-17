# NAMA

Personal fiscal-year spending tracker. Scope and rules: [docs/PRD.md](docs/PRD.md). Progress: [docs/PROGRESS.md](docs/PROGRESS.md).

- `api/`: ASP.NET Core Minimal API (.NET 10), runs on Cloud Run (`me-central1`)
- `api.tests/`: xUnit tests
- `web/`: Angular 22 + Angular Material, served by Firebase Hosting, which rewrites `/api/**` to Cloud Run

## Requirements

- .NET SDK 10
- Node 24.15 or newer (Angular 22 requires it)
- Firebase CLI (`npm install -g firebase-tools`)
- Java JDK 11 or newer (for the Firestore emulator, from M3)
- Google Cloud CLI (`gcloud`, for the one-time setup)
- Docker (optional, to run the production container locally)

## Local development

```sh
# API on http://localhost:5080
cd api && dotnet run

# Web on http://localhost:4200, /api is proxied to the API
cd web && npm install && npm start

# Tests and build
dotnet test api.tests
cd web && npm run build
```

Run the production container locally:

```sh
docker build -t nama-api api
docker run --rm -p 8080:8080 -e APP_VERSION=local nama-api
curl http://localhost:8080/api/health
```

## Environment variables (API)

| Name | Purpose |
| --- | --- |
| `PORT` | Port to listen on. Set by Cloud Run |
| `APP_VERSION` | Git commit returned by `GET /api/health`. Set by the workflow |
| `FIREBASE_PROJECT_ID` | Firebase project for token checks (used from M1) |
| `ALLOWED_EMAILS` | Comma-separated emails allowed to use the app (used from M1) |

## Deploy

Every push to `main` runs [.github/workflows/deploy.yml](.github/workflows/deploy.yml): API tests, web build, deploy the API to Cloud Run, deploy Hosting and Firestore rules, then a smoke test on `https://nama-b6bda.web.app/api/health`. There is only one environment: production.

GitHub repository variables (Settings → Secrets and variables → Actions → Variables): `GCP_PROJECT_ID`, `GCP_REGION`, `WIF_PROVIDER`, `DEPLOY_SA`, `ALLOWED_EMAILS`. No secrets are needed.

## One-time setup

1. Firebase console for project `nama-b6bda`:
   - Upgrade to Blaze. Create a 5 USD budget alert scoped to this project.
   - Authentication → Sign-in method → enable Google.
   - Firestore → create the (default) database in Native mode, location `me-central1`. The location cannot be changed later.
2. Install the Google Cloud CLI and run `gcloud auth login`.
3. Run `GITHUB_REPO=your-github-user/nama ./infra/setup.sh` and add the GitHub variables it prints.
4. Push to `main`.
5. After the first successful deploy, run `./infra/setup.sh cleanup` (keeps the 5 newest API images).

## Skipped Nice items

None yet.
