# SLYPN deployment secrets + app settings

What lives where, and how to roll values per environment. Goes alongside `docs/auth-setup.md` (Entra), `docs/observability-setup.md` (Grafana), and `docs/local-emulators.md` (dev).

> **Nothing in source control.** `local.settings.json` and `.env.local` are gitignored; samples in `local.settings.sample.json` and `.env.example` use placeholder strings.

---

## 1. GitHub Actions secrets

Set on **the repo** (`Settings → Secrets and variables → Actions → Repository secrets`). Per-environment values live as separate secrets so the workflow can pick one per branch later.

| Secret | Used by | Source |
|---|---|---|
| `AZURE_STATIC_WEB_APPS_API_TOKEN` | `.github/workflows/azure-static-web-apps.yml` | `az staticwebapp secrets list -n swa-slypn-prod -g rg-slypn-prod` after the first Bicep deploy |

Set via gh CLI:

```bash
gh secret set AZURE_STATIC_WEB_APPS_API_TOKEN  --body "<token>"
```

There's only one SWA at this scope (production). PR previews use the same SWA but a different `deployment_environment` label, so they share the token.

---

## 2. SWA application settings — Functions API

Set on the SWA resource via portal or CLI. These flow into the Functions process as environment variables prefixed with `AzureAd__`, `Storage__`, etc., matching the binding paths in `OtelOptions`, `EntraOptions`, etc.

### Common to dev + prod

| Setting | Purpose | Example |
|---|---|---|
| `Storage__MediaContainer` | Blob container for media uploads | `media` |
| `Storage__ContentContainer` | Blob container for article/draft bodies | `content` |
| `AzureAd__Authority` | Entra External ID authority | `https://slypn.ciamlogin.com/<tenant-id>/v2.0` |
| `AzureAd__Audience` | API audience | `api://<api-client-id>` |
| `AzureAd__TenantId` | Entra tenant | `<tenant-id>` |
| `AzureAd__SkipAuth` | **MUST be `false`** in any deployed env | `false` |
| `Graph__TenantId` | Same Entra tenant | `<tenant-id>` |
| `Graph__ClientId` | `slypn-api` app reg client id | `<api-client-id>` |
| `Otel__Endpoint` | Grafana Cloud OTLP HTTP endpoint | `https://otlp-gateway-prod-eu-west-2.grafana.net/otlp` |
| `Otel__Headers` | OTLP Basic-auth header | `Authorization=Basic <base64>` |
| `Otel__ServiceName` | OTel resource attribute | `slypn-api` |
| `Otel__Env` | OTel resource attribute | `prod` (or `dev`) |

### Dev-only

| Setting | Purpose | Notes |
|---|---|---|
| `Storage__ConnectionString` | Azurite or storage account connection string (Table + Blob) | Set everywhere until managed identity is wired (#38). Prod then relies on the Bicep-granted `Storage Blob Data Contributor` + `Storage Table Data Contributor` roles on the SWA managed identity. |
| `Graph__ClientSecret` | Graph app secret for invitation flow | Required everywhere until Graph supports managed identity for `User.Invite.All` (it doesn't today). Rotate annually. |

### Set via Azure CLI

```bash
az staticwebapp appsettings set \
  --name swa-slypn-prod -g rg-slypn-prod \
  --setting-names \
    AzureAd__Authority="https://slypn.ciamlogin.com/<tenant-id>/v2.0" \
    AzureAd__Audience="api://<api-client-id>" \
    AzureAd__TenantId="<tenant-id>" \
    AzureAd__SkipAuth="false" \
    Storage__MediaContainer="media" \
    Storage__ContentContainer="content" \
    Graph__TenantId="<tenant-id>" \
    Graph__ClientId="<api-client-id>" \
    Graph__ClientSecret="<rotated>" \
    Otel__Endpoint="https://otlp-gateway-prod-eu-west-2.grafana.net/otlp" \
    Otel__Headers="Authorization=Basic <base64>" \
    Otel__ServiceName="slypn-api" \
    Otel__Env="prod"
```

---

## 3. SWA staging-environment overrides (PR previews)

Per-PR-preview overrides are configured the same way but with `--environment-name pr-<number>`. The default policy is "PRs inherit production app settings" — fine for most things, but if you want PR writes isolated from production data, **point `Storage__ConnectionString` at a separate storage account** for the preview:

```bash
az staticwebapp appsettings set \
  --name swa-slypn-prod -g rg-slypn-prod \
  --environment-name pr-42 \
  --setting-names Storage__ConnectionString="<sandbox-account-connection-string>"
```

---

## 4. Vue `VITE_*` build-time values

These bake into the JS bundle at SWA build time. Set them as SWA app settings **before** the workflow runs, or pass them as build env via the action's `env` block.

| Setting | Notes |
|---|---|
| `VITE_MSAL_AUTHORITY` | Same as `AzureAd__Authority` |
| `VITE_MSAL_CLIENT_ID` | `slypn-spa` client id (note: **not** `slypn-api`'s id) |
| `VITE_API_SCOPE` | `api://<api-client-id>/access_as_user` |
| `VITE_FARO_URL` | Faro collector URL from Grafana Cloud Connections page |
| `VITE_FARO_APP_NAME` | `slypn-web` |
| `VITE_FARO_ENV` | `prod` (or `dev`) |

> Vite reads `VITE_*` from `process.env` at build time. The SWA build environment populates that from the app settings, so set `VITE_*` keys at the SWA scope **before** triggering the build.

---

## 5. Rotation cadence

| Secret | Rotation | Trigger |
|---|---|---|
| `AZURE_STATIC_WEB_APPS_API_TOKEN` | Annual | Or sooner if leaked. Regenerate in portal → re-set in repo secrets. |
| `Graph__ClientSecret` | Annual | Or sooner if leaked. Generate in Entra → re-set in SWA app settings. |
| `Otel__Headers` (Grafana access policy token) | Annual | Or sooner. Recreate access policy in Grafana → re-set. |
| Entra app reg secrets | n/a — public client (SPA uses PKCE, no secret) | — |
| Storage connection string | n/a — managed identity in prod (#38) | — |

A failed rotation never breaks running pods because SWA app settings are loaded on cold start; restart the Functions worker via portal after updating.

---

## 6. Quick "what env vars does X use" lookup

| Code path | Reads |
|---|---|
| `src/api/.../EntraOptions.cs` | `AzureAd__*` |
| `src/api/.../StorageOptions.cs` | `Storage__*` (Table + Blob) |
| `src/api/.../GraphOptions.cs` | `Graph__*` |
| `src/api/.../OtelOptions.cs` | `Otel__*` |
| `src/web/src/lib/msal.ts` | `VITE_MSAL_AUTHORITY`, `VITE_MSAL_CLIENT_ID`, `VITE_API_SCOPE` |
| `src/web/src/lib/faro.ts` | `VITE_FARO_URL`, `VITE_FARO_APP_NAME`, `VITE_FARO_ENV` |
