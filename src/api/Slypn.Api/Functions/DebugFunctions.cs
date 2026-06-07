using System.Text;
using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace Slypn.Api.Functions;

/// <summary>
/// TEMPORARY — remove after diagnosing SWA auth header forwarding. #debug
/// </summary>
public sealed class DebugFunctions
{
    [Function("AuthDebug")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "auth-debug")] HttpRequestData req)
    {
        var resp = req.CreateResponse();
        resp.Headers.Add("Content-Type", "application/json");

        var auth = req.Headers.TryGetValues("Authorization", out var vals)
            ? vals.FirstOrDefault() : null;

        string? kid = null;
        string? alg = null;
        string? typ = null;

        if (auth?.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) == true)
        {
            var token = auth["Bearer ".Length..].Trim();
            var seg   = token.Split('.').FirstOrDefault();
            if (seg is not null)
            {
                try
                {
                    var padded  = seg.PadRight(seg.Length + (4 - seg.Length % 4) % 4, '=')
                                     .Replace('-', '+').Replace('_', '/');
                    var hdrJson = Encoding.UTF8.GetString(Convert.FromBase64String(padded));
                    using var doc = JsonDocument.Parse(hdrJson);
                    kid = doc.RootElement.TryGetProperty("kid", out var k) ? k.GetString() : "<MISSING>";
                    alg = doc.RootElement.TryGetProperty("alg", out var a) ? a.GetString() : null;
                    typ = doc.RootElement.TryGetProperty("typ", out var t) ? t.GetString() : null;
                }
                catch { kid = "<decode-error>"; }
            }
        }

        await resp.WriteStringAsync(JsonSerializer.Serialize(new
        {
            authorizationPresent = auth is not null,
            bearerPresent        = auth?.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase),
            tokenLength          = auth?.Length,
            header               = new { kid, alg, typ },
            xMsClientPrincipal   = req.Headers.TryGetValues("x-ms-client-principal", out var cp)
                                       ? cp.FirstOrDefault() : null,
        }));

        return resp;
    }
}
