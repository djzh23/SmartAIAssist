# PrivatePrep — Backend

ASP.NET Core 9 API for **PrivatePrep** (domain [betweenatna.de](https://www.betweenatna.de)), an AI-powered career workspace for job seekers.

Hosted: **[smartassist-api.onrender.com](https://smartassist-api.onrender.com)**  
Frontend: [github.com/djzh23/SmartAssist-react](https://github.com/djzh23/SmartAssist-react)

---

## What this API does

- **AI agent** — routes chat by tool/mode, builds system prompts, streams replies (SSE). Primary LLM is **Groq**; Anthropic Claude is configured as fallback (disabled in production).
- **Career profile** — onboarding, skills, CV text/PDF parse, target jobs, anonymous CV summaries (`/api/profile`).
- **Chat sessions** — session index, order, transcripts (`/api/sessions`).
- **Chat notes** — saved assistant snippets (`/api/chat-notes`).
- **Job applications** — pipeline CRUD, cover letter, interview notes, link to a chat session.
- **CV.Studio** — resumes, templates, snapshot versions, categories, PDF/DOCX export, quota tracking.
- **Learning memory** — insights extracted from chats; vector store (pgvector + ONNX embeddings) for career memory.
- **Usage & billing** — Clerk JWT auth, daily message limits per plan, Stripe checkout/webhooks/portal.
- **Admin** — usage/token/RAG dashboards and Redis→Postgres backfills (allow-listed Clerk user ids).
- **Speech** — TTS via **Azure Speech** (`ISpeechService`). An ElevenLabs implementation still exists in the repo but is **not** registered.

---

## Runtime topology

```
Browser (betweenatna.de / Clerk)
        │  Bearer JWT
        ▼
SmartAssistApi (Render, Docker)
        ├── Supabase PostgreSQL   (production: profiles, sessions, applications,
        │                          notes, usage, CV.Studio, career_memory)
        ├── Upstash Redis REST    (always used for some caches; local default storage)
        ├── Groq                  (primary chat completions)
        ├── Anthropic Claude      (fallback; AllowAnthropicFallback=false in Production)
        ├── Clerk JWKS            (JWT verify; CLERK__ISSUER required outside Development)
        └── Azure Speech          (TTS)
```

**Production storage** (`appsettings.Production.json`): Postgres is on for chat notes, applications, career profile, chat sessions, learning memory, token usage, and daily usage. Redis remains for prompt-cache / some Stripe audit keys.

**Development default**: all of those feature stores use Redis unless you set a Supabase connection and flip `DatabaseFeatures`.

If Postgres is requested but the connection string is missing/invalid, several features **degrade to Redis** and advertise that via `X-*-Degraded` response headers.

---

## API overview

Auth: send `Authorization: Bearer <Clerk session JWT>` on user-scoped routes. Missing/invalid JWT is treated as **anonymous** (IP-scoped), not as a hard 401 on every endpoint — profile/sessions/applications still return 401 when a signed-in user is required.

Health: `GET /api/health` (Upstash + optional Postgres) and `GET /api/agent/health` (liveness JSON, no DB).

### Agent / chat
| Method | Endpoint | Notes |
|---|---|---|
| POST | `/api/agent/stream` | SSE stream |
| POST | `/api/agent/ask` | JSON completion |
| POST | `/api/agent/demo` | Public landing demo (no Clerk; per-IP cap) |
| POST | `/api/agent/context` | Persist session/tool context |
| GET | `/api/agent/context/{sessionId}/{toolType}` | Load context |
| POST | `/api/agent/speak` | TTS alias |
| GET | `/api/agent/usage` | Today’s usage + plan |
| GET | `/api/agent/health` | `{ status, timestamp }` |
| POST | `/api/jobs/preview` | Job-ad preview for context UI |
| GET | `/api/skills` | Skill registry for the chat toolbar |

### Career profile
| Method | Endpoint |
|---|---|
| GET | `/api/profile` |
| PUT | `/api/profile` |
| POST | `/api/profile/onboarding` |
| POST | `/api/profile/onboarding/skip` |
| GET/PUT | `/api/profile/onboarding/draft` |
| POST | `/api/profile/onboarding/coach-tour/done` |
| PUT | `/api/profile/skills` |
| POST | `/api/profile/cv` |
| POST | `/api/profile/cv/upload-pdf` |
| POST | `/api/profile/cv/anonymous-summary` |
| POST | `/api/profile/target-jobs` |
| DELETE | `/api/profile/target-jobs/{jobId}` |

### Sessions, notes, learning
| Method | Endpoint |
|---|---|
| GET/POST | `/api/sessions` |
| PUT | `/api/sessions/order` |
| POST | `/api/sessions/transcripts` |
| GET/PUT | `/api/sessions/{sessionId}/transcript` |
| PATCH/DELETE | `/api/sessions/{sessionId}` |
| GET/POST | `/api/chat-notes` |
| GET/PUT/DELETE | `/api/chat-notes/{noteId}` |
| GET | `/api/learning/insights` |
| PATCH | `/api/learning/insights/{insightId}` |
| POST | `/api/learning/insights/{insightId}/resolve` |

Insights are **created by the agent pipeline**, not by a public `POST /api/learning/insights` (the React client currently still calls that create URL — it is not implemented on this API).

### Job applications
| Method | Endpoint |
|---|---|
| GET/POST | `/api/applications` |
| GET | `/api/applications/{id}` |
| PUT | `/api/applications/{id}/status` |
| PUT | `/api/applications/{id}/cover-letter` |
| PUT | `/api/applications/{id}/interview-notes` |
| PUT | `/api/applications/{id}/link-session` |
| DELETE | `/api/applications/{id}` |

Pipeline statuses: `draft` → `applied` → `phoneScreen` → `interview` → `assessment` → `offer`, plus archive `accepted` / `rejected` / `withdrawn`.

### CV.Studio
| Method | Endpoint |
|---|---|
| GET/POST | `/api/cv-studio/resumes` |
| DELETE | `/api/cv-studio/resumes` (delete all) |
| GET/PUT/DELETE | `/api/cv-studio/resumes/{id}` |
| POST | `/api/cv-studio/resumes/templates/{templateKey}` |
| PATCH | `/api/cv-studio/resumes/{id}/link-application` |
| PATCH | `/api/cv-studio/resumes/{id}/notes` |
| GET/POST | `/api/cv-studio/resumes/{id}/versions` |
| GET/PUT/DELETE | `/api/cv-studio/resumes/{id}/versions/{versionId}` |
| POST | `/api/cv-studio/resumes/{id}/versions/{versionId}/restore` |
| GET | `/api/cv-studio/resumes/{id}/pdf` |
| GET | `/api/cv-studio/resumes/{id}/docx` |
| GET | `/api/cv-studio/resume-templates` |
| GET/POST | `/api/cv-studio/categories` |
| PATCH/DELETE | `/api/cv-studio/categories/{id}` |
| PUT | `/api/cv-studio/categories/order` |
| PUT | `/api/cv-studio/categories/assignments/{resumeId}` |
| GET | `/api/cv-studio/pdf-exports` |
| DELETE | `/api/cv-studio/pdf-exports/{id}` |

PDF generation is **QuestPDF**. PdfPig is used to extract text from uploaded CV PDFs.

### Payments
| Method | Endpoint |
|---|---|
| POST | `/api/stripe/checkout` |
| POST | `/api/stripe/portal` |
| GET | `/api/stripe/confirm-plan` |
| POST | `/api/stripe/sync-plan` |
| POST | `/api/stripe/webhook` |
| GET | `/api/stripe/plans` |
| GET | `/api/stripe/debug/me` | gated by `Stripe:EnableDebugEndpoint` |

### Speech
| Method | Endpoint |
|---|---|
| POST | `/api/speech/tts` |
| POST | `/api/speech/demo-tts` |

### Admin (`ADMIN_USER_IDS` / config allow-list)
Dashboard, usage, token and RAG stats, plus Redis→Postgres backfill routes under `/api/admin/migrations/...`.

---

## Architecture (this repo)

```
SmartAssistApi/
├── Controllers/           Agent, Profile, Sessions, ChatNotes, Applications,
│                          Learning, Jobs, Skills, Speech, Stripe, Admin,
│                          CvStudioResumes / Categories / PdfExports / ResumeTemplates
├── Middleware/            UserResolutionMiddleware, CvStudio exceptions
├── Services/              Agent, prompts, usage, Clerk, Stripe, Azure TTS,
│                          Postgres/Redis pairs per feature, embeddings, Groq
├── Services/Tools/        JobAnalyzer, LanguageLearning, Translation,
│                          Summary, Weather, Joke
├── Services/VectorStore/  career_memory ingest/retrieve (pgvector)
├── Data/                  SmartAssistDbContext + SQL migrations (embedded)
├── Health/                Upstash (+ optional EF Postgres) checks
└── vendor/cv-studio/      Resume domain, QuestPDF, OpenXML DOCX, EF schema
```

Startup **blocks on migrations**: CV.Studio `Database.MigrateAsync()` and `SmartAssistMigrationRunner` (embedded `Migrations/*.sql`). If Supabase is paused or `DATABASE_URL` is wrong, the process never becomes healthy — the API looks “hung” to the frontend.

`Clerk:Issuer` is **mandatory outside Development**. Without it the host throws at boot.

---

## Tech stack

| Area | Technology |
|---|---|
| Framework | ASP.NET Core 9 / C# |
| Primary LLM | Groq (`llama-3.3-70b-versatile` by default) |
| Fallback LLM | Anthropic Claude Sonnet + Haiku (off in Production) |
| Database | Supabase PostgreSQL + pgvector |
| Cache | Upstash Redis REST |
| Auth | Clerk JWT (JWKS) |
| Payments | Stripe |
| TTS | Azure Cognitive Speech (`AZURE_SPEECH_KEY`) |
| CV PDF | QuestPDF |
| CV text extract | PdfPig |
| Embeddings | ONNX (`Models/model-2.onnx`) |
| Tests | xUnit (`SmartAssistApi.Tests`) |
| Hosting | Render (Docker), deploy hook from GitHub Actions on `main` |

---

## AI modes (enabled)

| UI / `toolType` | Behaviour |
|---|---|
| Career Coach (`general`) | Open career advice |
| Job Analysis (`jobanalyzer`) | Keywords, gaps, CV tips from a job ad |
| Interview Prep (`interview` / `interviewprep`) | STAR-style practice |
| Language Learning (`language`) | Target language + translation + tip |
| Programming (`programming`) | Code help, Markdown |

Disabled/beta in `SkillRegistry` (not served): cover letter, salary coach, LinkedIn optimizer.

---

## Subscription plans

Daily **chat** limits (`UsageService.GetDailyLimit`):

| Plan | Messages / day |
|---|---|
| Anonymous | 2 |
| Free (registered) | 20 |
| Premium / Starter | 200 |
| Pro | Unlimited (`int.MaxValue`) |

Landing demo agent uses a separate per-IP counter (10 requests/day in code).

---

## Local development

**Requirements:** .NET 9 SDK

```bash
git clone https://github.com/djzh23/SmartAIAssist.git
cd SmartAIAssist
dotnet run --project SmartAssistApi
```

`launchSettings.json` binds **`http://localhost:5108`** (not 5194). Point the React Vite proxy (`VITE_PROXY_TARGET`) at that URL.

For local secrets prefer User Secrets or `appsettings.Development.json`. Keys the app actually reads:

```json
{
  "Anthropic": { "ApiKey": "sk-ant-..." },
  "Groq": { "ApiKey": "gsk_..." },
  "ConnectionStrings": { "Supabase": "Host=...;Database=...;Username=...;Password=..." },
  "Upstash": { "RestUrl": "https://...", "RestToken": "..." },
  "Clerk": { "Issuer": "https://your-instance.clerk.accounts.dev" },
  "Stripe": { "SecretKey": "sk_test_...", "WebhookSecret": "whsec_..." }
}
```

Optional: `AZURE_SPEECH_KEY`, `DATABASE_URL` (overrides empty JSON), `GROQ_API_KEY`, `CLERK__ISSUER`, `FRONTEND__BASEURL`, `CORS_ALLOWED_ORIGINS`, `ADMIN_USER_IDS`.

On Render, map `UPSTASH_REDIS_REST_URL` / `UPSTASH_REDIS_REST_TOKEN` (or `UPSTASH__RESTURL` / `UPSTASH__RESTTOKEN`). Production CORS includes `https://www.betweenatna.de` and `https://betweenatna.de`.

```bash
dotnet test
```

Docker (from `SmartAssistApi/`):

```bash
docker compose up --build
```

---

## Deployment

```
push to main → GitHub Actions: restore, build, test → POST Render deploy hook → Docker image on Render
```

`dotnet test` in CI currently has `continue-on-error: true`, so a failing test suite **does not** block the Render hook.

### Operations (Render + Supabase)

- Render **free** instances spin down when idle; the first request can stall until the process is up.
- Supabase **free** projects pause when idle. This API **migrates on startup**; a paused database means the container never listens. Symptom: the Vercel frontend loads, Clerk login works, then profile/chats hang until timeout.
- Keep `DATABASE_URL` / `SUPABASE__CONNECTIONSTRING` as a real `postgresql://` URI (no `[YOUR-PASSWORD]` placeholder).
- `GET /api/agent/health` is the light liveness probe. `GET /api/health` also checks Redis and, in production, Postgres.

---

## License

MIT
