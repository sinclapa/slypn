using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Slypn.Api.Services;

namespace Slypn.Api.Functions;

public sealed class ResourcesFunctions(IContentRepository repo)
{
    [Function("GetResources")]
    public async Task<HttpResponseData> GetResources(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "resources")] HttpRequestData req,
        CancellationToken ct)
    {
        var items = await repo.ListResourcesAsync(ct);
        var resp = req.CreateResponse(HttpStatusCode.OK);
        await resp.WriteAsJsonAsync(items);
        return resp;
    }
}
