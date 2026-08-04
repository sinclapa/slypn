# SLYPN — South London Younger Parkinson's Network

A community website for the South London Younger Parkinson's Network, affiliated with [Parkinson's UK](https://www.parkinsons.org.uk/). SLYPN supports working-age people living with Parkinson's in South London through coffee meet-ups, drinks, activities, and fundraising events.

## Status

Early development. The build is tracked in **[GitHub Project #2](https://github.com/users/sinclapa/projects/2)** as six phases (scaffold → persistence → auth → authoring → observability → deployment). See [`BRIEF.md`](BRIEF.md) for the original brief.

## Tech stack

| Layer | Choice |
|---|---|
| Frontend | Vue 3 + TypeScript + Vite + Pinia + TailwindCSS |
| API | ASP.NET Core 8 isolated-worker Functions (deployed as SWA managed Functions) |
| Hosting | Azure Static Web Apps (Free tier) |
| Data | Azure Table Storage (metadata) + Azure Blob Storage (bodies + media) |
| Auth | Entra External ID (MSAL.js + JWT bearer) |
| CMS | Custom in-app editor (TipTap) with draft/in-review/published workflow |
| Observability | Grafana Cloud — Faro Web SDK (UI) + OpenTelemetry .NET OTLP (API) |
| IaC | Bicep |
| CI/CD | GitHub Actions (SWA action provides PR preview URLs) |

## Repository layout

```
slypn/
├── BRIEF.md              # original brief
├── branding/             # logos and brand assets
├── brief/                # source material (e.g. sample newsletter)
├── docs/                 # setup notes (auth, observability, custom domain, …)
├── infra/                # Bicep IaC
├── scripts/              # PowerShell setup/start/stop helpers
├── src/
│   ├── web/              # Vue 3 + TS frontend
│   └── api/Slypn.Api/    # .NET 8 isolated Functions API
└── .github/workflows/    # GitHub Actions CI + deploy
```

## Local development

Local dev tooling lands in [issue #7 (`1.6 PowerShell setup/start/stop scripts`)](https://github.com/sinclapa/slypn/issues/7). Once merged, the flow will be:

```powershell
.\scripts\setupLocal.ps1   # one-time: install prereqs, restore packages
.\scripts\startLocal.ps1   # boot vite (web) + func (api) locally
.\scripts\stopLocal.ps1    # tear down
.\scripts\testLocal.ps1    # run API + UI tests with branch/total coverage report
```

Prereqs (target): PowerShell 7+, Node 20+, .NET 8 SDK, Azure Functions Core Tools v4, Docker (for the Azurite storage emulator).

## Contributing

The site has two authoring roles (lands in Phase 3):
- **Admin** — manages all content and members, approves submissions.
- **Contributor** — writes and edits their own articles; submissions need admin approval before publication.

Browser-based editing with autosave is implemented in Phase 4.

## Licence

TBD.
