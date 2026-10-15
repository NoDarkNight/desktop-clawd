using System.Diagnostics;
using System.Net;
using System.Text.Json;

namespace DesktopClawd;

/// <summary>
/// Receives Claude Code hook notifications on the local machine only:
/// <c>POST http://localhost:47821/claude/{working|attention|done|idle}</c> with header <c>X-Clawd: 1</c>.
/// The header is required so web pages in a browser can't trigger it (browsers can't send
/// custom headers cross-origin without a CORS preflight, which this never approves).
/// For "working", the hook's JSON body says which tool Claude is using, which picks the animation.
/// </summary>
public sealed class ClaudeListener(Action<ClaudeActivity, WorkKind> onActivity) : IDisposable
{
    public const int Port = 47821;
    private const long MaxBodyToParse = 4 * 1024 * 1024; // tool inputs can include whole files

    private readonly HttpListener _http = new() { Prefixes = { $"http://localhost:{Port}/claude/" } };

    /// <summary>Starts listening. Returns false if the port is unavailable (e.g. another pet is running).</summary>
    public bool Start()
    {
        try
        {
            _http.Start();
        }
        catch (HttpListenerException ex)
        {
            Trace.WriteLine($"Claude listener disabled: {ex.Message}");
            return false;
        }
        _ = ListenAsync();
        return true;
    }

    private async Task ListenAsync()
    {
        while (_http.IsListening)
        {
            HttpListenerContext ctx;
            try
            {
                ctx = await _http.GetContextAsync();
            }
            catch (Exception) when (!_http.IsListening)
            {
                return;
            }

            var request = ctx.Request;
            ClaudeActivity? activity = request.Url?.Segments.LastOrDefault()?.Trim('/').ToLowerInvariant() switch
            {
                "working" => ClaudeActivity.Working,
                "attention" => ClaudeActivity.NeedsAttention,
                "done" => ClaudeActivity.Done,
                "idle" => ClaudeActivity.None,
                _ => null,
            };
            var valid = activity is not null && request.HttpMethod == "POST" && request.Headers["X-Clawd"] == "1";

            var kind = valid && activity == ClaudeActivity.Working ? await ReadWorkKindAsync(request) : WorkKind.Typing;

            ctx.Response.StatusCode = valid ? 204 : 404;
            ctx.Response.Close();
            if (valid) onActivity(activity!.Value, kind);
        }
    }

    /// <summary>Reads "tool_name" from the hook payload (absent for a newly submitted prompt).</summary>
    private static async Task<WorkKind> ReadWorkKindAsync(HttpListenerRequest request)
    {
        if (!request.HasEntityBody || request.ContentLength64 > MaxBodyToParse) return WorkKind.Typing;
        try
        {
            using var json = await JsonDocument.ParseAsync(request.InputStream);
            var tool = json.RootElement.TryGetProperty("tool_name", out var name) ? name.GetString() : null;
            return ClaudeTools.KindOf(tool);
        }
        catch (JsonException)
        {
            return WorkKind.Typing;
        }
    }

    public void Dispose() => _http.Close();
}
