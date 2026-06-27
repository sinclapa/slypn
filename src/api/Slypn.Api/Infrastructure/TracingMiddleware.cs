using System.Diagnostics;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Middleware;
using OpenTelemetry;
using OpenTelemetry.Context.Propagation;

namespace Slypn.Api.Infrastructure;

/// <summary>
/// Extracts W3C traceparent/tracestate from each incoming HTTP request and
/// starts a server Activity parented to the caller's trace, so Azure Storage
/// spans appear as children of the browser span in Grafana Tempo.
/// </summary>
public sealed class TracingMiddleware : IFunctionsWorkerMiddleware
{
    private static readonly TextMapPropagator Propagator = Propagators.DefaultTextMapPropagator;

    public async Task Invoke(FunctionContext context, FunctionExecutionDelegate next)
    {
        var request = await context.GetHttpRequestDataAsync();
        if (request is null)
        {
            await next(context);
            return;
        }

        var parentContext = Propagator.Extract(
            default,
            request.Headers,
            static (headers, key) =>
                headers.TryGetValues(key, out var values)
                    ? values
                    : Enumerable.Empty<string>());

        Baggage.Current = parentContext.Baggage;

        using var activity = OtelSources.Api.StartActivity(
            context.FunctionDefinition.Name,
            ActivityKind.Server,
            parentContext.ActivityContext);

        activity?.SetTag("faas.invocation_id", context.InvocationId);
        activity?.SetTag("faas.name", context.FunctionDefinition.Name);

        await next(context);
    }
}
