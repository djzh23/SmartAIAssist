# SmartAssist Backend

ASP.NET Core 9 Backend für die SmartAssist KI Plattform.
Gehostet auf: **[smartassist-api.onrender.com](https://smartassist-api.onrender.com)**
Frontend Repo: [github.com/djzh23/SmartAssist-react](https://github.com/djzh23/SmartAssist-react)

---

## Was macht dieses Backend?

Das Backend steuert die KI Logik, Nutzerverwaltung und alle externen Dienste.
Es empfängt Nachrichten vom Frontend, wählt das passende Werkzeug aus,
baut den System Prompt auf und streamt die Antwort von Anthropic Claude zurück.

---

## API Endpunkte

| Methode | Endpunkt | Beschreibung |
|---|---|---|
| POST | `/api/agent/stream` | KI Antwort als SSE Stream |
| POST | `/api/agent/ask` | KI Antwort als einfacher JSON Response |
| POST | `/api/agent/speak` | Text zu Sprache via ElevenLabs |
| GET | `/api/agent/usage` | Tagesverbrauch des Nutzers |
| GET | `/api/agent/health` | Health Check |
| POST | `/api/agent/context` | Sitzungskontext setzen (Stelle, Lebenslauf) |
| POST | `/api/stripe/checkout` | Stripe Checkout Session erstellen |
| POST | `/api/stripe/webhook` | Stripe Webhook für Abo Ereignisse |
| GET | `/api/stripe/portal` | Stripe Kundenportal öffnen |

---

## Architektur

```
SmartAssistApi/
├── Controllers/
│   ├── AgentController.cs       KI Chat, Streaming, Usage, Kontext
│   ├── SpeechController.cs      Text zu Sprache (ElevenLabs)
│   └── StripeController.cs      Abonnement und Zahlung
├── Services/
│   ├── AgentService.cs          Kernlogik: Tool Routing, Claude Aufruf, Streaming
│   ├── SystemPromptBuilder.cs   Dynamische System Prompts je Werkzeug
│   ├── ConversationService.cs   Gesprächsverlauf und Sitzungskontext (Redis)
│   ├── UsageService.cs          Tägliche Nutzungslimits je Plan
│   ├── ClerkAuthService.cs      JWT Authentifizierung via Clerk
│   ├── ElevenLabsSpeechService  Text zu Sprache Synthese
│   └── StripeService.cs         Abo Verwaltung
├── Services/Tools/
│   ├── WeatherTool.cs           Wetter API
│   ├── JokeTool.cs              Witze Generierung
│   ├── TranslationTool.cs       Übersetzung
│   └── LanguageLearningTool.cs  Strukturiertes Sprachlern Format
└── Models/                      Request, Response und Kontext Typen
```

---

## Tech Stack

| Bereich | Technologie |
|---|---|
| Framework | ASP.NET Core 9 |
| Sprache | C# 13 |
| KI Modell | Anthropic Claude (claude-sonnet) |
| Datenbank | Upstash Redis (Gesprächsverlauf, Usage Tracking) |
| Authentifizierung | Clerk JWT |
| Zahlung | Stripe (Checkout, Webhook, Kundenportal) |
| Sprachsynthese | ElevenLabs TTS |
| Tests | xUnit (96 Tests) |
| Hosting | Render |

---

## Werkzeuge und Prompts

Jedes Werkzeug hat einen eigenen System Prompt in `SystemPromptBuilder.cs`:

| Werkzeug | Besonderheit |
|---|---|
| Allgemein | Offene Konversation |
| Sprachen lernen | Strukturiertes Format: `---ZIELSPRACHE---`, `---UEBERSETZUNG---`, `---TIPP---` |
| Stellenanalyse | Liest Job Kontext aus Redis, gibt CV Tipps und Keywords |
| Vorstellungsgespräch | STAR Methode, personalisierte Fragen basierend auf Lebenslauf |
| Programmierung | Code Antworten mit Markdown Highlighting |
| Wetter | Ruft externe Wetter API auf |

---

## Nutzerlimits

| Plan | Nachrichten pro Tag |
|---|---|
| Anonym | 3 |
| Kostenlos (registriert) | 20 |
| Premium | 200 |
| Pro | Unbegrenzt |

---

## Lokal starten

**Voraussetzungen:** .NET 9 SDK

```bash
git clone https://github.com/djzh23/SmartAIAssist.git
cd SmartAIAssist
```

Umgebungsvariablen in `appsettings.Development.json` oder als Umgebungsvariablen setzen:

```json
{
  "Anthropic": { "ApiKey": "sk-ant-..." },
  "Upstash": { "RestUrl": "...", "RestToken": "..." },
  "Clerk": { "SecretKey": "sk_..." },
  "ELEVENLABS_API_KEY": "sk_..."
}
```

```bash
dotnet run --project SmartAssistApi
```

API läuft unter `http://localhost:5194`.

---

## Tests ausführen

```bash
dotnet test
```

96 Unit Tests für Prompt Parsing, Tool Logik und Usage Service.

---

## Lizenz

MIT
