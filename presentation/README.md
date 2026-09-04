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
| `T` | Switch between the dark and light themes |
| `P` | Print — lays every slide out for PDF export |

The deck opens **dark** for everyone regardless of the operating system's colour
setting, since that is the design it was built in. Switching to light is remembered
per browser in `localStorage` under `slypn-deck-theme`. Printing is always light on
white, whichever theme is on screen.

Slides are deep-linkable: `index.html#12` opens slide 12.

## Keeping it accurate

The figures in the deck were measured on 2026-09-04, not estimated:

- **Usage** — Azure Monitor metrics on the storage account (capacity, transactions, egress).
- **Cost** — the Azure **Cost Management** query API, grouped by resource group,
  for the 30 days to 2026-09-04. Do **not** use `az consumption usage list` on this
  subscription: it returns records whose `pretaxCost` is the string `"None"`, which
  is easily mistaken for a measured zero.
- **Page weight** — the live site's assets, fetched with compression on.
- **Content volumes** — the public API.
- **Code and tests** — counted from the repository.
- **Screenshots** — captured from the live site at 390px and 1300px wide, then
  palette-optimised. Retake them if the home page changes.

## Third-party marks

The technology logos on the closing slide are inlined SVG paths from
[Simple Icons](https://simpleicons.org) (CC0), except Azure and Playwright, which
come from [Devicon](https://devicon.dev) (MIT). They are rendered monochrome and
used to identify the technologies — nominative use. Each remains the trademark of
its owner, and no endorsement or affiliation is implied.

Cost projections beyond the free tiers are approximate list prices and are labelled as such
in the deck. If you revise the numbers, update the basis note on the costs slide too.
