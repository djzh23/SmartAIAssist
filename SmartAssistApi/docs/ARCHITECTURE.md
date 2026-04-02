# Architecture

## Overview

SmartAssistApi is an ASP.NET Core Web API that exposes a single conversational endpoint backed by Claude (Anthropic). It supports **tool calling**: Claude can decide mid-conversation to invoke a registered tool (e.g. get weather, summarize text), receive the result, and incorporate it into its final reply.

---

## ASCII Diagram

```
┌─────────────────────────────────────────────────────────┐
│                        Client                           │
│              POST /api/agent/ask                        │
│              { "message": "..." }                       │
└─────────────────────┬───────────────────────────────────┘
                      │ HTTP
                      ▼
┌─────────────────────────────────────────────────────────┐
│                  AgentController                        │
│                                                         │
│  1. Validate: not empty, max 500 chars → 400            │
│  2. Delegate to AgentService                            │
│  3. Return 200 AgentResponse or 500 on exception        │
└─────────────────────┬───────────────────────────────────┘
                      │
                      ▼
┌─────────────────────────────────────────────────────────┐
│                   AgentService                          │
│                                                         │
│  - Reads Model, MaxTokens, Temperature from config      │
│  - Builds MessageParameters with registered tools       │
│  - Manages multi-turn message history                   │
│                                                         │
│  ┌─────────────────────────────────────────────────┐    │
│  │              Anthropic SDK                      │    │
│  │   AnthropicClient.Messages.GetClaudeMessageAsync│    │
│  └────────────────────┬────────────────────────────┘    │
│                       │                                 │
│          ┌────────────▼────────────┐                    │
│          │   Tool call in reply?   │                    │
│          └──────┬──────────┬───────┘                    │
│                 │ Yes      │ No                         │
│                 ▼          ▼                            │
│         ┌──────────┐  Return reply                      │
│         │ Invoke   │  directly                          │
│         │  Tool    │                                    │
│         └────┬─────┘                                    │
│              │ Append tool result to history            │
│              │ Call Claude again for final reply        │
│              ▼                                          │
│         Return reply + toolUsed name                    │
└─────────────────────┬───────────────────────────────────┘
                      │
          ┌───────────┴────────────┐
          ▼                        ▼
┌──────────────────┐    ┌─────────────────────┐
│   WeatherTool    │    │    SummaryTool       │
│                  │    │                      │
│ get_weather(city)│    │ summarize_text(text) │
│ → hardcoded data │    │ → first 15 words     │
└──────────────────┘    └─────────────────────┘
```

---

## Request Flow (step by step)

1. **Client** sends `POST /api/agent/ask` with `{ "message": "What's the weather in Berlin?" }`

2. **AgentController** validates the message (non-empty, ≤ 500 chars), then calls `AgentService.RunAsync()`.

3. **AgentService** builds a `MessageParameters` object containing:
   - The user message
   - The model, max tokens, and temperature from `appsettings.json`
   - The two registered tools (`get_weather`, `summarize_text`)

4. **First Claude call** — Claude reads the message and tools. If it determines a tool is needed, it returns a response containing one or more `ToolCall` objects instead of a plain text reply.

5. **Tool dispatch** — For each `ToolCall`, `AgentService` calls `toolCall.Invoke<string>()`, which routes to the matching static method in `WeatherTool` or `SummaryTool`. The result is appended to the message history.

6. **Second Claude call** — Claude receives the original message plus the tool result and produces a natural-language final reply.

7. **AgentController** returns `200 OK` with `{ "reply": "...", "toolUsed": "get_weather" }`.

If no tool is needed, steps 5–6 are skipped and the first Claude response is returned directly.

---

## Configuration (`appsettings.json`)

| Key | Purpose |
|-----|---------|
| `ANTHROPIC_API_KEY` | Anthropic API key (set via environment variable or `.env`) |
| `Anthropic:Model` | Claude model ID |
| `Anthropic:MaxTokens` | Maximum tokens in Claude's response |
| `Anthropic:Temperature` | Sampling temperature (1.0 = default) |

---

## Adding a Tool

See [../CLAUDE.md](../CLAUDE.md) — "How to Add a New Tool" section.
