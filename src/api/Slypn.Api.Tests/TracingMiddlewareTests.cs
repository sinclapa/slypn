using Slypn.Api.Infrastructure;
using Xunit;

namespace Slypn.Api.Tests;

public class TracingMiddlewareTests
{
    [Fact]
    public async Task Invoke_calls_next_and_returns_for_non_http_trigger()
    {
        // GetHttpRequestDataAsync returns null when IFunctionBindingsFeature is absent from
        // the context features store. TracingMiddleware must skip its tracing logic and call
        // the next middleware delegate when the request is null (e.g. timer / queue triggers).
        var middleware = new TracingMiddleware();
        var ctx = new TestMiddlewareContext("Slypn.Api.Functions.BlogFunctions.GetBlogPosts");
        var called = false;

        await middleware.Invoke(ctx, _ => { called = true; return Task.CompletedTask; });

        Assert.True(called);
    }
}
