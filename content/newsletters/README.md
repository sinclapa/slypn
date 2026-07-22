# Newsletter archive (import staging)

The SLYPN newsletter back-catalogue (PDF for 2020–2022, DOCX after), one file per
issue named `YYYY-MM.<ext>`. This folder is a **staging area for a one-time
import**, not where the site serves newsletters from — the canonical home is the
`content` blob container under `newsletters/{id}`, with metadata in the
`newsletters` table (see `Slypn.Api.Models.Newsletter`).

The binary files themselves are **git-ignored** (they live in blob storage after
import); only this README and `manifest.tsv` (provenance: each file → its original
slypn.org.uk source URL) are tracked.

## Re-importing

With Azurite (or a real storage account) running, from `src/api/Slypn.Seed`:

```bash
dotnet run -- --dir <path-to-this-folder> --connection-string "<storage-cs>"
```

Each file is uploaded to `content/newsletters/{id}` and its row upserted into the
`newsletters` table. The import is **idempotent** — ids are deterministic
(`newsletter-YYYY-MM`), so re-running replaces in place rather than duplicating.
Issue metadata (title/summary) is derived from the month, e.g.
`SLYPN newsletter — March 2022.`
