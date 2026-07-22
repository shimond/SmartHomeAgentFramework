// =====================================================================================
// SmartHome.McpServer — an MCP server (HTTP) exposing energy/weather tools.
// -------------------------------------------------------------------------------------
// Runs as a plain ASP.NET web app so Aspire can register it as a named resource and
// inject its URL into consumers (Step 8, Step 9) via service discovery — no subprocess
// path-hacking needed. Clients connect with HttpClientTransport using the discovered URL.
//
// This file is just the composition root: register the tool dependencies in DI, then let
// the MCP SDK expose the tools. The tools (EnergyMcpTools) are a DI-activated INSTANCE
// class over an injected IEnergyService + TimeProvider — see EnergyMcpTools.cs /
// EnergyService.cs — so there is no static state and the clock is testable.
// =====================================================================================

using SmartHome.McpServer;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();

// Tool dependencies. TimeProvider.System is the real clock; a fake can be substituted in tests.
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IEnergyService, EnergyService>();

builder.Services
    .AddMcpServer()
    .WithHttpTransport()
    // Explicit (AOT-friendly) registration of the one tool type, instead of assembly scanning.
    .WithTools<EnergyMcpTools>();

var app = builder.Build();
app.MapDefaultEndpoints();
app.MapMcp();

app.Run();
