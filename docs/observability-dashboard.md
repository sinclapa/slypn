# SLYPN baseline dashboard + alert

Once data is flowing from #34 (Faro) and #35 (OpenTelemetry .NET → OTLP), build this dashboard in Grafana Cloud. **Dashboards → New → New dashboard** and add each panel below.

> All API queries assume `service_name="slypn-api"` (set on the OTel resource). All Faro queries assume `app.name="slypn-web"` (set in `setupFaro()`).

---

## API panels (Prometheus + Tempo)

### 1. Request rate per route

**Visualisation:** Time series · **Stack:** off

Datasource: **grafanacloud-slypn-prom** (Prometheus)

```promql
sum by (http_route) (
  rate(http_server_request_duration_seconds_count{service_name="slypn-api"}[5m])
)
```

Group by `http_route` so each endpoint is its own line.

### 2. p95 latency per route

**Visualisation:** Time series · **Unit:** seconds (s)

```promql
histogram_quantile(
  0.95,
  sum by (le, http_route) (
    rate(http_server_request_duration_seconds_bucket{service_name="slypn-api"}[5m])
  )
)
```

### 3. Error rate (5xx) per route

**Visualisation:** Time series · **Unit:** percent (0–100)

```promql
100 *
  (sum by (http_route) (
    rate(http_server_request_duration_seconds_count{service_name="slypn-api",http_response_status_code=~"5.."}[5m])
  ))
  /
  (sum by (http_route) (
    rate(http_server_request_duration_seconds_count{service_name="slypn-api"}[5m])
  ) > 0)
```

### 4. Cosmos call duration (p95)

**Visualisation:** Time series · **Unit:** seconds (s)

Tempo gathers Cosmos SDK spans (we registered `Azure.*` as a source). Use this **TraceQL** in **Explore → Tempo**:

```traceql
{ resource.service.name="slypn-api" && span:db.system="cosmosdb" } | quantile_over_time(span:duration, 0.95) by (span:db.cosmosdb.container)
```

(If TraceQL aggregations aren't enabled on the free tier, fall back to filtering spans by `db.system=cosmosdb` and reading p95 from the histogram in Explore.)

### 5. Runtime — GC + GC heap

**Visualisation:** Time series

```promql
sum(rate(process_runtime_dotnet_gc_collections_count_total{service_name="slypn-api"}[5m])) by (generation)
```

```promql
process_runtime_dotnet_gc_heap_size_bytes{service_name="slypn-api"}
```

---

## UI panels (Faro → Loki + Mimir)

### 6. Core Web Vitals — p75 LCP / FID / CLS

Faro emits these as metric series via Mimir. Datasource: **grafanacloud-slypn-prom**.

```promql
histogram_quantile(0.75, sum(rate(faro_web_vitals_lcp_bucket{app_name="slypn-web"}[5m])) by (le))
```

```promql
histogram_quantile(0.75, sum(rate(faro_web_vitals_fid_bucket{app_name="slypn-web"}[5m])) by (le))
```

```promql
histogram_quantile(0.75, sum(rate(faro_web_vitals_cls_bucket{app_name="slypn-web"}[5m])) by (le))
```

LCP / FID are seconds, CLS is unitless (score).

### 7. UI errors per minute (by error name)

Datasource: **grafanacloud-slypn-logs** (Loki).

```logql
sum by (event_type, error_name) (
  rate({app_name="slypn-web"} |= "error" | json [1m])
)
```

### 8. UI session count

```promql
sum(faro_sessions_total{app_name="slypn-web"})
```

---

## Variables

Add a dashboard variable so the panels filter by environment.

- **Name:** `env`
- **Type:** Query
- **Datasource:** Prometheus
- **Query:** `label_values(http_server_request_duration_seconds_count, deployment_environment)`
- Then in each query, append `,deployment_environment=~"$env"` to the selector.

---

## Alert — API 5xx rate > 5% for 5 min

**Alerting → Alert rules → + New alert rule.**

1. **Grafana managed alert rule**.
2. **Query A (Prometheus)** — error rate, the same expression as panel 3 but summed across routes:
   ```promql
   100 *
     sum(rate(http_server_request_duration_seconds_count{service_name="slypn-api",http_response_status_code=~"5.."}[5m]))
     /
     sum(rate(http_server_request_duration_seconds_count{service_name="slypn-api"}[5m]))
   ```
3. **Reduce / Threshold:** Last value · **Is above 5** (= 5 %).
4. **Evaluation:** every **1 min** · **For** 5 min (so the alert fires only when the rate has stayed above 5 % for five minutes — avoids flapping on a single bad request).
5. **Folder:** `SLYPN` · **Group:** `api-availability`.
6. **Annotations**:
   - Summary: `SLYPN API 5xx rate above 5%`.
   - Description: `Errors per second is {{ $value | printf "%.2f" }}% over the last 5m. Investigate slypn-api logs (Explore → Loki: {service_name="slypn-api"} |= "Error").`
7. **Labels**:
   - `team=slypn`
   - `severity=warning`

### Contact point + notification policy

1. **Alerting → Contact points → + Add contact point** → name `slypn-email` → Email → your address → **Test** to verify the SMTP relay works.
2. **Notification policies → + Edit policy** → matcher `team=slypn` → contact point `slypn-email`.

Done. The alert will email you when the API 5xx rate has been above 5 % for 5 min, recover automatically when it drops back below.

---

## Out of scope (call out if it matters)

- **Per-tenant** dashboarding — single SLYPN tenant for now.
- **Synthetic checks** — Grafana Cloud has synthetic monitoring; could add a sign-in synthetic later but not for v1.
- **Pyroscope / continuous profiling** — free tier covers it but adds infrastructure overhead; skip for v1.

---

## Maintenance

If you change resource attribute names (e.g. rename `slypn-api` → something else), every PromQL in this doc must change too. Rule of thumb: the OTel resource name in `OtelOptions.cs` and the Faro `app.name` in `lib/faro.ts` are the source of truth; the dashboard mirrors them.
