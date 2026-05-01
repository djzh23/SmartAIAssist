# BetweenAtna — Backend

ASP.NET Core 9 backend for **BetweenAtna**, an AI-powered career platform for job seekers.

Hosted: **[smartassist-api.onrender.com](https://smartassist-api.onrender.com)**
Frontend: [github.com/djzh23/SmartAssist-react](https://github.com/djzh23/SmartAssist-react)

---

## What does this backend do?

Handles all business logic for the BetweenAtna platform:

- **AI routing** — receives chat messages, selects the right tool/mode, builds system prompts, streams Claude responses back via SSE
- **Job applications** — full CRUD for applications with pipeline stages and timeline tracking
- **CV Studio** — resume storage, snapshot versioning, PDF/DOCX export, category management
- **User management** — Clerk JWT auth, daily usage limits per subscription plan
- **Payments** — Stripe checkout, webhook handling, customer portal

---

## API Overview

### Agent / Chat
| Method | Endpoint | Description |
|---|---|---|
| POST | `/api/agent/stream` | AI response as SSE stream |
| POST | `/api/agent/ask` | AI response as JSON |
| GET | `/api/agent/usage` | Current day usage for the authenticated user |
| GET | `/api/agent/health` | Health check |

### Job Applications
| Method | Endpoint | Description |
|---|---|---|
| GET | `/api/applications` | List all applications |
| POST | `/api/applications` | Create application |
| PATCH | `/api/applications/{id}` | Update status, notes, fields |
| DELETE | `/api/applications/{id}` | Delete application |

### CV Studio — Resumes
| Method | Endpoint | Description |
|---|---|---|
| GET | `/api/cv-studio/resumes` | List resume summaries |
| POST | `/api/cv-studio/resumes` | Create resume |
| GET | `/api/cv-studio/resumes/{id}` | Get full resume data |
| PUT | `/api/cv-studio/resumes/{id}` | Update resume |
| DELETE | `/api/cv-studio/resumes/{id}` | Delete resume |

### CV Studio — Categories
| Method | Endpoint | Description |
|---|---|---|
| GET | `/api/cv-studio/categories` | List categories + assignments |
| POST | `/api/cv-studio/categories` | Create category |
| PATCH | `/api/cv-studio/categories/{id}` | Rename category |
| PUT | `/api/cv-studio/categories/order` | Reorder categories |
| DELETE | `/api/cv-studio/categories/{id}` | Delete category |
| POST | `/api/cv-studio/categories/assign` | Assign resume to category |

### CV Studio — Export
| Method | Endpoint | Description |
|---|---|---|
| POST | `/api/cv-studio/resumes/{id}/pdf` | Generate and download PDF |
| GET | `/api/cv-studio/pdf-exports` | List PDF export history |
| DELETE | `/api/cv-studio/pdf-exports/{id}` | Remove export entry |

### Payments
| Method | Endpoint | Description |
|---|---|---|
| POST | `/api/stripe/checkout` | Create Stripe checkout session |
| POST | `/api/stripe/webhook` | Handle Stripe subscription events |
| GET | `/api/stripe/portal` | Open Stripe customer portal |

---

## Architecture

```
SmartAssistApi/
├── Controllers/
│   ├── AgentController.cs              Chat, streaming, usage
│   ├── ApplicationsController.cs       Job application pipeline
│   ├── CvStudioController.cs           Resume CRUD + PDF/DOCX export
│   ├── CvStudioCategoriesController.cs Category management
│   ├── CvStudioVersionsController.cs   Snapshot versioning
│   └── StripeController.cs             Subscriptions and billing
├── Services/
│   ├── AgentService.cs                 AI tool routing, Claude orchestration, SSE streaming
│   ├── SystemPromptBuilder.cs          Per-mode system prompt assembly
│   ├── ApplicationsService.cs          Application pipeline logic + timeline
│   ├── CvStudioService.cs              Resume storage, PDF generation (PdfPig)
│   ├── CvStudioCategoriesService.cs    Category CRUD + assignment persistence
│   ├── ConversationService.cs          Chat session state (Redis)
│   ├── UsageService.cs                 Daily limits per plan (Redis counters)
│   ├── ClerkAuthService.cs             JWT verification
│   ├── ElevenLabsSpeechService.cs      Text-to-speech synthesis
│   └── StripeService.cs                Plan management via webhooks
├── Services/Tools/
│   ├── JobAnalysisTool.cs              Parses job descriptions, generates CV tips
│   ├── InterviewPrepTool.cs            Personalized interview questions (STAR method)
│   ├── LanguageLearningTool.cs         Structured multilingual responses
│   └── TranslationTool.cs              Direct translation
└── Models/                             All request/response/domain types
```

---

## Tech Stack

| Area | Technology |
|---|---|
| Framework | ASP.NET Core 9 |
| Language | C# 13 |
| AI Model | Anthropic Claude (Sonnet + Haiku) |
| Database | Supabase PostgreSQL (applications, CVs, categories) |
| Cache / Sessions | Upstash Redis (chat history, usage counters) |
| Auth | Clerk JWT |
| Payments | Stripe (Checkout, Webhooks, Customer Portal) |
| TTS | ElevenLabs |
| PDF Generation | PdfPig |
| Tests | xUnit |
| Hosting | Render (Docker, auto-deploy on push to `main`) |

---

## AI Modes

Each mode has a dedicated system prompt built by `SystemPromptBuilder.cs`:

| Mode | Behaviour |
|---|---|
| Career Coach | Open career advice, job search strategy |
| Job Analysis | Reads pasted job description, returns key requirements, keywords, and tailored CV tips |
| Interview Prep | Generates realistic questions using STAR method, personalised to the user's CV and target role |
| Language Learning | Structured output: target language text + translation + learning tip |
| Programming | Code answers with Markdown formatting, debugging focus |

---

## Subscription Plans

| Plan | Messages / day |
|---|---|
| Anonymous | 3 |
| Free (registered) | 20 |
| Premium | 200 |
| Pro | Unlimited |

---

## Local Development

**Requirements:** .NET 9 SDK

```bash
git clone https://github.com/djzh23/SmartAIAssist.git
cd SmartAIAssist
```

Add secrets to `SmartAssistApi/appsettings.Development.json`:

```json
{
  "Anthropic": { "ApiKey": "sk-ant-..." },
  "ConnectionStrings": { "DefaultConnection": "Host=...;Database=...;Username=...;Password=..." },
  "Upstash": { "RestUrl": "https://...", "RestToken": "..." },
  "Clerk": { "SecretKey": "sk_..." },
  "ElevenLabs": { "ApiKey": "sk_..." },
  "Stripe": { "SecretKey": "sk_test_...", "WebhookSecret": "whsec_..." }
}
```

```bash
dotnet run --project SmartAssistApi
```

API runs at `http://localhost:5194`.

---

## Run Tests

```bash
dotnet test
```

---

## Deployment

Push to `main` — GitHub Actions runs build + tests, then triggers a Render deploy via deploy hook:

```
push to main → dotnet build + test → Render builds Docker image → live
```

---

## License

MIT
