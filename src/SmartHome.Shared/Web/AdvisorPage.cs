namespace SmartHome.Shared.Web;

/// <summary>
/// One self-contained HTML page (inline CSS/JS, no build step, no framework) used by
/// Steps 0a, 0b, 1 and 2. This is deliberately NOT the point of the lecture — it exists so
/// you can demo /api/chat and /api/stream from a browser without Postman or a separate
/// frontend project. From Step 3 onward this page disappears entirely and DevUI takes over.
///
/// NOTE on the raw string below: with the $$""" prefix, interpolation requires DOUBLE
/// braces {{ }}; a single { or } is emitted literally. That's why {{title}}/{{subtitle}}
/// are doubled (real C# interpolation) while every CSS/JS brace below is single (literal).
///
/// The optional <paramref name="extraBody"/> lets a single step inject its own extra markup
/// (a form + script) WITHOUT changing the page for the other steps that share this class.
/// Step 2 uses it to add a "structured output" tester so the POST endpoint can be exercised
/// from the browser during the demo instead of from curl/Postman.
/// </summary>
public static class AdvisorPage
{
    public static string Html(string title, string subtitle, string extraBody = "") => $$"""
    <!DOCTYPE html>
    <html lang="en">
    <head>
    <meta charset="utf-8" />
    <title>{{title}}</title>
    <style>
      body { font-family: -apple-system, Segoe UI, Arial, sans-serif; max-width: 760px;
             margin: 40px auto; padding: 0 20px; color: #0f172a; background: #f8fafc; }
      h1 { font-size: 22px; margin-bottom: 0; }
      p.sub { color: #64748b; margin-top: 4px; }
      textarea { width: 100%; height: 70px; font-size: 15px; padding: 10px;
                 border: 1px solid #cbd5e1; border-radius: 8px; box-sizing: border-box; }
      .row { display: flex; gap: 10px; margin-top: 10px; }
      button { padding: 10px 16px; border: none; border-radius: 8px; background: #0d9488;
               color: white; font-size: 14px; cursor: pointer; }
      button.secondary { background: #334155; }
      button:disabled { background: #94a3b8; cursor: default; }
      #out { white-space: pre-wrap; background: white; border: 1px solid #e2e8f0;
             border-radius: 8px; padding: 14px; margin-top: 16px; min-height: 60px; }
      .meta { color: #94a3b8; font-size: 12px; margin-top: 6px; }
    </style>
    </head>
    <body>
      <h1>{{title}}</h1>
      <p class="sub">{{subtitle}}</p>

      <textarea id="msg">What smart home system should I get for a small apartment with mostly Philips Hue lights already?</textarea>
      <div class="row">
        <button onclick="send(false)" id="btnChat">Send (full response)</button>
        <button class="secondary" onclick="send(true)" id="btnStream">Send (streaming)</button>
      </div>
      <div id="out">Response will appear here…</div>
      <div class="meta" id="meta"></div>

    <script>
      async function send(stream) {
        const msg = document.getElementById('msg').value;
        const out = document.getElementById('out');
        const meta = document.getElementById('meta');
        document.getElementById('btnChat').disabled = true;
        document.getElementById('btnStream').disabled = true;
        out.textContent = '';
        const started = performance.now();

        if (!stream) {
          const res = await fetch('/api/chat', {
            method: 'POST', headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ message: msg })
          });
          const data = await res.json();
          out.textContent = data.reply;
          meta.textContent = 'GetResponseAsync - ' + Math.round(performance.now() - started) + 'ms (full answer arrived at once)';
        } else {
          const res = await fetch('/api/stream', {
            method: 'POST', headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ message: msg })
          });
          const reader = res.body.getReader();
          const decoder = new TextDecoder();
          let firstChunkAt = null;
          while (true) {
            const chunk = await reader.read();
            if (chunk.done) break;
            if (firstChunkAt === null) firstChunkAt = performance.now();
            out.textContent += decoder.decode(chunk.value, { stream: true });
          }
          meta.textContent = 'GetStreamingResponseAsync - first token at ' + Math.round(firstChunkAt - started) +
                              'ms, done at ' + Math.round(performance.now() - started) + 'ms';
        }
        document.getElementById('btnChat').disabled = false;
        document.getElementById('btnStream').disabled = false;
      }
    </script>
    {{extraBody}}
    </body>
    </html>
    """;

    /// <summary>
    /// Step-2-only tester: a textarea + button that POSTs to /api/recommend-structured and
    /// renders the parsed object, so the structured-output endpoint can be demoed straight
    /// from the browser (no curl/Postman). Pass this into <see cref="Html"/>'s extraBody.
    /// </summary>
    public static string StructuredTester => """
    <h1 style="margin-top:36px">Structured output tester</h1>
    <p class="sub">POSTs to <code>/api/recommend-structured</code> and shows the parsed object. Switch the provider and watch the ceiling.</p>
    <textarea id="smsg">Recommend a starter smart home kit for a small apartment that already has Philips Hue lights.</textarea>
    <div class="row">
      <button onclick="sendStructured()" id="btnStructured">Get structured recommendation</button>
    </div>
    <div id="sout">Structured response will appear here…</div>
    <div class="meta" id="smeta"></div>
    <script>
      async function sendStructured() {
        const msg = document.getElementById('smsg').value;
        const sout = document.getElementById('sout');
        const smeta = document.getElementById('smeta');
        const btn = document.getElementById('btnStructured');
        btn.disabled = true;
        sout.textContent = '';
        smeta.textContent = '';
        const started = performance.now();
        try {
          const res = await fetch('/api/recommend-structured', {
            method: 'POST', headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ message: msg })
          });
          const data = await res.json();
          if (data.parsed) {
            sout.textContent = JSON.stringify(data.parsed, null, 2);
            smeta.textContent = 'provider: ' + data.provider + ' - parsed OK in ' +
                                Math.round(performance.now() - started) + 'ms';
          } else {
            sout.textContent = 'PARSE FAILED\n\n' + (data.parseError || '') +
                               '\n\n--- raw model text ---\n' + (data.raw || '');
            smeta.textContent = 'provider: ' + data.provider + ' - the structured-output ceiling, live';
          }
        } catch (e) {
          sout.textContent = 'Request error: ' + e;
        } finally {
          btn.disabled = false;
        }
      }
    </script>
    """;
}
