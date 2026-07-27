# SLYPN data model

This document describes the storage schema for SLYPN — tables, partition/row keys, where bodies live, and the rationale for each choice. Bootstrapping logic lives in `src/api/Slypn.Api/Infrastructure/TableBootstrapper.cs`; the data access layer is `src/api/Slypn.Api/Services/ContentRepository.cs`.

## Why Table + Blob storage

All data lives in a single **Azure Storage account** (the same one used for media), split across two cheap services:

- **Azure Table Storage** holds structured metadata, one row per entity. Pennies-per-GB, pay-as-you-go — far cheaper than a provisioned Cosmos DB account for a small community site.
- **Azure Blob Storage** holds large article/draft HTML bodies (one blob per content id) plus media uploads. Table caps a single string property at 64 KB and a whole entity at 1 MB, so rich TipTap HTML lives in blobs instead.

To keep it simple and cheap we:

- Store each entity's metadata as a single `Json` column (System.Text.Json, camelCase) rather than mapping every field to a column.
- Keep article/draft bodies out of the row entirely (blob keyed by content id, so status transitions never move the blob).
- Do list ordering/filtering in memory — Table Storage only orders by PartitionKey + RowKey, and volumes are small.

## Tables, keys, and bodies

| Table | PartitionKey | RowKey | Body in Blob | Why |
|---|---|---|---|---|
| `articles` | `status` | `id` | `content/articles/{id}` | Public reads are heavily biased to `status = "published"`. Partitioning on status makes the common query a single-partition lookup. A `Slug` column is stored alongside the JSON for slug lookups. |
| `drafts` | `authorId` | `id` | `content/drafts/{id}` | Drafts are private; queries are "my drafts" (contributor) or a single draft by id. Partitioning on `authorId` keeps those single-partition. |
| `events` | `yearMonth` (e.g. `2026-05`) | `id` | — | Listing events is by date range; a year-month partition keeps "this month + next" cheap. The API derives `yearMonth` from `startsAt` on write. |
| `resources` | `category` | `id` | — | The Resources page filters by category. Partitioning matches the access pattern. |
| `newsletters` | `"newsletter"` (constant) | `id` | `content/newsletters/{id}` (attached issue file, PDF/DOCX) | Volumes are small (~12 issues/year); a single partition keeps `ListNewsletters` a single-partition scan. |
| `members` | `"member"` (constant) | `id` | — | Member counts are small; a single partition makes `ListMembers` and the email/oid lookups single-partition scans, and point reads by id are PK+RK lookups. |

## Concurrency

Optimistic concurrency uses the **native Table entity ETag**. `ContentRepository` base64-encodes it into the model's `Etag` property (so the `W/"datetime'...'"` value survives the RFC-7232 quoting in the HTTP `ETag`/`If-Match` headers) and decodes the incoming `If-Match` before issuing conditional writes. A mismatch surfaces as `412 Precondition Failed`.

## Trade-offs explicitly accepted

- **`articles` partition on `status`** creates a hot `published` partition (the vast majority of reads). Fine at our scale (hundreds of articles, light traffic).
- **List endpoints fetch bodies.** Article/draft list responses include the body (the approvals queue renders it), so those lists fetch the body blobs in parallel. Cheap at our volumes; could be trimmed to summaries later.
- **Workflow transitions are not atomic.** Submit/publish/revise and an event partition-key change are read→write→delete sequences. Admin actions are rare and re-runs are idempotent (the source is gone, surfacing a clean error). The body blob is keyed by id, so it never moves during a status change.

## Bootstrap

`TableBootstrapper` is an `IHostedService` registered in `Program.cs`. On startup it:

1. Reads `Storage:ConnectionString` from configuration (`local.settings.json` for dev, app settings for prod).
2. If the connection string is missing, **skips silently** and logs a hint — the API still starts and serves reads from mock data.
3. Calls `CreateIfNotExistsAsync` for each table above. The `content` blob container is created lazily by `ContentBodyStore`.

The bootstrapper is idempotent — safe to call on every startup, in dev or prod.
