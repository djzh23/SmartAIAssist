# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

SmartAssistApi is a full-stack AI Agent application:

- **Backend:** ASP.NET Core 9 REST API with Anthropic Claude Tool Calling
- **Frontend:** Blazor WebAssembly — chat UI with session memory
- **Infrastructure:** Docker Compose, GitHub Actions CI

## Solution Structure

```
SmartAssistApi/
├── SmartAssistApi/          ← Backend API
├── SmartAssistApi.Client/   ← Blazor WebAssembly Frontend
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
- NEVER put business logic in Controllers or Blazor pages
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

## Frontend Standards (SmartAssistApi.Client/)

- Pages: Blazor pages in `Pages/` — only UI logic
- Services: HttpClient wrappers in `Services/`
- Models: shared DTOs mirrored from backend
- No direct HttpClient in Pages — always use a service class

## How to Add a New Tool

Read: `docs/playbooks/add-new-tool.md`

## How to Add a New Blazor Page

Read: `docs/playbooks/add-blazor-page.md`

## Testing Standards

- Unit tests: mock all dependencies with Moq
- Test naming: `MethodName_Scenario_ExpectedResult`
- Every new service needs at least 3 tests
- Every new tool needs at least 1 test
