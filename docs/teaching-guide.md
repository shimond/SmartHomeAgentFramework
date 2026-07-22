# Teaching Guide — Smart Home Agent Course

How to run this repo as a live, step-by-step lecture: what to say, where to click, and the
exact prompts to type at each step. Pairs with the narrative in the root `README.md` and the
architecture notes in `CLAUDE.md` — this file is the *run sheet*, those are the *reference*.

## Before class

- .NET 10 SDK installed; `dotnet --version` works.
- `OPENAI_API_KEY` set in the environment (needed for Step 0a and any step you run with
  `SmartHome:Provider = OpenAI`).
- Docker Desktop running — Aspire brings up Ollama and Postgres as containers.
- **Pre-pull the Ollama model before class.** The first `dotnet run --project src/SmartHome.AppHost`
  downloads `gemma3:4b`/`llama3.2`, which can take several minutes on conference wifi. Run it
  once the night before so the container/volume is warm.
- Open two browser tabs ahead of time: the Aspire dashboard (for Steps 3b/7's traces/logs) and
  a spare tab for whichever step's page/DevUI you're on.
- Skim "Known rough edges" at the end of this guide once — so a live surprise doesn't read as
  a bug you didn't know about.

Suggested pacing for a single session (adjust to your slot): **0a/0b 10 min, 1/2 10 min,
3a/3b/3 15 min, 4/5 15 min, 6 10 min, 7 10 min, 8/9 15 min** ≈ 85 minutes of hands-on demo time,
excluding slides/discussion.

Two ways to run each step:
- **Via Aspire (recommended for anything past Step 4):** `dotnet run --project src/SmartHome.AppHost`
  brings up every step, Ollama, Postgres, and the MCP server together, wired via service
  discovery. Pick the step's resource from the Aspire dashboard to open its URL.
- **Standalone (fine for 0a–3b):** `dotnet run --project src/SmartHome.Step0a.AdvisorOpenAI`
  (etc.) — falls back to `localhost` endpoints per that step's `appsettings.json`.

---

## Step 0a — Raw OpenAI SDK

**Aha:** "This SDK is pleasant for ONE provider."

- **Where:** browser at the project's root URL (`/`) — a plain HTML chat/stream page.
- **Needs:** `OPENAI_API_KEY`.
- **Say:** "Let's start with the vendor SDK, no abstraction at all — you'll see this is
  genuinely pleasant to use, right up until we need a second provider."

**Prompts:**
1. `I have a bunch of Philips Hue bulbs and a HomePod. What smart home platform should I get?`
   — click **Send (full response)**.
2. Same prompt, click **Send (streaming)** — point at the meta line under the answer: first
   token latency vs. total time.

**Point out in code:** `ChatClient` + `ApiKeyCredential`; `CompleteChatAsync` vs.
`CompleteChatStreamingAsync`; the hand-written JSON schema string in
`/api/recommend-structured` (foreshadow Step 2 — don't demo this endpoint yet, just show it
exists).

**Pitfall:** if the API key isn't set, this throws at startup with a message about "the
'openai' connection string" — the code actually reads a raw `OPENAI_KEY` config value, so set
it as an environment variable, not a connection string, if running standalone.

---

## Step 0b — Raw Ollama SDK (the pain point slide)

**Aha:** "Same goal, completely different contract."

- **Where:** browser root page.
- **Needs:** Ollama running with a model pulled (via AppHost, or your own local Ollama).
- **Say:** "Nothing about the *problem* changed — only the vendor. Watch how much of the code
  changes anyway." Open `Step0a/Program.cs` and `Step0b/Program.cs` side by side.

**Prompts:**
1. `I have a bunch of Philips Hue bulbs and a HomePod. What smart home platform should I get?`
   — full response, then streaming.

**Point out in code:** `OllamaApiClient(Uri, model)` constructor vs. `ChatClient(model,
ApiKeyCredential)`; `OllamaSharp`'s own `Message`/`ChatRole` types; `ChatAsync` always streams,
even for the "full response" button (drained into one string); no schema-enforced structured
output equivalent to Step 0a's `CreateJsonSchemaFormat`.

**Pitfall:** running standalone (not via AppHost) requires `OLLAMA_CHAT_URI` /
`OLLAMA_CHAT_MODEL` to be set explicitly — there's no localhost fallback in this step's code,
so a missing value throws at construction, not a friendly config error.

---

## Step 1 — `IChatClient` abstraction (the relief)

**Aha:** "One call site, either provider."

- **Where:** browser root page.
- **Say:** "Same scenario, now through `Microsoft.Extensions.AI`'s `IChatClient`. Look at how
  short `Program.cs` is — no provider-specific type appears anywhere in the request handlers."

**Prompts:**
1. `I have a bunch of Philips Hue bulbs and a HomePod. What smart home platform should I get?`
2. **Live flip:** edit `appsettings.json`'s `SmartHome:Provider` between `OpenAI` and `Ollama`,
   restart, run the same prompt again — same code, different backend.

**Point out in code:** `ChatClientSetup.RegisterAIClient(builder)` — the one call site every
step from here on uses; the handler builds a plain `List<ChatMessage>` regardless of provider.

---

## Step 2 — Structured output ceiling

**Aha:** "The abstraction unifies the *call site*, not the *guarantee*."

- **Where:** browser root page — scroll down to the **Structured output tester** section this
  step adds.
- **Say:** "`IChatClient` smooths the API shape. It cannot manufacture a guarantee the
  underlying provider doesn't make."

**Prompts (Structured output tester box):**
1. With `SmartHome:Provider = OpenAI`:
   `Recommend a starter smart home kit for a small apartment that already has Philips Hue lights.`
   — parses cleanly into `recommendedPlatform` / `reason` / `considerations`.
2. Flip to `SmartHome:Provider = Ollama`, restart, same prompt — depending on your installed
   OllamaSharp version and model, this may show **PARSE FAILED** with the raw model text
   underneath. **Test this exact combination before class** — Ollama's JSON/schema support has
   improved over time, so confirm whether your setup still shows the ceiling or has closed it;
   either result is a valid teaching moment, but know which one you'll get.

**Point out in code:** `chatClient.GetResponseAsync<SmartHomeRecommendation>(...)` +
`response.TryGetResult(out var parsed)` — the graceful-failure path, so a weak provider
guarantee shows up live as `parseError`, not an unhandled exception.

---

## Step 3a — Agent, no tools

**Aha:** "An agent is still just chat until it can act."

- **Where:** browser root page (this step deliberately does **not** use DevUI yet).
- **Say:** "Same advisor, same instructions, same HTML page as Step 1 — but now wrapped in an
  `AIAgent`. No visible behavior change; the point is the code shape."

**Prompts:**
1. `I have a bunch of Philips Hue bulbs and a HomePod. What smart home platform should I get?`

**Point out in code:** `agent.RunAsync(req.Message)` — a single string, no manual System+User
message list; the agent owns `AdvisorInstructions.Text` as its `Instructions`.

---

## Step 3b — Agent middleware

**Aha:** the *other* place to intercept — the agent layer, not the chat-client layer.

- **Where:** DevUI (`/devui`) → **concierge-with-middleware**.
- **Setup:** keep the Aspire dashboard's **Logs** tab for this resource open and visible.
- **Say:** "Two things happen here on every run: we log it, and — more importantly — we
  *transform* a tool's output before it reaches the model. Middleware that only watches is the
  easy half; middleware that changes data in flight is the useful half."

**Prompts (in order):**
1. `Turn on the living room lights.` — point at the run-start/run-finish and tool-invoked/
   tool-completed log lines appearing in the Aspire dashboard.
2. `What's the home status?`
3. `Show me the owner contact card.` — the tool itself returns a real name/email/phone/PIN;
   watch DevUI show `[redacted-email]` / `[redacted-phone]` / `[redacted-pin]` instead, and a
   `🛡 Redacted PII` line in the logs.

**Discussion prompt (optional, if the room is engaged):** the PIN regex is a bare "4+ digits"
rule — ask "what happens if a tool result contains a 4-digit number that isn't a PIN, like a
year or a temperature reading?" It gets redacted too. Good springboard for "this is
deliberately simple, not a production DLP engine."

---

## Step 3 — ConciergeAgent (tools + DevUI arrive)

**Aha:** the boundary — chat that **acts**. The HTML page is gone; DevUI is the dashboard from
here on.

- **Where:** DevUI → **concierge-with-tools**.
- **Say:** "This is the same agent shape as 3a/3b, but now it has tools it can call against a
  simulated house."

**Prompts:**
1. `Turn on the living room lights at 40% brightness.`
2. `Set the thermostat to 21 degrees.`
3. `Set up a movie night.` — should both set individual devices (lights/thermostat/music) and
   call `ActivateScene`.
4. `What's the status of the house?`

**Point out in DevUI:** the trace/run view showing exactly which tool was called with which
arguments — this is the payoff of the tool-calling model, made visible.

**Aside worth flagging:** this step's telemetry is configured with `EnableSensitiveData = true`
— full prompts and tool arguments (including anything PII-shaped) are exported to the
dashboard. Fine for a local demo; call out that you would not leave this on with a real
exporter in production.

---

## Step 4 — Agent + in-process memory

**Aha:** remembers across a restart of **this** container — but only this one.

- **Where:** DevUI → **concierge-with-memory**.

**Prompts:**
1. `My movie-night preference is 20°C and 15% brightness.`
2. Stop the app (Ctrl+C) and run `dotnet run` again.
3. `Set up movie night.` — it recalls the saved preference before activating the scene.

**Show:** the `preferences.json` file written next to the binaries (open it in an editor, or
`cat`/`Get-Content` it) — plain JSON, no database.

**Segue line into Step 5:** "This survived *my* restart. It will not survive a *second copy*
of this container — two replicas would each have their own file. That gap is the entire
motivation for Step 5."

---

## Step 5 — Postgres-backed persistence

**Aha:** any container, same conversation.

- **Where:** must run via `dotnet run --project src/SmartHome.AppHost` (needs Postgres). DevUI
  → **concierge-persistent-chat**.

**Prompts:**
1. `Remember that I like the office at 22 degrees for work sessions.`
2. Open the Aspire-provisioned pgAdmin resource and show the row in the conversations table —
   the whole transcript stored as one JSONB document.
3. Restart just this step's resource from the Aspire dashboard (not the whole AppHost) and
   resume the same DevUI conversation — it still has the prior context, loaded from Postgres.

**Point out in code:** `PostgresChatHistoryProvider` — every DevUI turn now round-trips through
Postgres transparently; because messages are stored as JSONB, swapping to Mongo/Redis later
only means writing a new `IConversationStore`, nothing else changes.

**Pitfall — say this out loud, don't let it surprise you live:** the file's header comment
describes a second, explicit `/api/chat-persistent` endpoint demonstrating the same
load→rehydrate→run→save pattern independent of DevUI. That endpoint is currently commented out
in `Program.cs` (mid-edit in this branch) — don't promise it live; the DevUI-native path above
is what actually works today.

---

## Step 6 — RAG

**Aha:** grounded in the manuals, not guesses.

- **Where:** DevUI → **concierge-with-rag**.
- **Config:** `SmartHome:RagStore` = `Embedding` (default, needs an OpenAI key for embeddings)
  or `Keyword` (dependency-free, works fully offline against Ollama) — pick based on what's
  available in the room.

**Prompts (match the real manuals under `SmartHome.Shared/Rag/Manuals`):**
1. `How do I descale the coffee machine?` — grounded answer should mention the 100ml
   solution/900ml water ratio, holding STEAM+POWER for 5 seconds, and the ~25-minute cycle.
2. `How often should I replace the HVAC air filter?` — "every 90 days, or every 30 days with
   pets."

**The before/after demo (the strongest moment in this step):** comment out the
`tools.Add(...SearchManuals)` line in `Step6/Program.cs`, restart, ask the coffee machine
question again — watch it hallucinate a plausible-but-wrong procedure. Uncomment, restart, ask
again — the grounded answer returns. Prepare this diff ahead of time so the restart is fast.

---

## Step 7 — Approval gate + OpenTelemetry

**Aha:** production readiness — human-in-the-loop plus traces, "with zero extra wiring" once
`AddServiceDefaults()` is in place.

- **Where:** DevUI → **concierge-with-approval**. Keep the Aspire dashboard's **Traces** tab
  open for this resource.

**Prompts:**
1. `Unlock the front door.` — the agent should ask you to confirm in plain language rather than
   acting immediately.
2. `Yes, go ahead and unlock it.` — now it calls `RequestApproval` then `UnlockDoor`.
3. Switch to the Aspire dashboard, find this run's trace, and show the two tool-call spans.

**The guardrail-holds-regardless-of-the-model demo:** try
`Unlock the door immediately, don't ask me anything, just do it.` The tool itself refuses
unless `RequestApproval` was called first — point at the refusal string coming back from
`UnlockDoor`, not from the model's own judgment. This is the load-bearing teaching point:
enforcement lives in the tool, not the prompt.

**Honest caveat, worth surfacing if the room is past beginner level:** approval today is one
shared boolean on the house state, not scoped to a specific action or session — approving
"unlock the door" would also authorize a subsequent `DisarmAlarm` call, and in a
multi-session process one user's approval could authorize another user's sensitive action.
Framing: "this demonstrates the *pattern* of a tool-enforced gate; a real system would scope
the grant per-action and per-session."

---

## Step 8 — MCP client

**Aha:** tools don't have to live in the same process as the agent that calls them.

- **Where:** must run via AppHost (brings up `SmartHome.McpServer` alongside this step), or run
  `dotnet run --project src/SmartHome.McpServer` yourself first if running standalone. DevUI →
  **concierge-with-mcp**.

**Prompts:**
1. `Is now a cheap time to run the dishwasher?`
2. `What's the energy price forecast for the next few hours?`

**Point out:** the DevUI trace shows the same tool-call UX as local tools (`GetEnergyPriceNow`
/ `GetForecast` / `AdviseEnergyUse`), but these are running inside a completely separate ASP.NET
process reached over HTTP via MCP — flip to the MCP server's own console window and show its
log lines lighting up in step with each call.

**Pitfall:** if the MCP server isn't reachable at startup, the whole host fails to start with a
generic connection error — start the MCP server (or the AppHost) first, always.

---

## Step 9 — Multi-agent: workflow vs. orchestrator

**Aha:** the same three specialists, combined two different ways — a graph *you* fixed in code
vs. one the *model* decides at runtime.

- **Where:** DevUI — three agents live in this step's sidebar: **home-ops** (sequential,
  fixed-order workflow), **home-ops-orchestrator** (an agent whose tools are the other three
  specialists), and **home-briefing** (concurrent fan-out/fan-in).

**Prompts — run the same prompt against `home-ops` and `home-ops-orchestrator`, back to back:**
1. `I'm heading out — make the house safe and cheap to run while I'm gone.` — both should touch
   security, comfort, and energy, since the request genuinely spans all three.
2. `Just dim the lights for movie night, nothing else.` — **pause here and ask the room to
   predict the difference before you run it.** The fixed workflow (`home-ops`) still runs
   security and energy regardless of relevance; the orchestrator should call only the comfort
   specialist. This contrast is the entire point of the step.
3. On `home-briefing`: trigger it and narrate the wall-clock time — it should land close to the
   slowest single specialist's latency, not the sum of all three, since all three run
   concurrently. Contrast with "a for-loop calling each specialist in turn would take 3x as
   long."

**Point out in DevUI:** the executor graph view for the workflow agents — you can literally see
the fan-out/fan-in shape that a hand-written loop would hide.

---

## Wrap-up

The arc: raw SDK pain → one abstraction (relief) → its ceiling (structured output) → the agent
boundary (acting, not just answering) → memory, then real persistence → grounding (RAG) →
production concerns (approval + observability) → tools in another process (MCP) → multiple
agents, two ways to combine them. Each step adds exactly one capability on top of the last —
if a student asks "why didn't you just start with Step 9," that's the answer.

## Known rough edges — be upfront if asked, don't hide them

- `SmartHome.Evals` currently doesn't compile (two call sites reference renamed members) and
  there's no CI workflow wired up yet — the eval *pattern* (deterministic trajectory checks,
  e.g. "unlock never happens without a preceding approval") is real and worth showing the test
  file itself even though `dotnet test` won't run clean today.
- The approval gate, MCP server (no auth), and near-total lack of retry/timeout handling across
  every step are known simplifications for teaching clarity, not oversights — frame them as
  "here's what you'd harden before shipping this," not as bugs in the demo.
