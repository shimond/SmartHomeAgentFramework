# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this repo is

A teaching solution for an Agent Framework course, structured as a sequence of numbered ASP.NET
Core "Step" projects that each add ONE new capability on top of the last:

```
0a (raw OpenAI SDK) → 0b (raw Ollama SDK, contrast) → 1 (IChatClient abstraction)
→ 2 (structured output) → 3a (agent, no tools) → 3b (agent middleware)
→ 3 (agent + tools + DevUI) → 4 (+ in-process memory) → 5 (+ Postgres conversation store)
→ 6 (+ RAG) → 7 (+ approval gate + OpenTelemetry) → 8 (+ MCP client) → 9 (multi-agent)
```

Steps 0a–2 are "advisor" apps (recommend a smart-home platform, no actions) with an embedded
HTML chat page. Steps 3a onward are the "concierge" agent (controls a simulated house via
`Microsoft.Agents.AI`); the HTML page is gone and **DevUI** (`/devui`) is the dashboard.

**⚠️ Known state**: this solution was generated as a teaching scaffold and has not been
compiled (no .NET SDK / network access in the environment that produced it). Do not assume it
builds cleanly — verify before reporting success. One concrete known break: `SmartHome.Evals/ConciergeTrajectoryTests.cs`
calls `ChatClientSetup.CreateChatClient(...)` and `Agents.ConciergeInstructions`, but
`SmartHome.Shared` actually exposes `ChatClientSetup.RegisterAIClient(builder)` (an
`IHostApplicationBuilder` extension, not a factory method) and `Agents.HomeAssistanceInstructions`.
Fix call sites to match `SmartHome.Shared`, don't rename the shared members to match the test.
Three fast-moving integration points are flagged in code comments where their exact API depends
on package versions that change frequently: (1) the Aspire↔Ollama integration
(`CommunityToolkit.Aspire.Hosting.Ollama`'s `AddOllama`/`AddModel`), (2) wiring a Postgres-backed
chat-history provider so DevUI's own sessions persist (Step 5's `PostgresChatHistoryProvider.cs`),
and (3) MCP client construction in Step 8/9 (`McpToolsProvider` in Step 9's `Program.cs`).

## Commands

Run everything via Aspire (recommended — brings up Ollama + Postgres + all step projects wired
together with service discovery, dashboard at the Aspire URL):

```bash
dotnet run --project src/SmartHome.AppHost
```

Run a single step standalone (falls back to `http://localhost:11434` for Ollama and a local
Postgres connection string per-step in `appsettings.json`):

```bash
dotnet run --project src/SmartHome.Step3.ConciergeAgent
```

Step 0a (OpenAI) and the evals need `OPENAI_API_KEY` set if exercising the OpenAI path. Step 5
needs a reachable Postgres (either the AppHost, or point `ConnectionStrings:conversations` in
`src/SmartHome.Step5.PersistentChat/appsettings.json` at one).

Build:
```bash
dotnet build
```

Run the eval suite (deterministic trajectory checks, no LLM judge — see "Evals" below):
```bash
dotnet test src/SmartHome.Evals
```
Run a single eval test:
```bash
dotnet test src/SmartHome.Evals --filter "FullyQualifiedName~Unlock_request_must_be_preceded_by_approval"
```

### Running the evals (provider + key)

The evals call a **live model** — they build the real Step-3 concierge (through the shared
`ChatClientSetup.RegisterAIClient` path) and run it, so a reachable model is required or every
test fails on the network call, not an assertion.

`src/SmartHome.Evals/test.runsettings` sets the target to **OpenAI / `gpt-4o-mini`** and is
auto-applied in both VS Test Explorer and `dotnet test` via `<RunSettingsFilePath>` in the
`.csproj` — no manual selection.

**The API key is deliberately NOT in source control.** The Evals project shares the AppHost's
`<UserSecretsId>` (`fe8ac0f9-94e9-4766-8846-f638f607134e`), so `TestAgentFactory` reads the same
user-secret the AppHost already uses — set it once and both use it (no VS restart, no per-shell
env var):

```bash
dotnet user-secrets set "Parameters:openai-openai-apikey" "sk-..." --project src/SmartHome.AppHost
```

Then just `dotnet test src/SmartHome.Evals` (or Run All in Test Explorer). Key resolution order in
`TestAgentFactory` is: `OPENAI_API_KEY` env var first (the CI path — wins when set), then the
shared user-secret above. It throws a clear message naming this command if neither is present. For
CI, prefer the env var:
```bash
OPENAI_API_KEY=sk-... dotnet test src/SmartHome.Evals
```

To target a different model, override via env var (wins over the runsettings default):
```bash
SMARTHOME_MODEL=gpt-4o dotnet test src/SmartHome.Evals
```
To run against **local Ollama** instead, set `SMARTHOME_PROVIDER=Ollama` (defaults to model
`llama3.2` on `http://localhost:11434`; pull the model and have `ollama serve` running first),
or flip `<SMARTHOME_PROVIDER>` to `Ollama` in `test.runsettings` (no key needed then).

These are behavioral tests against a real model: `gpt-4o-mini` reliably passes all three, but
`MovieNight`/`Status` assert specific tool calls, so a weak local model can legitimately fail
them. The guardrail test (`Unlock_...`) passes even if the model just refuses.

There is no lint config in this repo (no `.editorconfig`-driven analyzers beyond the standard
SDK); rely on `dotnet build` warnings.

## Architecture

**Every step project is a standalone, runnable ASP.NET Core app** (own `Program.cs`,
`appsettings.json`, launch profile) — they are not layered on each other in code. What's shared
lives in `SmartHome.Shared` and `SmartHome.ServiceDefaults`; each step composes those pieces
differently to demonstrate one delta. When asked to "add X to step N," check whether X belongs
in the step's own `Program.cs` or should move into `SmartHome.Shared` for reuse by later steps.

- **`SmartHome.Shared`** — the reusable core every agent-capable step (3a+) pulls from:
  - `Hosting/ChatClientSetup.cs` — `RegisterAIClient(builder)` reads `SmartHome:Provider`
    (`"OpenAI"` or `"Ollama"`) from config and registers the matching `IChatClient` (via
    `AddOpenAIClient`/`AddOllamaApiClient`). This is the one call site every step from Step 1
    onward uses instead of hard-coding a provider SDK. `RegisterEmbeddingGenerator` does the
    same for the OpenAI-only embedding generator used by Step 6's RAG.
  - `Hosting/Agents.cs` — `Agents.HomeAssistanceInstructions` (system prompt) and
    `Agents.ToolsFor(IHomeGateway)` (the tool list) shared by every concierge/specialist agent;
    `AdvisorInstructions.Text` is the separate prompt for the advisor steps (0a–2).
  - `Domain/Home.cs` / `HomeTools.cs` — the house behind an `IHomeGateway` boundary (device
    reads + commands for lights, thermostat, lock, alarm, music, scene). `InMemoryHome` is the
    offline implementation registered as a singleton (`AddSingleton<IHomeGateway, InMemoryHome>()`);
    the seam is documented for a real device-hub implementation (`DeviceHubHome`) — same
    swap-the-impl pattern as `IManualStore`/`IConversationStore`. `HomeTools` are a thin layer over
    the gateway, exposing the `[Description]`-annotated methods as tools via `AIFunctionFactory.Create`.
    `UnlockDoor` and `DisarmAlarm` are gated: they refuse unless `home.SensitiveActionApproved` was
    set by a prior `RequestApproval` call — enforced in the tool itself, not just the prompt, so it
    holds regardless of model behavior (this is the Step 7 guardrail and the eval that checks it).
  - `Domain/IConversationStore.cs` — the Step 5 swappable persistence boundary
    (`LoadAsync`/`SaveAsync` by `conversationId`); `InMemoryConversationStore` is the
    Step-4-equivalent baseline, `PostgresConversationStore` (in the Step5 project) is the real
    implementation, storing messages as JSONB.
  - `Rag/` — `IManualStore` with a keyword-based `ManualStore` and an OpenAI-embedding-backed
    `EmbeddingManualStore` for Step 6; swappable via config so RAG still works without OpenAI.
  - `Web/AdvisorPage.cs` — the embedded HTML+JS chat/stream page used only by Steps 0a–2.

- **`SmartHome.ServiceDefaults`** — the standard Aspire "ServiceDefaults" shape
  (`AddServiceDefaults()`), called near the top of every step's `Program.cs`. Wires
  OpenTelemetry export, health checks (`/health`, `/alive`), service discovery, resilient
  `HttpClient`s, and — notably — registers DevUI plus the OpenAI-compatible Responses/
  Conversations endpoints (`AddDevUI`, `AddOpenAIResponses`, `AddOpenAIConversations`) that
  `MapDevUI()` and `MapDefaultEndpoints()` depend on. If DevUI 404s at `/devui`, this is the
  first place to check package-version drift; the canonical reference is
  `dotnet new aiagent-webapi` from `Microsoft.Agents.AI.ProjectTemplates`.

- **`SmartHome.AppHost`** (`AppHost.cs`) — the Aspire orchestration entry point. Declares the
  Ollama container (`gemma3:4b`, persistent volume/lifetime), the OpenAI resource + two models
  (`gpt-4o-mini` for chat, `text-embedding-3-small` for embeddings), Postgres + pgAdmin, and adds
  every step project with `.WithReference(...)`/`.WaitFor(...)` wiring so service discovery
  resolves container endpoints automatically. This is the map of which step depends on which
  backing resource — check it before adding a new resource dependency to a step.

- **Agent registration pattern** (Step 3 onward): `builder.AddAIAgent("name", (sp, key) => ...)`
  registers a keyed `AIAgent` that DevUI auto-discovers. The factory pulls `IChatClient` +
  `IHomeGateway` from DI, optionally wraps the client with `.AsBuilder().UseLogging(...).UseOpenTelemetry(...)`
  (Step 7's telemetry pattern), and builds a `ChatClientAgent` with `Agents.HomeAssistanceInstructions`
  + `Agents.ToolsFor(state)`. Step 9 also shows the same pattern applied to multiple named
  "specialist" agents (`security`, `comfort`, `energy`) plus a workflow/orchestrator built from
  them via `AgentWorkflowBuilder` — registered manually with `AddKeyedSingleton<AIAgent>` (not
  `.AddAsAIAgent()`) specifically so `includeWorkflowOutputsInResponse: true` can be set, without
  which DevUI shows the executor graph but no aggregated output.

- **Agent-layer middleware** (Step 3b, `SmartHome.Step3b.AgentWithMiddleware`): uses
  `agent.AsBuilder().Use(...)` — run middleware (`IEnumerable<ChatMessage>`-shaped lambda) and
  function-invocation middleware (`FunctionInvocationContext`-shaped lambda) — distinct from the
  chat-client middleware in Steps 3/7. Two demos transform data across the model boundary in both
  directions: a **PII-redaction** middleware scrubs a tool's result on the way OUT, and a
  **secret-injection** middleware sets `context.Arguments["pin"]` on the way IN so the guest PIN
  reaches the tool but never the model. The PIN never appears in the schema because Step 3b swaps
  the shared `UnlockDoor` for a local `PinGatedLock` variant whose `pin` param is marked
  `ExcludeFromSchema` via `AIFunctionFactoryOptions.ConfigureParameterBinding` (see
  `PinGatedLock.cs`); the demo PIN is `Lock:Pin` in that project's `appsettings.json`. The swap is
  Step-3b-local, so shared `HomeTools`/`ToolsFor` and every other step stay untouched.

- **`SmartHome.McpServer`** — a standalone ASP.NET MCP server (`AddMcpServer().WithHttpTransport().WithTools<EnergyMcpTools>()`)
  exposing energy/weather tools. `Program.cs` is the composition root only; the tools live in
  `EnergyMcpTools.cs` as a **DI-activated instance class** (the SDK builds a fresh instance per
  call from a per-request scope), delegating to an injected `IEnergyService` (`EnergyService.cs`)
  that computes prices/forecast from an injected `TimeProvider` — no static state, no
  `DateTime.Now`, so the logic is testable with a fake clock. Steps 8/9 connect
  to it over HTTP using an Aspire-discovered URL (`services:mcp-energy-server:https:0` /
  `:http:0` config keys, falling back to `http://localhost:5300`). Step 9's `McpToolsProvider`
  does the MCP handshake lazily on first use (guarded by a `SemaphoreSlim`) rather than eagerly
  in an `IHostedService`, specifically to avoid a startup race where an agent factory could run
  before the handshake completes and capture an empty tool list.

- **`SmartHome.Evals`** — xUnit trajectory tests (not LLM-judged) asserting which tools get
  called and in what order for fixed prompts — e.g. `Unlock_request_must_be_preceded_by_approval`
  checks that `UnlockDoor` never appears in the tool-call trajectory without a preceding
  `RequestApproval`. This is layer 1 of a two-layer eval model; layer 2 (LLM-judged groundedness
  via `Microsoft.Extensions.AI.Evaluation.Quality`, pinned in `Directory.Packages.props` but not
  wired up) is intentionally not implemented, to keep this project runnable offline against
  Ollama.

## Conventions

- Central package management: all versions live in `Directory.Packages.props`
  (`ManagePackageVersionsCentrally=true`) — add new dependencies there, not with inline
  `Version=` attributes in a `.csproj`.
- Common MSBuild settings (`net10.0`, nullable enabled, implicit usings, prerelease-package
  `NoWarn`s) live in `Directory.Build.props` and apply to every project automatically.
- Provider switching is config-driven: `SmartHome:Provider` = `"OpenAI"` or `"Ollama"` in each
  step's `appsettings.json`. Steps 0a/0b are hard-coded to one provider on purpose (that's the
  lecture's contrast point); everything from Step 1 onward is provider-agnostic through
  `ChatClientSetup`.
