using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.Azure.Functions.Worker;
using Slypn.Api.Infrastructure;
using Xunit;

namespace Slypn.Api.Tests;

/// <summary>
/// The whole HTTP surface, pinned in one table.
///
/// Three facts about each endpoint live in three different places and have to agree: the route the
/// client calls, the <c>[Function]</c> name, and the role gate. The middleware resolves that gate by
/// reflecting on <c>FunctionDefinition.EntryPoint</c> — <c>Namespace.Type.Method</c> — so moving a
/// handler between classes silently changes what the gate is attached to, while every other part of
/// the codebase thinks in routes. Nothing else connects the three.
///
/// So a route disappearing, a handler changing class, or a gate falling off shows up here as a diff
/// in a sorted table, in under a second, rather than as a 404 in Playwright twenty minutes later.
/// Update the expected list deliberately; a surprise here is the point.
/// </summary>
public class RouteInventoryTests
{
    private sealed record Endpoint(string Function, string EntryPoint, string Methods, string Route, string Gate)
    {
        public override string ToString() => $"{Methods,-6} /{Route,-38} {Function,-26} {Gate,-28} {EntryPoint}";
    }

    private static IEnumerable<Endpoint> Discover()
    {
        foreach (var type in typeof(Slypn.Api.Functions.ArticlesFunctions).Assembly.GetTypes())
        {
            foreach (var m in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
            {
                var fn = m.GetCustomAttribute<FunctionAttribute>();
                if (fn is null) continue;

                var trigger = m.GetParameters()
                    .Select(p => p.GetCustomAttribute<HttpTriggerAttribute>())
                    .FirstOrDefault(t => t is not null);
                if (trigger is null) continue; // non-HTTP trigger

                var role = m.GetCustomAttribute<RequireRoleAttribute>();
                var gate = role switch
                {
                    null => "anonymous",
                    { Optional: true } => "optional-auth",
                    { Roles.Length: 0 } => "authenticated",
                    _ => string.Join('+', role.Roles.OrderBy(r => r)),
                };

                yield return new Endpoint(
                    fn.Name,
                    $"{type.FullName}.{m.Name}",
                    string.Join('|', (trigger.Methods ?? []).Select(x => x.ToUpperInvariant()).OrderBy(x => x)),
                    trigger.Route ?? "",
                    gate);
            }
        }
    }

    // Sorted by route then method so the diff reads like the Swagger page.
    private static readonly string[] Expected =
    [
        "DELETE /content/{id}                            DeleteArticle              Admin                        Slypn.Api.Functions.ContentFunctions.Delete",
        "GET    /articles                                 GetArticles                optional-auth                Slypn.Api.Functions.ArticlesFunctions.GetArticles",
        "POST   /content                                 CreateArticle              Admin+Contributor            Slypn.Api.Functions.ContentFunctions.Create",
        "GET    /articles/{slug}                          GetArticleBySlug           optional-auth                Slypn.Api.Functions.ArticlesFunctions.GetArticleBySlug",
        "PUT    /content/{id}                            ReplaceArticle             Admin+Contributor            Slypn.Api.Functions.ContentFunctions.Replace",
        "POST   /content/{id}/cancel-deletion            CancelArticleDeletion      Admin                        Slypn.Api.Functions.ContentFunctions.CancelDeletion",
        "POST   /content/{id}/edit                       EditPublishedArticle       Admin+Contributor            Slypn.Api.Functions.ContentFunctions.Edit",
        "POST   /content/{id}/publish                    PublishArticle             Admin                        Slypn.Api.Functions.ContentFunctions.Publish",
        "POST   /content/{id}/request-deletion           RequestArticleDeletion     Admin+Contributor            Slypn.Api.Functions.ContentFunctions.RequestDeletion",
        "POST   /content/{id}/revise                     ReviseArticle              Admin                        Slypn.Api.Functions.ContentFunctions.Revise",
        "POST   /content/{id}/withdraw                   WithdrawArticle            Admin+Contributor            Slypn.Api.Functions.ContentFunctions.Withdraw",
        "GET    /blog                                     GetBlogPosts               optional-auth                Slypn.Api.Functions.BlogFunctions.GetBlogPosts",
        "GET    /blog/{slug}                              GetBlogPostBySlug          optional-auth                Slypn.Api.Functions.BlogFunctions.GetBlogPostBySlug",
        "GET    /review/articles                          GetPendingArticles         Admin+Contributor            Slypn.Api.Functions.ArticlesFunctions.GetPendingArticles",
        "GET    /review/blog                              GetPendingBlogPosts        Admin+Contributor            Slypn.Api.Functions.BlogFunctions.GetPendingBlogPosts",
    ];

    [Fact]
    public void The_content_surface_is_what_we_expect()
    {
        // Compared on collapsed whitespace: the padding above is for readability in the
        // failure message, and should not be something anyone has to get right by hand.
        static string Norm(string s) => Regex.Replace(s, @"\s+", " ").Trim();

        var actual = Discover()
            .Where(e => e.Route.StartsWith("articles") || e.Route.StartsWith("blog")
                     || e.Route.StartsWith("content") || e.Route.StartsWith("review/"))
            .Select(e => Norm(e.ToString()))
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToArray();

        var expected = Expected.Select(Norm).OrderBy(s => s, StringComparer.Ordinal).ToArray();

        Assert.Equal(string.Join('\n', expected), string.Join('\n', actual));
    }

    [Fact]
    public void Every_http_endpoint_declares_its_gate_or_is_deliberately_anonymous()
    {
        // A handler with no [RequireRole] gets no principal at all, so it cannot be
        // caller-aware even by accident. Listing the anonymous ones by name means adding
        // one is a deliberate edit here rather than an omission nobody notices.
        var anonymous = Discover()
            .Where(e => e.Gate == "anonymous")
            .Select(e => e.Function)
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToArray();

        // Joined rather than compared as a collection so a mismatch prints the whole list
        // instead of eliding it.
        // Every one of these is deliberately public: the public reads, the swagger redirect
        // page, the anonymous newsletter subscribe, and AllowSignup — which is reachable
        // without a token but gated on the ?k= shared secret instead (see docs/auth-setup.md).
        Assert.Equal(
            "AllowSignup, GetEvent, GetEvents, GetNewsletterFile, GetNewsletters, "
            + "GetResources, SubscribeToNewsletter, SwaggerOAuth2Redirect",
            string.Join(", ", anonymous));
    }
}
