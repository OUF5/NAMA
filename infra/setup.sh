#!/usr/bin/env bash
# One-time Google Cloud setup for NAMA (PRD 7.12). You run this by hand, once.
#
# Before running, in the Firebase / Google Cloud console:
#   1. Upgrade project nama-b6bda to Blaze and create a 5 USD budget alert scoped to it.
#   2. Authentication -> Sign-in method -> enable Google.
#   3. Firestore -> create the (default) database, Native mode, location me-central1.
#      The location cannot be changed later.
#
# Usage:
#   gcloud auth login
#   GITHUB_REPO=your-github-user/nama ./infra/setup.sh            # steps 4 to 7
#   ./infra/setup.sh cleanup                                       # step 9, after the first deploy
#   ./infra/setup.sh firestore                                     # before the first Firestore deploy (M3)
set -euo pipefail

PROJECT_ID="nama-b6bda"
REGION="me-central1"
SA_NAME="nama-deploy"
POOL_ID="github"
PROVIDER_ID="nama-repo"
SA_EMAIL="${SA_NAME}@${PROJECT_ID}.iam.gserviceaccount.com"

# Step 9: keep only the 5 newest API images in Artifact Registry.
if [[ "${1:-}" == "cleanup" ]]; then
  POLICY_FILE="$(mktemp)"
  cat > "$POLICY_FILE" <<'JSON'
[
  { "name": "keep-5-newest", "action": { "type": "Keep" }, "mostRecentVersions": { "keepCount": 5 } },
  { "name": "delete-the-rest", "action": { "type": "Delete" }, "condition": { "tagState": "any" } }
]
JSON
  gcloud artifacts repositories set-cleanup-policies cloud-run-source-deploy \
    --project="$PROJECT_ID" \
    --location="$REGION" \
    --policy="$POLICY_FILE" \
    --no-dry-run
  rm -f "$POLICY_FILE"
  echo "Cleanup policy set on cloud-run-source-deploy."
  exit 0
fi

# M3: the API reads and writes Firestore. Cloud Run runs as the Compute Engine default service
# account, which gets no roles automatically here (iam.automaticIamGrantsForDefaultServiceAccounts
# is enforced), so it needs Cloud Datastore User.
if [[ "${1:-}" == "firestore" ]]; then
  PROJECT_NUMBER="$(gcloud projects describe "$PROJECT_ID" --format='value(projectNumber)')"
  gcloud projects add-iam-policy-binding "$PROJECT_ID" \
    --member="serviceAccount:${PROJECT_NUMBER}-compute@developer.gserviceaccount.com" \
    --role="roles/datastore.user" \
    --condition=None
  echo "Cloud Run can now use Firestore."
  exit 0
fi

: "${GITHUB_REPO:?Set GITHUB_REPO to your repository, for example GITHUB_REPO=your-github-user/nama}"

PROJECT_NUMBER="$(gcloud projects describe "$PROJECT_ID" --format='value(projectNumber)')"

# Step 4: APIs for Cloud Run source deploys and keyless GitHub auth.
gcloud services enable \
  run.googleapis.com \
  cloudbuild.googleapis.com \
  artifactregistry.googleapis.com \
  iam.googleapis.com \
  iamcredentials.googleapis.com \
  sts.googleapis.com \
  cloudresourcemanager.googleapis.com \
  --project="$PROJECT_ID"

# Step 5: deploy service account, and a Workload Identity pool and provider limited to your repo.
gcloud iam service-accounts create "$SA_NAME" \
  --project="$PROJECT_ID" \
  --display-name="NAMA deploy (GitHub Actions)"

gcloud iam workload-identity-pools create "$POOL_ID" \
  --project="$PROJECT_ID" \
  --location="global" \
  --display-name="GitHub Actions Pool"

gcloud iam workload-identity-pools providers create-oidc "$PROVIDER_ID" \
  --project="$PROJECT_ID" \
  --location="global" \
  --workload-identity-pool="$POOL_ID" \
  --display-name="NAMA repo" \
  --attribute-mapping="google.subject=assertion.sub,attribute.repository=assertion.repository" \
  --attribute-condition="assertion.repository == '${GITHUB_REPO}'" \
  --issuer-uri="https://token.actions.githubusercontent.com"

gcloud iam service-accounts add-iam-policy-binding "$SA_EMAIL" \
  --project="$PROJECT_ID" \
  --role="roles/iam.workloadIdentityUser" \
  --member="principalSet://iam.googleapis.com/projects/${PROJECT_NUMBER}/locations/global/workloadIdentityPools/${POOL_ID}/attribute.repository/${GITHUB_REPO}"

# Step 6: roles for the deploy service account (PRD starting set, plus Service Usage Consumer
# from the Cloud Run "deploy from source" docs). Tighten later.
for ROLE in \
  roles/run.admin \
  roles/iam.serviceAccountUser \
  roles/cloudbuild.builds.editor \
  roles/artifactregistry.admin \
  roles/storage.admin \
  roles/firebase.admin \
  roles/serviceusage.serviceUsageConsumer; do
  gcloud projects add-iam-policy-binding "$PROJECT_ID" \
    --member="serviceAccount:${SA_EMAIL}" \
    --role="$ROLE" \
    --condition=None
done

# Cloud Build builds the source with the Compute Engine default service account,
# which needs Cloud Run Builder (Cloud Run "deploy from source" docs).
gcloud projects add-iam-policy-binding "$PROJECT_ID" \
  --member="serviceAccount:${PROJECT_NUMBER}-compute@developer.gserviceaccount.com" \
  --role="roles/run.builder" \
  --condition=None

# Step 7: values for GitHub -> Settings -> Secrets and variables -> Actions -> Variables.
cat <<EOF

Done. Add these GitHub repository variables:
  GCP_PROJECT_ID  = ${PROJECT_ID}
  GCP_REGION      = ${REGION}
  WIF_PROVIDER    = projects/${PROJECT_NUMBER}/locations/global/workloadIdentityPools/${POOL_ID}/providers/${PROVIDER_ID}
  DEPLOY_SA       = ${SA_EMAIL}
  ALLOWED_EMAILS  = your Gmail address (comma separated if more than one)

After the first successful deploy, run:  ./infra/setup.sh cleanup
EOF
