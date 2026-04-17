# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

SmartAssistApi is the **ASP.NET Core 9 REST API** for the SmartAssist product.

- **Production frontend:** **React** (separate repo: `SmartAssist-react`, Vite) — this is what ships to users.
- **Optional in this repo:** `SmartAssistApi.Client/` — **Blazor WebAssembly** sample/legacy UI used for early testing; not the live product UI.
- **Infrastructure:** Docker Compose, GitHub Actions CI

## Solution Structure

```
SmartAssistApi/
├── SmartAssistApi/          ← Backend API (primary focus)
├── SmartAssistApi.Client/   ← Blazor WASM (optional / legacy — do not assume production traffic)
├── SmartAssistApi.Tests/    ← xUnit Tests
├── docs/
│   ├── agents/              ← Claude Code Agents
│   ├── playbooks/           ← Reproducible workflows
│   └── skills/              ← Reusable skill definitions
├── CLAUDE.md
└── docker-compose.yml
```

## Non-Negotiable Rules

- ALWAYS write tests before committing any feature
- ALWAYS run `dotnet test` — zero failures before commit
- ALWAYS use conventional commits (feat/fix/refactor/chore/docs)
- NEVER hardcode config — use appsettings.json + IConfiguration
- NEVER put business logic in Controllers or (if touching Blazor) Blazor pages
- When adding a feature: read the relevant playbook in `docs/playbooks/`
- When asked to add a tool: use `docs/agents/tool-builder-agent.md`
- When asked to write tests: use `docs/agents/testing-agent.md`

## Commit Convention

```
feat: add X feature
fix: resolve Y bug
refactor: restructure Z without behavior change
chore: update dependencies / docker / config
docs: update documentation
test: add or fix tests
```

## Backend Standards (SmartAssistApi/)

- Controllers: only HTTP routing + validation + error handling
- Services: all business logic
- Tools: static classes in `Services/Tools/` — one file per tool
- Models: C# records for DTOs
- Config: all values in `appsettings.json` under named sections
- **CORS policy name:** `SmartAssistWeb` (React and other allowed web origins — see `Program.cs`)

## Production React frontend (SmartAssist-react)

- Lives outside this repo; consumes this API via `VITE_API_BASE_URL` (or host proxy).
- CORS / `FRONTEND__BASEURL` / `CORS_ALLOWED_ORIGINS` on the API host must include the deployed React origin(s).

## Optional: Blazor client (SmartAssistApi.Client/)

- Pages: Blazor pages in `Pages/` — only UI logic
- Services: HttpClient wrappers in `Services/`
- Models: shared DTOs mirrored from backend
- No direct HttpClient in Pages — always use a service class

## How to Add a New Tool

Read: `docs/playbooks/add-new-tool.md`

## How to Add a New Blazor Page (only if you touch the WASM client)

Read: `docs/playbooks/add-blazor-page.md`

## Testing Standards

- Unit tests: mock all dependencies with Moq
- Test naming: `MethodName_Scenario_ExpectedResult`
- Every new service needs at least 3 tests
- Every new tool needs at least 1 test
