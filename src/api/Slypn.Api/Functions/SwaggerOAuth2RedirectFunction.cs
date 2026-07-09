using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace Slypn.Api.Functions;

/// <summary>
/// Serves the Swagger UI OAuth2 redirect page. The isolated-worker OpenAPI
/// extension does not register this endpoint automatically; Swagger UI derives
/// the redirect URL from its own page path, so it must live here.
/// </summary>
public sealed class SwaggerOAuth2RedirectFunction
{
    private const string Html = """
        <!doctype html>
        <html lang="en-US">
        <body onload="run()"></body>
        </html>
        <script>
          'use strict';
          function run() {
            var oauth2 = window.opener.swaggerUIRedirectOauth2;
            var sentState = oauth2.state;
            var redirectUrl = oauth2.redirectUrl;
            var isValid, qp, arr;

            if (/code|token|error/.test(window.location.hash)) {
              qp = window.location.hash.substring(1).replace('?', '&');
            } else {
              qp = location.search.substring(1);
            }

            arr = qp.split('&');
            arr.forEach(function(v, i, _arr) { _arr[i] = '"' + v.replace('=', '":"') + '"'; });
            qp = qp ? JSON.parse('{' + arr.join() + '}', function(key, value) {
              return key ? decodeURIComponent(value) : value;
            }) : {};

            isValid = qp.state === sentState;

            // Swagger UI's authorizeAccessCodeWithFormParams reads auth.code directly;
            // it does not read from the token argument, so we must set it here.
            if (qp.code) { oauth2.auth.code = qp.code; }

            if ((
              oauth2.auth.schema.get('flow') === 'accessCode' ||
              oauth2.auth.schema.get('flow') === 'authorizationCode' ||
              oauth2.auth.schema.get('flow') === 'pkce'
            ) && !oauth2.auth.bearerFormat && window.opener && isValid) {
              oauth2.callback({ auth: oauth2.auth, redirectUrl: redirectUrl });
            } else {
              oauth2.callback({ auth: oauth2.auth, token: qp, isValid: isValid, redirectUrl: redirectUrl });
            }
            window.close();
          }
        </script>
        """;

    [Function("SwaggerOAuth2Redirect")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "swagger/oauth2-redirect.html")] HttpRequestData req)
    {
        var response = req.CreateResponse(HttpStatusCode.OK);
        response.Headers.Add("Content-Type", "text/html; charset=utf-8");
        response.Headers.Add("Cache-Control", "no-store");
        await response.WriteStringAsync(Html);
        return response;
    }
}
