# NAMA
Personal fiscal-year spending tracker. docs/PRD.md is the source of truth. Requirement IDs (for example ENT-03) are stable.

## Stack
- api/: ASP.NET Core Minimal API, .NET 10, Google.Cloud.Firestore, JwtBearer (Firebase ID tokens)
- web/: Angular 22, Angular Material, standalone components, signals, Firebase JS SDK (auth only)
- Firebase Hosting serves web/ and rewrites /api/** to Cloud Run (api/). Region: me-central1 (Doha)

## Commands
- Firestore emulator: firebase emulators:start --only firestore
- API: cd api && dotnet run        (port 5080, FIRESTORE_EMULATOR_HOST set)
- Tests: dotnet test api.tests
- Web: cd web && npm start         (proxy /api to http://localhost:5080)
- Web build: cd web && npm run build

## Hard rules
- Money: long minor units in storage, decimal in DTOs. Never double or float.
- Dates: YYYY-MM-DD strings, month keys YYYY-MM. Use TimeProvider and the profile time zone for "today".
- Month and FY logic lives only in api/Domain/FiscalCalendar and is unit-tested.
- Take the user id from the token "sub" claim. Never from the request body or URL.
- No DELETE endpoints for categories, sub-categories or bank accounts.
- Browsers never talk to Firestore. firestore.rules denies everything.
- Cloud Run is made public with --no-invoker-iam-check, never with an allUsers IAM binding.
- Keep it simple: no new library without a one-line reason in the commit message.

## Workflow
- Follow "Start here" at the top of docs/PRD.md.
- Scope = docs/PRD.md only. Unclear or conflicting? Stop and ask. New ideas go to docs/PROGRESS.md, not into code.
- Read docs/PROGRESS.md at the start of every session and update it at the end.
- One milestone at a time (PRD section 8). Plan first, then code, then prove it with real test and build output.
- Never invent packages, versions, APIs or CLI flags. Check official docs first.
- Commit messages reference requirement IDs.
