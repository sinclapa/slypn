# SLYPN data model

This document describes the Cosmos DB schema for SLYPN — containers, partition keys, and the rationale for each choice. Bootstrapping logic lives in `src/api/Slypn.Api/Infrastructure/CosmosBootstrapper.cs`.

## Why Cosmos free tier

All data lives in a single Cosmos DB account on the **free tier** (one per Azure subscription, free forever): 1000 RU/s shared across all containers, 25 GB storage. To stay inside it we keep:

- Documents small. Article bodies are bounded at ~10 KB; everything else is well under 1 KB.
- Containers few (one per content type).
- Write rate low. This is a small community site with a few writes per day.

## Containers and partition keys

| Container | Partition key | Why |
|---|---|---|
| `articles` | `/status` | Public reads are heavily biased to `status = "published"`. Partitioning on status makes the most common query a single-partition lookup. Trade-off: cross-partition list when an admin wants "everything by author X, any status" — rare and acceptable. |
| `drafts` | `/authorId` | Drafts are private. Every query either starts with "show me my drafts" (contributor view) or "drafts pending review by X" (admin). Partitioning on `authorId` keeps those single-partition. |
| `events` | `/yearMonth` (e.g. `2026-05`) | Listing events is almost always by date range, and the home page + Events page need "this month + next month". A year-month partition keeps that to one or two partition reads. The API derives `yearMonth` from `startsAt` on write. |
| `resources` | `/category` | The Resources page filters by category (chips). Partitioning matches the access pattern. Small partitions (9 entries today) — fine forever. |
| `newsletters` | `/year` (four digits) | Archive views are by year; ~12 issues per partition per year. Cheap. |
| `members` | `/id` | Single-row reads by member id dominate (auth flows + admin lookup). One row per partition is unusual but correct for this access pattern — we never list "all members in partition X". |

## Trade-offs explicitly accepted

- **`articles` partition on `/status`** creates a hot partition for `published` (the vast majority of reads). For a community site at our scale (hundreds of articles, light traffic) this is fine; if write throughput on `published` ever becomes a problem we'd shard by `yearPublished` or move to a composite key.
- **`events` requires `yearMonth` on write**. Computed in the API, not user-provided. If a write skips it, validation rejects.
- **`members /id` is one row per partition**. Unusual, but it matches every access we need.
- **Shared throughput across containers** (free tier is database-level). Means a hot container can starve quiet ones. With our traffic profile, no risk.

## Bootstrap

`CosmosBootstrapper` is an `IHostedService` registered in `Program.cs`. On startup it:

1. Reads `Cosmos:Endpoint`, `Cosmos:Key`, `Cosmos:Database` from configuration (`local.settings.json` for dev, app settings for prod).
2. If the endpoint/key are missing, **skips silently** and logs a hint — the API still starts with mock data, which is the current state before #14 wires reads to Cosmos.
3. Calls `CreateDatabaseIfNotExistsAsync` and `CreateContainerIfNotExistsAsync` for each entry in the table above.
4. When the endpoint is `localhost`/`127.0.0.1`, accepts the emulator's self-signed certificate.

The bootstrapper is idempotent — safe to call on every startup, in dev or prod.

## Next

- **#12** — a `CosmosService` wrapper exposing typed container handles via DI.
- **#13** — Blob Storage for media (article images).
- **#14** — replace the mock read endpoints with Cosmos reads.
