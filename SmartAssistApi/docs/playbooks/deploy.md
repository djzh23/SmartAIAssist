# Playbook: Public Release + Deploy

Follow this checklist before pushing a public GitHub repository.

## 1) Security and repository hygiene

1. Ensure secrets are not in tracked files.
2. Keep `.env` local-only and commit only `.env.example`.
3. Verify `.gitignore` excludes:
   - `bin/`, `obj/`, `.vs/`
   - `*.user`
   - `.env` and other local secret files

Quick scan:

```bash
rg -n "sk_[A-Za-z0-9]{20,}|ANTHROPIC_API_KEY=|ELEVENLABS_API_KEY="
```

If anything sensitive appears, remove it before commit.

## 2) Local validation

Run API build:

```bash
dotnet build SmartAssistApi/SmartAssistApi.csproj
```

Run client compile:

```bash
dotnet msbuild SmartAssistApi.Client/SmartAssistApi.Client.csproj /t:CoreCompile
```

Run tests (if your local policy allows test assembly loading):

```bash
dotnet test SmartAssistApi.Tests/SmartAssistApi.Tests.csproj
```

## 3) Docker sanity check (local)

```bash
docker compose -f SmartAssistApi/docker-compose.yml up --build -d
curl http://localhost:8080/api/agent/health
```

Expected: HTTP `200` with `status: ok`.

## 4) Backend deploy on Render

Use the `SmartAssistApi` folder as service root (Docker deploy).

Required environment variables in Render:

- `ANTHROPIC_API_KEY`
- `ELEVENLABS_API_KEY`
- `ELEVENLABS_VOICE_ES` (optional but recommended)
- `ELEVENLABS_VOICE_DEFAULT` (optional)
- `CORS_ALLOWED_ORIGINS` (comma-separated; include your Blazor domain)

Notes:

- App supports Render `PORT` automatically.
- CORS accepts local dev origins plus configured production origins.

## 5) Blazor client deploy

This is a standalone WASM app. Deploy static files from `SmartAssistApi.Client/wwwroot` build output.

Before production build, set API base URL in:

- `SmartAssistApi.Client/wwwroot/appsettings.json`

Set:

```json
{
  "ApiBaseUrl": "https://<your-render-service>.onrender.com/"
}
```

Then build/publish client and deploy to your static host.

## 6) Final commit and push

```bash
git add .
git commit -m "chore: pre-public cleanup and deploy readiness"
git push
```

