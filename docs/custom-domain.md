# SLYPN custom domain — DNS + SSL

After the first Bicep deploy (#42) the SWA is reachable at the auto-generated `https://<swa-default-hostname>.azurestaticapps.net`. This doc swaps that for a real domain — typically `slypn.org.uk` — with a free auto-renewed SSL cert. Done once per environment.

> **Prerequisites**
> - You own the domain. Registrar access to edit DNS records.
> - No tier requirement — Free-tier SWA supports up to 2 custom domains (Standard/Premium raise that limit if you need more).
> - You are signed in as Owner or Contributor on the SWA resource.

---

## 1. Pick the hostnames

| Hostname | What it serves | DNS record type |
|---|---|---|
| `slypn.org.uk` | Production root — main site | `ALIAS` / `ANAME` / apex `A` (depends on registrar) |
| `www.slypn.org.uk` | Production www — redirected to apex | `CNAME` → `<swa-default-hostname>.azurestaticapps.net` |
| `dev.slypn.org.uk` | Dev SWA (optional) | `CNAME` → `<dev-swa-hostname>.azurestaticapps.net` |

Some registrars don't support `ALIAS`/`ANAME` on the apex — in that case either:
- Use `www.slypn.org.uk` as the canonical hostname and `301`-redirect the apex via your registrar's URL forwarder.
- Or move DNS to a provider that supports apex aliasing (Cloudflare DNS, Azure DNS Zone, AWS Route 53, etc.).

---

## 2. Add the custom domain in Azure

Portal route:
1. SWA → **Custom domains → + Add**.
2. **Custom domain on an existing domain** → enter `slypn.org.uk`.
3. **Validation type**:
   - **TXT** if you're configuring the apex without a CNAME (Azure issues a `_dnsauth` TXT record to add).
   - **CNAME** if it's a sub-domain like `www`.
4. Copy the validation value Azure shows.

Or via CLI:

```bash
az staticwebapp hostname set \
  -n swa-slypn-prod -g rg-slypn-prod \
  --hostname slypn.org.uk \
  --validation-method dns-txt-token
```

---

## 3. Add the DNS records

At your registrar, add:

| Type | Name | Value | TTL |
|---|---|---|---|
| `TXT` | `_dnsauth.slypn.org.uk` | `<token from step 2>` | 300 |
| `ALIAS` / `ANAME` / `A` | `@` (apex) | `<swa-default-hostname>.azurestaticapps.net` | 300 |
| `CNAME` | `www` | `<swa-default-hostname>.azurestaticapps.net` | 300 |

For Cloudflare, set the records as **DNS only** (grey cloud) — proxying through Cloudflare's orange cloud will mask the SWA hostname and break validation. You can re-enable proxying after validation, but be aware SWA's managed SSL won't apply through Cloudflare's edge then; Cloudflare's SSL takes over and Faro's CSP rules need to allow the Cloudflare host.

---

## 4. Wait for validation + SSL

- DNS propagation: typically a few minutes; can be up to 48 hours with stubborn TTLs.
- Once the portal shows **Ready**, the SWA's managed SSL cert auto-provisions (a few more minutes). The cert renews automatically 30 days before expiry. You'll see it in the Custom domains list as **Healthy / Issued**.

Hit `https://slypn.org.uk/` — should serve the site with a valid cert and no browser warning.

---

## 5. Update downstream config

When the production hostname is live, three other systems need to know.

### Entra External ID (sections 6.2 + 6.4 of auth-setup.md)

1. **App registrations → slypn-spa → Authentication → Add redirect URI**: `https://slypn.org.uk/auth/callback`.
2. **Add logout URL**: `https://slypn.org.uk/`.
3. Keep the localhost URIs — they're still needed for local dev.

### Grafana Faro (section 3 of observability-setup.md)

In the Faro app's **Allowed origins**, add:

```
https://slypn.org.uk
https://www.slypn.org.uk
```

Browser CORS will block the SDK from posting without this.

### Vue env vars (deployment-secrets.md §4)

`VITE_FARO_URL` doesn't change. But if you've added the new domain as a separate Faro app (one per environment), update `VITE_FARO_URL` in SWA app settings and redeploy.

---

## 6. Apex-only deployments (alternative)

If you'd rather skip the `www` host entirely:

1. Only add the apex hostname in step 2.
2. At the registrar, add a `301` redirect from `www.slypn.org.uk` → `https://slypn.org.uk/`.

Most registrars support this (Cloudflare via "Page rules" / "Bulk redirects", Namecheap via "URL Forwarding", etc.).

---

## 7. Removing or moving a hostname

```bash
az staticwebapp hostname delete \
  -n swa-slypn-prod -g rg-slypn-prod \
  --hostname slypn.org.uk
```

Then remove the DNS records. The managed SSL cert auto-cleans within a day.

---

## 8. Troubleshooting

- **"DNS validation failed"** → the TXT record hasn't propagated yet. Wait 5-10 min, click Re-validate.
- **Apex `A` record but Azure still shows "DNS_VALIDATION"** → your registrar's apex isn't pointing where you think; `dig slypn.org.uk` to confirm.
- **`SSL certificate is invalid`** → the cert provisioned but DNS is still pointing somewhere else (e.g. an old A record). Remove competing records.
- **Sign-in fails after switching to the custom domain** → you forgot section 5's Entra redirect URI step. Add it and clear the Entra session cache (incognito tab).
- **Faro stops posting events** → CORS blocked by Allowed Origins; add the new hostname.
