using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Slypn.Api.Services;

namespace Slypn.Api.Functions;

public sealed class ResourcesFunctions(IMockDataService data)
{
    [Function("GetResources")]
    public async Task<HttpResponseData> GetResources(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "resources")] HttpRequestData req)
    {
        var resp = req.CreateResponse(HttpStatusCode.OK);
        await resp.WriteAsJsonAsync(data.Resources);
        return resp;
    }
}
