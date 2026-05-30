using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Slypn.Api.Services;

namespace Slypn.Api.Functions;

public sealed class ArticlesFunctions(IMockDataService data)
{
    [Function("GetArticles")]
    public async Task<HttpResponseData> GetArticles(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "articles")] HttpRequestData req)
    {
        var resp = req.CreateResponse(HttpStatusCode.OK);
        await resp.WriteAsJsonAsync(data.Articles);
        return resp;
    }

    [Function("GetArticleBySlug")]
    public async Task<HttpResponseData> GetArticleBySlug(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "articles/{slug}")] HttpRequestData req,
        string slug)
    {
        var article = data.Articles.FirstOrDefault(a =>
            string.Equals(a.Slug, slug, StringComparison.OrdinalIgnoreCase));
        if (article is null)
        {
            return req.CreateResponse(HttpStatusCode.NotFound);
        }
        var resp = req.CreateResponse(HttpStatusCode.OK);
        await resp.WriteAsJsonAsync(article);
        return resp;
    }
}
