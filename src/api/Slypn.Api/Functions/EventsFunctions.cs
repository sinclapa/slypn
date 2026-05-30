using System.Net;
using System.Web;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Slypn.Api.Services;

namespace Slypn.Api.Functions;

public sealed class EventsFunctions(IContentRepository repo)
{
    [Function("GetEvents")]
    public async Task<HttpResponseData> GetEvents(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "events")] HttpRequestData req,
        CancellationToken ct)
    {
        var upcoming = HttpUtility.ParseQueryString(req.Url.Query)["upcoming"];
        var onlyUpcoming = string.Equals(upcoming, "true", StringComparison.OrdinalIgnoreCase);

        var events = await repo.ListEventsAsync(onlyUpcoming, ct);
        var resp = req.CreateResponse(HttpStatusCode.OK);
        await resp.WriteAsJsonAsync(events);
        return resp;
    }
}
