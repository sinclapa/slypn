# SLYPN presentation

A self-contained slide deck covering the site's features, architecture, infrastructure,
CI/CD pipeline, observability and running costs.

## Viewing it

Open [`index.html`](index.html) in any browser — there is no build step. The only external
dependencies are two webfonts; the screenshots in [`img/`](img) are referenced relatively,
so keep the folder together when you move it.

Published at **<https://sinclapa.github.io/slypn/>** by
[`.github/workflows/pages.yml`](../.github/workflows/pages.yml) on every push to `main`
that touches this folder.

| Key | Action |
|---|---|
| `→` `space` `PageDown` | Next slide |
| `←` `PageUp` | Previous slide |
| `Home` / `End` | First / last slide |
| `P` | Print — lays every slide out for PDF export |

Slides are deep-linkable: `index.html#12` opens slide 12.

## Keeping it accurate

The figures in the deck were measured on 2026-09-04, not estimated:

- **Usage** — Azure Monitor metrics on the storage account (capacity, transactions, egress).
- **Cost** — `az consumption usage list` over the preceding 32 days.
- **Page weight** — the live site's assets, fetched with compression on.
- **Content volumes** — the public API.
- **Code and tests** — counted from the repository.
- **Screenshots** — captured from the live site at 390px and 1300px wide, then
  palette-optimised. Retake them if the home page changes.

Cost projections beyond the free tiers are approximate list prices and are labelled as such
in the deck. If you revise the numbers, update the basis note on the costs slide too.
