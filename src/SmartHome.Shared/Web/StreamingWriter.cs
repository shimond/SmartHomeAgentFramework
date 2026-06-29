using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.AI;

namespace SmartHome.Shared.Web;

/// <summary>
/// Writes an IChatClient streaming response straight to the HTTP response body as plain
/// text, flushing per chunk. The advisor page's fetch().body.getReader() reads this as a
/// ReadableStream — no SSE framing needed for this simple demo.
/// </summary>
public static class StreamingWriter
{
    public static async Task WriteAsync(
        HttpResponse response, IChatClient chat, string userMessage, string instructions,
        CancellationToken ct)
    {
        response.ContentType = "text/plain; charset=utf-8";
        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, instructions),
            new(ChatRole.User, userMessage)
        };

        await foreach (var update in chat.GetStreamingResponseAsync(messages, cancellationToken: ct))
        {
            if (string.IsNullOrEmpty(update.Text)) continue;
            await response.WriteAsync(update.Text, ct);
            await response.Body.FlushAsync(ct);
        }
    }
}

public record ChatRequest(string Message);
