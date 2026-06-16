# SLYPN first deploy — end-to-end runbook

Sequenced checklist for taking the repo from "everything green in CI" to a working production site with a PR-preview that bounces back to prod after merge. Allow ~1 hour of focused click time the first time you do it.

> Prerequisite reading: `docs/auth-setup.md`, `docs/observability-setup.md`, `docs/deployment-secrets.md`, `docs/custom-domain.md`. This doc threads them together.

---

## Phase A — Prerequisites (15 min)

- [ ] **Owner** role on the Azure subscription you'll deploy into (`docs/auth-setup.md` step 6 covers granting yourself if you're not).
- [ ] **Entra External ID tenant** + `slypn-api` and `slypn-spa` app registrations complete (`docs/auth-setup.md` sections 1, 2, 6). Note the **tenant id**, **api client id**, **spa client id** values.
- [ ] **Grafana Cloud stack** with OTLP + Faro app created (`docs/observability-setup.md` sections 1-3). Note the **OTLP endpoint**, **OTLP headers**, **Faro collector URL**.
- [ ] You have the `slypn.org.uk` (or chosen) domain available with DNS access.
- [ ] Local CLI tools logged in:
  ```bash
  az login
  az account set --subscription "<sub-id>"
  gh auth status   # should show project + repo scopes
  ```

---

## Phase B — Provision Azure (10 min)

> We run a single production environment. Per-branch testing happens through SWA's built-in PR preview environments, so there's no separate `dev` resource group to provision.

### B.1 Resource group

```bash
az group create -n rg-slypn-prod -l uksouth
```

### B.2 Deploy Bicep

```bash
az deployment group create \
  -g rg-slypn-prod \
  -f infra/main.bicep \
  -p @infra/main.parameters.prod.json
```

Expected resources after ~5 min:

- `swa-slypn-prod` (Static Web App)
- `slypnprodst<suffix>` (Storage account, with `media` + `content` blob containers and the Table service; the six content tables are created at runtime by `TableBootstrapper`)
- Two role assignments: SWA managed identity → Storage Blob Data Contributor + Storage Table Data Contributor.

Capture the outputs:

```bash
PROD_OUTPUTS=$(az deployment group show -g rg-slypn-prod -n main --query properties.outputs)
echo "$PROD_OUTPUTS" | jq .
```

---

## Phase C — Wire secrets + app settings (10 min)

### C.1 GitHub repo secret

```bash
PROD_TOKEN=$(az staticwebapp secrets list -n swa-slypn-prod -g rg-slypn-prod \
  --query "properties.apiKey" -o tsv)
gh secret set AZURE_STATIC_WEB_APPS_API_TOKEN --body "$PROD_TOKEN"
```

### C.2 SWA app settings

Pull the values from outputs + your captured Entra/Grafana details, then paste into the `az staticwebapp appsettings set` command in `docs/deployment-secrets.md` §2 — once, for `-n swa-slypn-prod -g rg-slypn-prod`.

Critical settings to double-check:

- `AzureAd__SkipAuth=false` (**not** `true`).
- `Storage__ConnectionString` points at the deployed storage account (Table + Blob endpoints).
- `Otel__Headers` is the full `Authorization=Basic <base64>` string.
- `Graph__ClientSecret` is set (annual rotation per `docs/deployment-secrets.md` §5).

### C.3 Remove the build-only skip flag

Once the token is in repo secrets, remove the two `skip_deploy_on_missing_secrets: true` lines from `.github/workflows/azure-static-web-apps.yml` in a follow-up PR so the SWA action either succeeds or fails honestly.

---

## Phase D — First production deploy (10 min)

The workflow on `main` deploys on every push. The very first deploy happens when this runbook's confirmation PR (see Phase E) merges — but you can also trigger it now from the Actions tab → **Azure Static Web Apps deploy** → **Run workflow** → branch `main`.

Wait for the workflow to finish (~3-5 min). The **Build + deploy** step should print the SWA URL.

Visit `https://<swa-default-hostname>.azurestaticapps.net/` — the site should render with all public content.

---

## Phase E — PR preview smoke test (10 min)

The whole point of the SWA action is per-PR previews. Verify the loop works.

### E.1 Open a trivial PR

```bash
git checkout -b chore/first-deploy-confirmation
echo "- $(date -u +%Y-%m-%d) — first prod deploy confirmed" >> docs/deployment-log.md
git add docs/deployment-log.md && git commit -m "chore: log first prod deploy"
git push -u origin chore/first-deploy-confirmation
gh pr create --title "chore: confirm first deploy" --body "Trivial change to exercise PR preview."
```

### E.2 Verify the preview

- [ ] The SWA deploy workflow runs on the PR.
- [ ] The action posts a comment on the PR with a `pr-<number>` preview URL.
- [ ] Click the URL — site renders + reads from the production storage account (acceptable for read-only checks; **don't** create test data on prod from a preview).
- [ ] Optional: override `Storage__ConnectionString` per-preview as `docs/deployment-secrets.md` §3 describes if you'd like the preview pointing at a sandbox storage account.

### E.3 Merge and verify prod

- [ ] Merge the PR.
- [ ] SWA action runs again on `main`, this time deploying to production (`deployment_environment=''`).
- [ ] Re-visit `https://<swa-default-hostname>.azurestaticapps.net/` — the change shows up.
- [ ] PR close fires the **Close PR environment** job, tearing down `pr-<number>`.

---

## Phase F — Auth smoke (10 min)

- [ ] In **Entra External ID → App registrations → slypn-spa → Authentication**, add the production redirect URI: `https://<swa-default-hostname>.azurestaticapps.net/auth/callback` (and the eventual custom-domain one in Phase G).
- [ ] In **Entra External ID → Enterprise applications → slypn-api → Users and groups**, assign yourself the **Administrator** app role.
- [ ] Sign in on the production site → Sign in button → External ID flow → returns to home.
- [ ] Top-right user menu shows your display name; **Dashboard**, **Editor**, **Admin** entries appear.
- [ ] Open **/admin** → Approvals queue loads (empty is fine). Try the **Invite a member** form with a throwaway email; the response should be `inviteSent=true` if Graph creds are wired.
- [ ] Open **/editor** → write a draft → autosave indicator goes "Saving…" → "Saved at HH:mm:ss". Hit **Submit for review** → article appears in the Admin Approvals queue (refresh).

---

## Phase G — Custom domain (15 min)

Follow `docs/custom-domain.md` end-to-end:

- [ ] Add the apex + `www` hostnames in the Azure portal.
- [ ] Add the TXT validation record + the apex/www DNS records at the registrar.
- [ ] Wait for `Healthy / Issued` status (DNS + SSL).
- [ ] Visit `https://slypn.org.uk/` → site renders, cert valid.
- [ ] Update the **Entra slypn-spa redirect URIs** to include `https://slypn.org.uk/auth/callback` + logout URL `https://slypn.org.uk/`.
- [ ] Update the **Faro allowed origins** to include the new domain.

---

## Phase H — Observability check (5 min)

- [ ] In Grafana Cloud → **Explore → Tempo** → query `{ resource.service.name="slypn-api" }`. You should see the spans from the smoke traffic.
- [ ] **Explore → Prometheus** → `rate(http_server_request_duration_seconds_count{service_name="slypn-api"}[5m])`. Non-zero.
- [ ] **Explore → Loki** → `{service_name="slypn-api"}`. You should see info logs from the recent requests.
- [ ] Open the Faro app's **Sessions** view → you should see your own session captured.
- [ ] Bring up the dashboard from `docs/observability-dashboard.md` → all panels populate (Web Vitals may take a few minutes of real navigation to surface).
- [ ] Confirm the **5xx-rate** alert from `docs/observability-dashboard.md` is firing → no, just check it's evaluating (no firing condition unless you intentionally break things).

---

## Phase I — Wrap up

- [ ] Commit/merge the workflow change removing `skip_deploy_on_missing_secrets`.
- [ ] Annotate the GitHub release / project board: Phase 6 complete.
- [ ] Cull stale PRs and branches.
- [ ] Email yourself the rotation calendar from `docs/deployment-secrets.md` §5 so the annual rotations don't slip.

---

## Rollback

If at any phase prod is broken:

1. **Revert the offending PR** on `main`. The SWA action redeploys the previous bundle within ~3 min.
2. **Or roll back via portal** — SWA → Environments → previous production deployment → **Restore**.
3. For Bicep regressions: redeploy a previous `infra/main.bicep` commit against the same RG.

Data (Table + Blob storage) is unaffected by SWA rollbacks. If you need point-in-time recovery, enable storage account backup / soft delete + versioning on the storage account.

---

## What you actually built

After this runbook completes you have:

- Azure Table + Blob Storage backing a Vue 3 + .NET 8 isolated Functions site.
- Per-PR preview environments with auto-tear-down.
- Entra External ID sign-in (email + Google + Facebook) with three roles.
- TipTap-authored articles + blog posts with draft autosave, optimistic concurrency, and admin approval.
- Observability into UI and API via Grafana Cloud (free tier).
- All-managed SSL on a custom domain.

The community can be onboarded as soon as you grant them invitations.

Closes #42 — and the SLYPN build.
