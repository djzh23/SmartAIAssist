# Playbook: Add New Tool

## Step 1 — Create tool file

Path: `SmartAssistApi/Services/Tools/{Name}Tool.cs`

Use template from `docs/agents/tool-builder-agent.md`

## Step 2 — Register in AgentService.cs

Add `Tool.FromFunc()` entry to the tools list

## Step 3 — Write unit test

Path: `SmartAssistApi.Tests/{Name}ToolTests.cs`

Test: valid input returns non-empty string

## Step 4 — Run tests

```bash
dotnet test
```

Must be green before continuing.

## Step 5 — Test via Docker

```bash
docker compose up --build -d
curl -X POST http://localhost:8080/api/agent/ask \
  -H "Content-Type: application/json" \
  -d "{\"message\": \"test the new tool\"}"
```

## Step 6 — Commit

```bash
git add .
git commit -m "feat: add {name} tool"
```
