# Local emulators

`scripts/start.ps1` launches two Docker containers alongside the Vue dev server and the Functions host:

| Container | Image | Purpose | Ports |
|---|---|---|---|
| `slypn-azurite` | `mcr.microsoft.com/azure-storage/azurite:latest` | Blob (+ queue + table) emulator | `10000`/`10001`/`10002` |
| `slypn-cosmos`  | `mcr.microsoft.com/cosmosdb/linux/azure-cosmos-emulator:vnext-preview` | Cosmos DB emulator | `8081` |

## Lifecycle

- `scripts/start.ps1` — creates the containers on first run, then `docker start`s them on subsequent runs. **Data persists** between start/stop cycles.
- `scripts/stop.ps1` — stops the containers (preserving data). Pass `-KeepEmulators` to leave them running between stops.
- `scripts/clean.ps1` — removes the containers, wiping all emulator data.

## Cosmos emulator TLS cert

The Cosmos emulator serves over HTTPS with a self-signed certificate. The API and the seed CLI handle this in code:

> when `Endpoint` contains `localhost` or `127.0.0.1`, the `CosmosClient` is built with a `HttpClient` that bypasses cert validation (`HttpClientHandler.DangerousAcceptAnyServerCertificateValidator`).

That means **no OS-level cert install is required** for the SLYPN code paths. If you want to hit `https://localhost:8081/_explorer/index.html` in a browser, or use curl/Postman, you'll either need to install the emulator's cert (see [Microsoft Learn — Install the certificate](https://learn.microsoft.com/azure/cosmos-db/how-to-develop-emulator)) or accept the cert warning in your tool of choice.

## Azurite

Azurite is HTTP only on the emulator endpoints. The `Storage__ConnectionString` in `local.settings.sample.json` includes `BlobEndpoint=http://127.0.0.1:10000/devstoreaccount1` — no cert wrangling needed.

## Seeding

After `start.ps1` (and the Cosmos emulator is ready), `scripts/seed.ps1` upserts the sample newsletter from `brief/SLYPN_Newsletter_MAY_2026.docx` into the `newsletters` container.

## Skipping emulators

`scripts/start.ps1 -NoEmulators` boots only vite + func and skips the Docker containers — handy if you've pointed the API at a real Cosmos / Storage account via env vars, or are working on UI-only changes.
