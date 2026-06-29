using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using OllamaSharp;
using OpenAI;
using OpenAI.Chat;
using System.ClientModel;

namespace SmartHome.Shared.Hosting;

/// <summary>
/// Builds an IChatClient from configuration. This is THE class Step 1 introduces — before
/// it, Steps 0a/0b hard-code their provider's native client so students feel the contract
/// differences directly; from Step 1 onward, everything funnels through here.
///
/// appsettings.json:
///   "SmartHome": { "Provider": "OpenAI" | "Ollama" }
///
/// Under Aspire (recommended for the workshop room), the Ollama endpoint and model come
/// from service discovery instead of literal config — see UseAspireOllama below. Aspire
/// injects an env var / config entry like "services:ollama:chat:0" pointing at the
/// container; .AddServiceDefaults() + .AddOllamaApiClient("chat") (CommunityToolkit.Aspire)
/// resolve it automatically. Verify the exact extension name against the current
/// CommunityToolkit.Aspire.Hosting.Ollama version — this integration moves quickly.
///
/// The OpenAI API key is read from the "openai" connection string in configuration.
/// </summary>
public static class ChatClientSetup
{
    public static void RegisterAIClient(IHostApplicationBuilder builder)
    {
        var section = builder.Configuration.GetSection("SmartHome");
        var provider = (section["Provider"] ?? "Ollama").Trim();
        
        if(provider.ToLowerInvariant() == "openai")
        {
            builder.AddOpenAIClient("openai-chat").AddChatClient();
        }
        else if (provider.ToLowerInvariant() == "ollama")
        {
            builder.AddOllamaApiClient("ollama-chat", x=> x.SelectedModel = builder.Configuration["OLLAMA_CHAT_MODEL"]).AddChatClient();
        }
        else
        {
            throw new InvalidOperationException($"Unknown SmartHome:Provider '{provider}'. Use OpenAI or Ollama.");
        }
    }
    
}
