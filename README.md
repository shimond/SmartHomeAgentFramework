# Smart Home Agent Course — full progression

A teaching solution built around one narrative arc: **feel the pain → see the abstraction
→ hit its ceiling → see why agents exist → build agent capability one step at a time.**
ASP.NET Core throughout. Aspire orchestrates Ollama + Postgres + every step.

> ## ⚠️ Read this first
> This solution was generated as a teaching scaffold and **was not compiled** (no .NET SDK
> / no network in the environment that produced it). Treat it as a strong, consistent
> starting point — not a guaranteed green build. Three fast-moving integration points are
> flagged in code comments where they appear, because their exact API shape depends on
> package versions that change frequently: **(1)** the Aspire↔Ollama integration
> (`CommunityToolkit.Aspire.Hosting.Ollama`'s `AddOllama`/`AddModel`), **(2)** wiring a
> Postgres-backed chat-history provider so DevUI's own sessions persist (Step 5currently
> demonstrates the pattern via an explicit endpoint instead), and **(3)** the MCP client
> construction in Step 8. Each is called out at its source with what to verify and against
> which reference (the book repo, or the package's own docs).

---

## The narrative, in one table

| Step | What it shows | The "aha" |
|---|---|---|
| **0a** | Raw OpenAI `ChatClient` | This SDK is pleasant for ONE provider |
| **0b** | Raw `OllamaApiClient` | Same goal, **completely different contract** — the pain |
| **1** | `IChatClient` abstraction | One call site, either provider — the relief |
| **2** | Structured output ceiling | Abstraction unifies the *call site*, not the *guarantee* |
| **3** | `AddAIAgent` + DevUI + tools | The boundary: chat that **acts**; endpoints disappear, DevUI appears |
| **4** | Agent + in-process memory | Remembers across restarts — but only on ONE container |
| **5** | Agent + Postgres conversation store | **Any container, same conversation** |
| **6** | Agent + RAG | Grounded in manuals, not guesses |
| **7** | Agent + approval + OpenTelemetry | Production readiness: human-in-the-loop + traces |
| **8** | Agent + MCP client | Tools can live in a totally separate process |
| **9** | Multi-agent: **workflow** *and* **orchestrator** | Same 3 specialists, two ways to combine them — fixed graph vs. model-decided flow |

Steps 0a–2 are **advisor** apps (recommend a platform, no actions) with a tiny embedded
HTML chat+stream page — open a browser, no Postman, no separate frontend. Steps 3–9 are
the **concierge** agent (controls a simulated house) — the HTML page is gone; **DevUI**
is the dashboard from here on.

---

## Run everything via Aspire (recommended)

```bash
dotnet run --project src/SmartHome.AppHost
```

This brings up an Ollama container (model `llama3.2`, pre-pulled, persisted across
restarts), a Postgres container + pgAdmin (for Step 5), and all eleven step projects,
wired together via Aspire service discovery. The Aspire dashboard opens automatically —
every project's logs, traces, and metrics are visible there, including Step 7's
OpenTelemetry demo with zero extra setup (that's what `AddServiceDefaults()` buys you).

For Step 0a (OpenAI) and the eval tests, set `OPENAI_API_KEY` in your environment first if
you want to exercise the OpenAI path:

```bash
export OPENAI_API_KEY=sk-...
```

## Run a single step standalone (without Aspire)

```bash
dotnet run --project src/SmartHome.Step3.ConciergeAgent
```

Falls back to `http://localhost:11434` for Ollama and a local Postgres connection string
(see each step's `appsettings.json`) — fine for Steps 0a–4/6–9. Step 5 needs a reachable
Postgres; either run the AppHost, or point `ConnectionStrings:conversations` in
`src/SmartHome.Step5.PersistentChat/appsettings.json` at one you have running.

---

## Switching provider (Steps 0b onward)

Edit `SmartHome:Provider` in the step's `appsettings.json` — `"OpenAI"` or `"Ollama"`.
Step 0a is hard-coded to OpenAI and Step 0b is hard-coded to Ollama **on purpose** — that's
the contrast the lecture is built on. Everything from Step 1 onward is provider-agnostic.

---

## If DevUI doesn't load (`/devui` 404s) — Steps 3–9

`MapDevUI()` (dev-only) needs to sit alongside the OpenAI-compatible Responses/
Conversations endpoints the hosting layer exposes for registered agents. If your installed
package version wires this differently, the canonical reference is the official template:

```bash
dotnet new install Microsoft.Agents.AI.ProjectTemplates
dotnet new aiagent-webapi -o _ReferenceWiring
```

Copy `_ReferenceWiring/Program.cs`'s DevUI/endpoint wiring into the step (keep the step's
own agent registrations), and re-run.

---

## Step 5 in depth: where does the session actually live?

- **Step 4** stores preferences in a local JSON file — survives a restart of *this*
  container, but a second container/replica never sees it. That's not a bug; it's the
  setup for Step 5.
- **Step 5** swaps the storage for Postgres (`PostgresConversationStore.cs`, behind the
  `IConversationStore` interface defined in `SmartHome.Shared`). The pattern, demonstrated
  explicitly via `POST /api/chat-persistent`:
  1. Load this `conversationId`'s prior messages from Postgres — once, at the start.
  2. Rehydrate an in-memory `AgentThread` for the duration of *this* request only.
  3. Run the agent normally.
  4. Save the updated transcript back — once, at the end.

  Any container can now pick up any conversation — no sticky sessions, no per-token DB
  round trips. The DevUI agent in the same file is registered the ordinary way (Step 3/4
  style); making DevUI's *own* sessions Postgres-backed too requires wiring a custom
  chat-history provider into the framework's extension point — flagged as a `TODO` in
  `Step5/Program.cs` for exactly this reason.
- Messages are stored as **JSONB** — a Mongo-style document inside a Postgres column —
  so swapping in Mongo or Redis later only means writing a new `IConversationStore`
  implementation; nothing else changes.

---

## Step 9 in depth: workflow vs. orchestrator

Step 9 registers the same three specialists (`security`, `comfort`, `energy`) twice over,
behind two different agents you can pick in DevUI's sidebar:

- **`home-ops`** — a `WorkflowBuilder.BuildSequential` graph. **You** decided the order in
  code: security → comfort → energy, every single time, regardless of what the user asked.
- **`home-ops-orchestrator`** — an ordinary agent whose *tools* are the other three agents
  (wrapped via the `AsAgentTool` helper in `Program.cs`). **The model** decides which
  specialist(s) to call, in what order, and can skip one entirely if the request doesn't
  need it.

Run the same prompt against both to feel the difference:

- `"I'm heading out — make the house safe and cheap to run while I'm gone."` — both should
  touch all three specialists, since the request genuinely spans all of them.
- `"Just dim the lights for movie night, nothing else."` — the workflow still runs
  security and energy regardless; the orchestrator should call only comfort.

`AsAgentTool` is a manual wrap (pass a natural-language instruction in, get the
specialist's final text back) chosen because it's guaranteed to compile regardless of
package version. Some Agent Framework versions ship a built-in "agent as tool" helper —
check your installed version for something like `AIAgent.AsAIFunction()` and swap it in if
present; the manual wrap is the safe fallback either way.

---

## Evals (`SmartHome.Evals`)

```bash
dotnet test src/SmartHome.Evals
```

Deterministic **trajectory** checks — no LLM judge, no flake, runs in CI as a merge gate.
The key one: `Unlock_request_must_be_preceded_by_approval` — the exact Step 7 guardrail,
turned into something that can fail a build. This is layer 1 of the two-layer eval model;
layer 2 (LLM-judged groundedness, intent resolution) would use
`Microsoft.Extensions.AI.Evaluation.Quality` against a judge model — not included here to
keep the eval project runnable offline against Ollama, but the package is pinned in
`Directory.Packages.props` if you want to add it.

Tests default to Ollama; override with env vars:

```bash
SMARTHOME_PROVIDER=OpenAI SMARTHOME_MODEL=gpt-4o-mini dotnet test src/SmartHome.Evals
```

---

## Project layout

```
SmartHomeAgentCourse/
├─ SmartHomeAgentCourse.slnx
├─ Directory.Build.props / Directory.Packages.props / nuget.config
└─ src/
   ├─ SmartHome.AppHost/            # Aspire: Ollama + Postgres + every step, wired together
   ├─ SmartHome.ServiceDefaults/    # OTel + health checks + service discovery (every step uses this)
   ├─ SmartHome.Shared/             # Domain (HomeState/HomeTools), chat-client factory, RAG store,
   │                                # IConversationStore, the embedded advisor HTML page
   ├─ SmartHome.Step0a.AdvisorOpenAI/    # raw OpenAI SDK
   ├─ SmartHome.Step0b.AdvisorOllama/    # raw Ollama SDK — the contrast
   ├─ SmartHome.Step1.AdvisorAbstracted/ # IChatClient
   ├─ SmartHome.Step2.AdvisorStructured/ # the structured-output ceiling
   ├─ SmartHome.Step3.ConciergeAgent/    # AddAIAgent + DevUI + tools
   ├─ SmartHome.Step4.AgentWithMemory/   # + in-process memory
   ├─ SmartHome.Step5.PersistentChat/    # + Postgres conversation store
   ├─ SmartHome.Step6.AgentWithRag/      # + RAG
   ├─ SmartHome.Step7.AgentWithApproval/ # + approval gate + OpenTelemetry
   ├─ SmartHome.Step8.AgentWithMcp/      # + MCP client
   ├─ SmartHome.Step9.MultiAgentWorkflow/# + multi-agent workflow
   ├─ SmartHome.McpServer/               # the external MCP tool provider (Steps 8/9)
   └─ SmartHome.Evals/                   # deterministic trajectory tests (xUnit)
```
