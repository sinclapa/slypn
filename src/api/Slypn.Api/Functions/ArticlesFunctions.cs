using System.Net;
using System.Web;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Slypn.Api.Services;

namespace Slypn.Api.Functions;

public sealed class ArticlesFunctions(IContentRepository repo)
{
    [Function("GetArticles")]
    public async Task<HttpResponseData> GetArticles(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "articles")] HttpRequestData req,
        CancellationToken ct)
    {
        var status = HttpUtility.ParseQueryString(req.Url.Query)["status"];
        var articles = await repo.ListArticlesAsync(status, ct);
        var resp = req.CreateResponse(HttpStatusCode.OK);
        await resp.WriteAsJsonAsync(articles);
        return resp;
    }

    [Function("GetArticleBySlug")]
    public async Task<HttpResponseData> GetArticleBySlug(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "articles/{slug}")] HttpRequestData req,
        string slug,
        CancellationToken ct)
    {
        var article = await repo.GetArticleBySlugAsync(slug, ct);
        if (article is null) return req.CreateResponse(HttpStatusCode.NotFound);
        var resp = req.CreateResponse(HttpStatusCode.OK);
        await resp.WriteAsJsonAsync(article);
        return resp;
    }
}
