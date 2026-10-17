# NAMA progress

## Status

- **M0 Skeleton and pipeline:** code done and checked locally (17 Oct 2026). Not pushed yet. The "a push to `main` deploys" check is still open, waiting on the one-time cloud setup (README → One-time setup).
- Next: M1 Sign-in, in a fresh session.

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
- 17 Oct 2026: `firestore.indexes.json` starts empty. The `entries (fyId asc, date desc)` index is added with the entries work (M6).

## Open questions

- UX-02 (project name and current month label in the top bar) needs profile data. Planned for M1/M3; M0 shows only "NAMA".
- Owner: update the laptop's Node to 24.15 or newer before running `npm start` locally (for example the installer from nodejs.org, or retry `brew install node@24` after `brew update`).
