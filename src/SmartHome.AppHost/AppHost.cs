

var builder = DistributedApplication.CreateBuilder(args);

var openai = builder.AddOpenAI("openai");

var openAichat = openai.AddModel("openai-chat", "gpt-4o-mini");

// ---- Ollama: one container, one model, shared by every step that uses it ----
var ollama = builder.AddOllama("ollama")
    .WithDataVolume()      // persist pulled models across restarts
    .WithLifetime(ContainerLifetime.Persistent);

var ollamaChat = ollama.AddModel("ollama-chat", "gemma3:4b"); // 3B: fast cold start, tool-capable

//// ---- Postgres: for Step 5's persistent, multi-container-safe conversation history ----
var postgres = builder.AddPostgres("postgres")
    .WithDataVolume()
    .WithPgAdmin();

var conversationsDb = postgres.AddDatabase("conversations");

//----Steps 0a / 0b / 1 / 2: the advisor(plain ASP.NET endpoints + embedded HTML page) ----
builder.AddProject<Projects.SmartHome_Step0a_AdvisorOpenAI>("step0a-advisor-openai")
   .WithReference(openai);

builder.AddProject<Projects.SmartHome_Step0b_AdvisorOllama>("step0b-advisor-ollama")
    .WithReference(ollamaChat)
    .WithReference(openAichat);

builder.AddProject<Projects.SmartHome_Step1_AdvisorAbstracted>("step1-advisor-abstracted")
    .WithReference(ollamaChat)
    .WithReference(openAichat);

builder.AddProject<Projects.SmartHome_Step2_AdvisorStructured>("step2-advisor-structured")
    .WithReference(ollamaChat)
    .WithReference(openAichat);


////// ---- Step 3a: the advisor as an AIAgent (no tools) — the bridge between Step 1 and Step 3 ----
builder.AddProject<Projects.SmartHome_Step3a_AdvisorAgentNoTools>("step3a-advisor-agent-notools")
    .WithReference(ollamaChat)
    .WithReference(openAichat);

//// ---- Step 3b: agent middleware (run + function-invocation interceptors) ----
builder.AddProject<Projects.SmartHome_Step3b_AgentWithMiddleware>("step3b-agent-middleware")
    .WithReference(ollamaChat)
    .WithReference(openAichat);

//// ---- Steps 3/4: the agent, hosted via AddAIAgent + DevUI (no more HTML page) ----
builder.AddProject<Projects.SmartHome_Step3_ConciergeAgent>("step3-concierge-agent")
    .WithReference(ollamaChat)
    .WithReference(openAichat);

builder.AddProject<Projects.SmartHome_Step4_AgentWithMemory>("step4-agent-memory")
    .WithReference(ollamaChat)
    .WithReference(openAichat);

//// ---- Step 5: agent + Postgres-backed conversation history ----
builder.AddProject<Projects.SmartHome_Step5_PersistentChat>("step5-persistent-chat")
    .WithReference(openAichat)
    .WithReference(conversationsDb)
    .WaitFor(openAichat)
    .WaitFor(conversationsDb);

//// ---- Step 6: RAG over the appliance manuals ----
builder.AddProject<Projects.SmartHome_Step6_AgentWithRag>("step6-agent-rag")
    .WithReference(openAichat)
    .WaitFor(openAichat);

//// ---- Step 7: approval gate + OpenTelemetry (traces visible in the Aspire dashboard) ----
builder.AddProject<Projects.SmartHome_Step7_AgentWithApproval>("step7-agent-approval")
    .WithReference(openAichat)
    .WaitFor(openAichat);

//// ---- Step 8: MCP — the energy/weather HTTP server + the agent that consumes it ----
var mcpServer = builder.AddProject<Projects.SmartHome_McpServer>("mcp-energy-server")
    .WithHttpEndpoint()
    .WithHttpsEndpoint()
    .WithExternalHttpEndpoints();

builder.AddProject<Projects.SmartHome_Step8_AgentWithMcp>("step8-agent-mcp")
    .WithReference(openAichat)
    .WithReference(mcpServer)
    .WaitFor(openAichat)
    .WaitFor(mcpServer);

////// ---- Step 9: comfort/security/energy specialists + a sequential workflow ----
builder.AddProject<Projects.SmartHome_Step9_MultiAgentWorkflow>("step9-multi-agent")
    .WithReference(openAichat)
    .WithReference(mcpServer)
    .WaitFor(openAichat)
    .WaitFor(mcpServer);

builder.Build().Run();
