using System.Diagnostics;
using System.Net;

namespace DesktopClawd;

/// <summary>
/// Receives Claude Code hook notifications on the local machine only:
/// <c>POST http://localhost:47821/claude/{working|attention|done|idle}</c> with header <c>X-Clawd: 1</c>.
/// The header is required so web pages in a browser can't trigger it (browsers can't send
/// custom headers cross-origin without a CORS preflight, which this never approves).
/// </summary>
public sealed class ClaudeListener(Action<ClaudeActivity> onActivity) : IDisposable
{
    public const int Port = 47821;

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

            ctx.Response.StatusCode = valid ? 204 : 404;
            ctx.Response.Close();
            if (valid) onActivity(activity!.Value);
        }
    }

    public void Dispose() => _http.Close();
}
