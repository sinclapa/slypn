# Local emulators

`scripts/start.ps1` launches one Docker container alongside the Vue dev server and the Functions host:

| Container | Image | Purpose | Ports |
|---|---|---|---|
| `slypn-azurite` | `mcr.microsoft.com/azure-storage/azurite:latest` | Blob + queue + **table** emulator | `10000`/`10001`/`10002` |

Azurite emulates both services SLYPN uses — Table Storage (metadata) and Blob Storage (article/draft bodies + media) — so no separate database emulator is needed.

## Lifecycle

- `scripts/start.ps1` — creates the container on first run, then `docker start`s it on subsequent runs. **Data persists** between start/stop cycles.
- `scripts/stop.ps1` — stops the container (preserving data). Pass `-KeepEmulators` to leave it running between stops.
- `scripts/clean.ps1` — removes the container, wiping all emulator data.

## Connection

Azurite is HTTP only. The `Storage__ConnectionString` in `local.settings.sample.json` pins all three endpoints to the local Azurite ports:

```
BlobEndpoint=http://127.0.0.1:10000/devstoreaccount1;
QueueEndpoint=http://127.0.0.1:10001/devstoreaccount1;
TableEndpoint=http://127.0.0.1:10002/devstoreaccount1;
```

No cert wrangling needed — it's plain HTTP. Inspect the data with [Azure Storage Explorer](https://azure.microsoft.com/products/storage/storage-explorer) by connecting to the local emulator.

## Seeding

After `start.ps1` (and Azurite is ready), `scripts/seed.ps1` upserts the sample newsletter from `brief/SLYPN_Newsletter_MAY_2026.docx` into the `newsletters` table.

## Skipping emulators

`scripts/start.ps1 -NoEmulators` boots only vite + func and skips the Docker container — handy if you've pointed the API at a real Storage account via `Storage__ConnectionString`, or are working on UI-only changes.
