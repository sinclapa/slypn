# SLYPN observability — Grafana Cloud + Faro + OTLP

One-time manual setup. After this, the UI Faro wiring lands in #34 and the API OpenTelemetry wiring in #35.

> **Why Grafana Cloud?** The free tier gives 50 GB logs / 50 GB traces / 10 k metric series / 50 k Faro sessions per month — plenty for SLYPN's expected scale. Everything we send is OTLP-native (no proprietary agents), so we can move off Grafana later by changing one endpoint URL. **Not** affiliated with any other Grafana on the network — this is purely Grafana Labs' hosted offering at `grafana.com`.

---

## 1. Create the Grafana Cloud stack

1. Sign up at <https://grafana.com/auth/sign-up/create-user> (Google / GitHub / email — pick whatever matches your other identities). Free tier; no credit card.
2. After verification you land in the **Cloud** view. Click **+ Create a stack**.
3. **Stack slug**: `slypn` (gives you `slypn.grafana.net`).
4. **Region**: pick the closest — for the UK use `prod-eu-west-2`. The region affects ingestion latency, not pricing.
5. **Provision**. Takes about a minute.
6. Once provisioned, the **Overview** page lists three datasources we'll use:
   - **Prometheus** (metrics) — the **Send Metrics** card has the OTLP endpoint.
   - **Loki** (logs) — same idea.
   - **Tempo** (traces) — same idea.

Note the stack's **org-slug-prod-region** value (e.g. `slypn-prod-eu-west-2`). It shows in URLs and is occasionally needed.

---

## 2. Create the OTLP collector token

The API will send traces + metrics + logs to **Grafana Cloud OTLP** using an access-policy token.

1. **Connections → Add new connection → OpenTelemetry (OTLP)**.
2. Choose **Send data to Grafana Cloud using OTLP**.
3. The wizard creates an access policy and gives you four values **immediately** (you can only see the token once — copy it now):
   - **OTLP HTTP endpoint** — `https://otlp-gateway-<region>.grafana.net/otlp`
   - **Instance ID** — a number, used as the username.
   - **Access policy token** — the password (starts `glc_...`).
   - The combined **`OTEL_EXPORTER_OTLP_HEADERS`** value the wizard generates: `Authorization=Basic <base64-of-instanceId:token>`. Copy that string exactly.
4. Name the access policy something memorable like `slypn-otlp-write`. Scopes:
   - `metrics:write`
   - `logs:write`
   - `traces:write`

Lose the token, you can recreate it from **Administration → Access policies → slypn-otlp-write → Add token**.

---

## 3. Create the Faro web app

Faro is Grafana's browser SDK — front-end errors, web vitals, distributed traces from the SPA.

1. **Connections → Add new connection → Frontend application (Faro)**.
2. Click **New app**.
3. **App name**: `slypn-web`.
4. **App URL**: `http://localhost:5173/` (we'll add `https://slypn.example.com/` once #41 wires the SWA custom domain).
5. **Captures**: tick **Web vitals**, **Errors**, **Console**, **Sessions**, **Traces (distributed)**.
6. **Create app**. You'll be shown:
   - **Faro collector URL** — `https://faro-collector-<region>.grafana.net/collect/<app-id>`.
   - **App key** (rotates automatically; the collector URL has the id baked in).
7. **Allowed origins**: add `http://localhost:5173` and (later) the production SWA URL. Without this, browser CORS will block the SDK.

---

## 4. Values to capture for code wiring

Land these in SWA app settings + Vue env vars when #34 / #35 / #41 wire things up. **Nothing in source control.**

| Where | Setting | Source |
|---|---|---|
| Vue SPA (`.env.local` / SWA app setting) | `VITE_FARO_URL` | Faro collector URL from §3.6 |
| Vue SPA | `VITE_FARO_APP_NAME` | `slypn-web` |
| Vue SPA | `VITE_FARO_ENV` | `dev` / `prod` |
| Functions API (`local.settings.json` / SWA app setting) | `Otel__Endpoint` | OTLP HTTP endpoint from §2.3 |
| Functions API | `Otel__Headers` | The full `Authorization=Basic ...` string from §2.3 |
| Functions API | `Otel__ServiceName` | `slypn-api` |
| Functions API | `Otel__Env` | `dev` / `prod` |

Sample placeholder values land in `src/web/.env.example` and `src/api/Slypn.Api/local.settings.sample.json` alongside the SDK code in #34 / #35.

---

## 5. Baseline dashboard + alert (#36)

After data is flowing:

- The OTLP "Send to Grafana Cloud" wizard pre-provisions a generic OpenTelemetry dashboard (filter by `service.name = slypn-api`). That's enough for #36; we'll fork it and tailor per-route panels there.
- Faro auto-provisions a frontend dashboard tied to `slypn-web`.
- A single starter alert — API 5xx rate > 5% over 5 min — emails you. Configure recipient in **Alerts & IRM → Contact points**.

---

## 6. Cookie-consent gating (UI)

Faro **must not** initialise until the cookie banner's `analytics` consent is granted. The wiring in #34 reads the consent state from `useCookieConsent()` and only constructs the Faro instance when the user has accepted. Until consent, every Faro call is a no-op.

The API side has no cookie consideration — it's server telemetry.

---

## 7. Troubleshooting

- **No traces showing**: check OTLP endpoint URL doesn't have a trailing `/v1/traces`. The wizard's value is the base; the SDK appends the right suffix.
- **No Faro events**: check the browser console for CORS errors; verify the collector URL's `<app-id>` matches the app you created.
- **"Token unauthorised"** on the API: the access policy needs all three of `metrics:write`, `logs:write`, `traces:write` — easy to forget logs.
