using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Slypn.Api.Services;

namespace Slypn.Api.Functions;

public sealed class NewslettersFunctions(IContentRepository repo)
{
    [Function("GetNewsletters")]
    public async Task<HttpResponseData> GetNewsletters(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "newsletters")] HttpRequestData req,
        CancellationToken ct)
    {
        var items = await repo.ListNewslettersAsync(ct);
        var resp = req.CreateResponse(HttpStatusCode.OK);
        await resp.WriteAsJsonAsync(items);
        return resp;
    }
}
