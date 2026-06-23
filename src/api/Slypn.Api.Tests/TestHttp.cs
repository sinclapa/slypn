using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Azure.Core.Serialization;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.DependencyInjection;
using Slypn.Api.Infrastructure;

namespace Slypn.Api.Tests;

/// <summary>
/// Minimal in-memory implementations of the isolated-worker HTTP types so Function
/// methods can be invoked directly in unit tests. Only the members the SLYPN
/// functions actually touch are wired up; the rest throw.
/// </summary>
internal sealed class TestFunctionContext : FunctionContext
{
    private readonly IServiceProvider _services = BuildServices();

    private static IServiceProvider BuildServices()
    {
        var sc = new ServiceCollection();
        sc.Configure<WorkerOptions>(o =>
            o.Serializer = new JsonObjectSerializer(new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        return sc.BuildServiceProvider();
    }

    public override IDictionary<object, object> Items { get; set; } = new Dictionary<object, object>();
    public override IServiceProvider InstanceServices { get => _services; set { } }
    public override string InvocationId => "test-invocation";
    public override string FunctionId => "test-function";
    public override TraceContext TraceContext => throw new NotSupportedException();
    public override BindingContext BindingContext => throw new NotSupportedException();
    public override RetryContext RetryContext => throw new NotSupportedException();
    public override FunctionDefinition FunctionDefinition => throw new NotSupportedException();
    public override IInvocationFeatures Features => throw new NotSupportedException();

    /// <summary>Attach a signed-in principal with the given oid/name/roles.</summary>
    public TestFunctionContext WithUser(string oid, string name = "Test User", params string[] roles)
    {
        var identity = new ClaimsIdentity("test", "name", "roles");
        identity.AddClaim(new Claim("oid", oid));
        identity.AddClaim(new Claim("name", name));
        foreach (var r in roles) identity.AddClaim(new Claim("roles", r));
        Items[JwtMiddleware.PrincipalContextKey] = new ClaimsPrincipal(identity);
        return this;
    }
}

internal sealed class TestHttpCookies : HttpCookies
{
    public override void Append(string name, string value) { }
    public override void Append(IHttpCookie cookie) { }
    public override IHttpCookie CreateNew() => throw new NotSupportedException();
}

internal sealed class TestHttpResponseData : HttpResponseData
{
    public TestHttpResponseData(FunctionContext ctx) : base(ctx) { }
    public override HttpStatusCode StatusCode { get; set; } = HttpStatusCode.OK;
    public override HttpHeadersCollection Headers { get; set; } = new();
    public override Stream Body { get; set; } = new MemoryStream();
    public override HttpCookies Cookies { get; } = new TestHttpCookies();

    public string ReadBodyAsString()
    {
        Body.Position = 0;
        using var reader = new StreamReader(Body, Encoding.UTF8, leaveOpen: true);
        return reader.ReadToEnd();
    }

    public T? ReadBodyAs<T>()
    {
        var json = ReadBodyAsString();
        return string.IsNullOrWhiteSpace(json) ? default : JsonSerializer.Deserialize<T>(json, Web);
    }

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);
}

internal sealed class TestHttpRequestData : HttpRequestData
{
    private readonly Stream _body;
    public TestHttpRequestData(FunctionContext ctx, string method, string url, string? body = null,
        IDictionary<string, string>? headers = null) : base(ctx)
    {
        Method = method;
        Url = new Uri(url);
        _body = new MemoryStream(body is null ? Array.Empty<byte>() : Encoding.UTF8.GetBytes(body));
        Headers = new HttpHeadersCollection();
        if (headers is not null)
            foreach (var (k, v) in headers) Headers.Add(k, v);
    }

    public override Stream Body => _body;
    public override HttpHeadersCollection Headers { get; }
    public override IReadOnlyCollection<IHttpCookie> Cookies => Array.Empty<IHttpCookie>();
    public override Uri Url { get; }
    public override IEnumerable<ClaimsIdentity> Identities => Array.Empty<ClaimsIdentity>();
    public override string Method { get; }
    public override HttpResponseData CreateResponse() => new TestHttpResponseData(FunctionContext);
}

internal static class TestHttp
{
    public static TestHttpRequestData Get(FunctionContext ctx, string url) =>
        new(ctx, "GET", url);

    public static TestHttpRequestData Json(FunctionContext ctx, string method, string url, object body,
        IDictionary<string, string>? headers = null) =>
        new(ctx, method, url, JsonSerializer.Serialize(body, new JsonSerializerOptions(JsonSerializerDefaults.Web)), headers);

    public static TestHttpRequestData Raw(FunctionContext ctx, string method, string url, string body) =>
        new(ctx, method, url, body);
}
