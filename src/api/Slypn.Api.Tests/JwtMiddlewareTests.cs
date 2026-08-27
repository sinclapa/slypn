using System.Net;
using System.Security.Claims;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Slypn.Api.Infrastructure;
using Slypn.Api.Models;
using Slypn.Api.Services;
using Xunit;

namespace Slypn.Api.Tests;

file sealed class StubJwtValidator : IJwtValidator
{
    private readonly ClaimsPrincipal? _principal;
    private readonly Exception? _throws;

    /// <param name="principal">Returned on validation. Null means validation fails.</param>
    /// <param name="throws">Exception to fail with; defaults to SecurityTokenException.</param>
    public StubJwtValidator(bool isConfigured, ClaimsPrincipal? principal = null, Exception? throws = null)
    {
        IsConfigured = isConfigured;
        _principal = principal;
        _throws = throws;
    }

    public bool IsConfigured { get; }

    /// The token the middleware actually chose — the only way to assert which
    /// header won when several carry usable tokens.
    public string? LastToken { get; private set; }

    public Task<ClaimsPrincipal> ValidateAsync(string token, CancellationToken ct)
    {
        LastToken = token;
        return _principal is not null
            ? Task.FromResult(_principal)
            : throw (_throws ?? new Microsoft.IdentityModel.Tokens.SecurityTokenException("invalid"));
    }
}

public class JwtMiddlewareTests
{
    private static JwtMiddleware Make(
        bool skipAuth = false,
        bool validatorConfigured = true,
        ClaimsPrincipal? principal = null,
        Exception? validatorThrows = null,
        FakeContentRepository? repo = null) =>
        new(
            new StubJwtValidator(validatorConfigured, principal, validatorThrows),
            Options.Create(new EntraOptions { SkipAuth = skipAuth }),
            repo ?? new FakeContentRepository(),
            NullLogger<JwtMiddleware>.Instance);

    private static ClaimsPrincipal PrincipalWith(params string[] roles)
    {
        var identity = new ClaimsIdentity("test", "name", "roles");
        identity.AddClaim(new Claim("oid", "11111111-1111-1111-1111-111111111111"));
        identity.AddClaim(new Claim("name", "Test User"));
        foreach (var role in roles)
            identity.AddClaim(new Claim("roles", role));
        return new ClaimsPrincipal(identity);
    }

    /// A request carrying the given headers. AuthenticateAsync takes the request
    /// directly, so no FunctionContext plumbing is needed.
    private static TestHttpRequestData Request(params (string Name, string Value)[] headers) =>
        new(new TestFunctionContext(), "GET", "http://localhost/api/review/articles", null,
            headers.ToDictionary(h => h.Name, h => h.Value));

    // BlogFunctions.GetBlogPosts has no [RequireRole] → middleware must call next immediately.
    [Fact]
    public async Task Passthrough_when_function_has_no_RequireRole()
    {
        // GetResources carries no auth attribute at all. (GetBlogPosts used to serve as
        // the example here, until it gained [OptionalAuth] — which takes the authenticate
        // path and so no longer exercises this branch.)
        var ctx = new TestMiddlewareContext("Slypn.Api.Functions.ResourcesFunctions.GetResources");
        var called = false;

        await Make().Invoke(ctx, _ => { called = true; return Task.CompletedTask; });

        Assert.True(called);
    }

    // [OptionalAuth] relaxes *authentication*, not the HTTP-trigger requirement: reaching
    // it on a non-HTTP trigger is still a misconfiguration and must fail loudly rather
    // than silently serving everyone as anonymous.
    [Fact]
    public async Task Throws_when_OptionalAuth_is_on_a_non_http_context()
    {
        var ctx = new TestMiddlewareContext("Slypn.Api.Functions.BlogFunctions.GetBlogPosts");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Make().Invoke(ctx, _ => Task.CompletedTask));
    }

    [Fact]
    public void OptionalAuth_is_a_RequireRole_with_no_roles_that_never_refuses()
    {
        var optional = new Slypn.Api.Infrastructure.OptionalAuthAttribute();
        Assert.Empty(optional.Roles);
        Assert.True(optional.Optional);
        // The plain attribute must keep refusing, or every gate in the app opens.
        Assert.False(new Slypn.Api.Infrastructure.RequireRoleAttribute("Admin").Optional);
    }

    // Unknown entry point → GetRoleAttribute returns null → next called.
    [Fact]
    public async Task Passthrough_when_entry_point_not_found()
    {
        var ctx = new TestMiddlewareContext("Does.Not.Exist");
        var called = false;

        await Make().Invoke(ctx, _ => { called = true; return Task.CompletedTask; });

        Assert.True(called);
    }

    // ArticlesFunctions.Create has [RequireRole] but Features returns no IFunctionBindingsFeature,
    // so GetHttpRequestDataAsync returns null → middleware must throw InvalidOperationException.
    [Fact]
    public async Task Throws_when_RequireRole_on_non_http_context()
    {
        var ctx = new TestMiddlewareContext("Slypn.Api.Functions.ContentFunctions.Create");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Make().Invoke(ctx, _ => Task.CompletedTask));
    }

    // [RequireRole("Admin")] present, SkipAuth=true, no HTTP context → still throws before SkipAuth check.
    [Fact]
    public async Task SkipAuth_still_throws_without_http_context()
    {
        var ctx = new TestMiddlewareContext("Slypn.Api.Functions.ContentFunctions.Publish");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Make(skipAuth: true).Invoke(ctx, _ => Task.CompletedTask));
    }

    [Fact]
    public void PrincipalContextKey_is_stable()
    {
        Assert.Equal("Slypn.Principal", JwtMiddleware.PrincipalContextKey);
    }

    // ── Authentication decisions ─────────────────────────────────────────────
    // None of these had any coverage. They are unreachable through Invoke() in a
    // test — that needs IFunctionBindingsFeature, internal to the Worker SDK —
    // and unreachable from e2e too, which runs with SkipAuth=true where a
    // missing persona header resolves to the default admin persona.

    private static readonly RequireRoleAttribute AdminOnly = new("Admin");
    private static readonly RequireRoleAttribute AnyAuthenticated = new();

    [Fact]
    public async Task Unauthorized_when_no_token_is_present()
    {
        var result = await Make().AuthenticateAsync(Request(), AdminOnly, default);

        Assert.False(result.IsAllowed);
        Assert.Equal(HttpStatusCode.Unauthorized, result.RefusalCode);
        Assert.Equal("Missing Bearer token.", result.RefusalMessage);
    }

    [Theory]
    // No empty or whitespace-only case: HttpHeaders.Add rejects both with a
    // FormatException, so such a header cannot reach the app at all. The
    // IsNullOrWhiteSpace branch in ExtractBearer is therefore defensive only.
    [InlineData("Basic abc")]              // wrong scheme
    [InlineData("Bearer ")]                // scheme with no token
    [InlineData("Bearer not-a-jwt")]       // unparseable
    public async Task Unauthorized_when_the_authorization_header_is_unusable(string header)
    {
        var result = await Make().AuthenticateAsync(
            Request(("Authorization", header)), AdminOnly, default);

        Assert.False(result.IsAllowed);
        Assert.Equal(HttpStatusCode.Unauthorized, result.RefusalCode);
    }

    [Fact]
    public async Task Unauthorized_when_the_token_fails_validation()
    {
        // A structurally valid JWT with a kid, so it gets past ExtractBearer and
        // actually reaches the validator.
        var result = await Make().AuthenticateAsync(
            Request(("Authorization", $"Bearer {TokenWithKid()}")), AdminOnly, default);

        Assert.False(result.IsAllowed);
        Assert.Equal(HttpStatusCode.Unauthorized, result.RefusalCode);
        Assert.Contains("Invalid token", result.RefusalMessage);
    }

    [Fact]
    public async Task Unauthorized_when_validation_throws_something_unexpected()
    {
        var result = await Make(validatorThrows: new InvalidOperationException("boom"))
            .AuthenticateAsync(Request(("Authorization", $"Bearer {TokenWithKid()}")), AdminOnly, default);

        Assert.False(result.IsAllowed);
        Assert.Equal(HttpStatusCode.Unauthorized, result.RefusalCode);
        Assert.Equal("Token validation error.", result.RefusalMessage);
    }

    [Fact]
    public async Task ServiceUnavailable_when_auth_is_not_configured()
    {
        var result = await Make(validatorConfigured: false)
            .AuthenticateAsync(Request(("Authorization", $"Bearer {TokenWithKid()}")), AdminOnly, default);

        Assert.False(result.IsAllowed);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, result.RefusalCode);
    }

    [Fact]
    public async Task Forbidden_when_the_token_is_valid_but_the_role_is_wrong()
    {
        var result = await Make(principal: PrincipalWith("Member"))
            .AuthenticateAsync(Request(("Authorization", $"Bearer {TokenWithKid()}")), AdminOnly, default);

        Assert.False(result.IsAllowed);
        Assert.Equal(HttpStatusCode.Forbidden, result.RefusalCode);
        Assert.Equal("Required role: Admin.", result.RefusalMessage);
    }

    [Fact]
    public async Task Allows_when_the_token_is_valid_and_the_role_matches()
    {
        // The member row is what carries the role — the table is authoritative, so a
        // valid token on its own no longer grants anything (see the SEC-3 tests below).
        var repo = new FakeContentRepository { MemberByOid = MemberWith("Admin") };
        var result = await Make(principal: PrincipalWith("Admin"), repo: repo)
            .AuthenticateAsync(Request(("Authorization", $"Bearer {TokenWithKid()}")), AdminOnly, default);

        Assert.True(result.IsAllowed);
        Assert.Null(result.RefusalCode);
        Assert.Contains(result.Principal!.Claims, c => c.Type == "roles" && c.Value == "Admin");
    }

    [Fact]
    public async Task Allows_any_authenticated_caller_when_no_roles_are_listed()
    {
        // [RequireRole] with no roles means "signed in", used by /me and /whoami.
        var result = await Make(principal: PrincipalWith("Member"))
            .AuthenticateAsync(Request(("Authorization", $"Bearer {TokenWithKid()}")), AnyAuthenticated, default);

        Assert.True(result.IsAllowed);
    }

    [Fact]
    public async Task Prefers_X_Slypn_Token_over_Authorization()
    {
        // SWA's gateway overwrites Authorization with its own session token. The
        // real one arrives in X-Slypn-Token, and reading Authorization first
        // would break every authenticated call in production while looking fine
        // locally.
        //
        // Both headers carry a usable kid-bearing token here on purpose. If one
        // were kid-less, ExtractBearer would skip it and fall through to the
        // other, so the test would still pass with the precedence reversed and
        // prove nothing. Asserting which token reached the validator is the only
        // way to pin the ordering.
        var authorizationToken = TokenWithKid("from-authorization");
        var slypnToken = TokenWithKid("from-x-slypn-token");
        var validator = new StubJwtValidator(true, PrincipalWith("Admin"));
        var middleware = new JwtMiddleware(
            validator,
            Options.Create(new EntraOptions { SkipAuth = false }),
            new FakeContentRepository { MemberByOid = MemberWith("Admin") },
            NullLogger<JwtMiddleware>.Instance);

        var result = await middleware.AuthenticateAsync(
            Request(("Authorization", $"Bearer {authorizationToken}"),
                    ("X-Slypn-Token", $"Bearer {slypnToken}")),
            AdminOnly, default);

        Assert.True(result.IsAllowed);
        Assert.Equal(slypnToken, validator.LastToken);
    }

    [Fact]
    public async Task Ignores_a_kid_less_token_rather_than_sending_it_to_the_validator()
    {
        // SWA session tokens have no kid. They must be skipped, not forwarded —
        // forwarding produces a confusing IDX10517 instead of a plain 401.
        var result = await Make(principal: PrincipalWith("Admin"))
            .AuthenticateAsync(Request(("Authorization", "Bearer swa.session.token")), AdminOnly, default);

        Assert.False(result.IsAllowed);
        Assert.Equal("Missing Bearer token.", result.RefusalMessage);
    }

    // ── Dev-persona path (SkipAuth) ──────────────────────────────────────────

    [Fact]
    public async Task SkipAuth_synthesises_the_requested_persona()
    {
        var result = await Make(skipAuth: true).AuthenticateAsync(
            Request((DevPersonas.HeaderName, "contributor")), AnyAuthenticated, default);

        Assert.True(result.IsAllowed);
        Assert.Equal("22222222-2222-2222-2222-222222222222", result.Principal!.FindFirst("oid")?.Value);
    }

    [Fact]
    public async Task SkipAuth_still_enforces_the_role_gate()
    {
        // The escape hatch bypasses token validation, not authorisation — this is
        // what lets the e2e suite assert 403s per persona.
        var result = await Make(skipAuth: true).AuthenticateAsync(
            Request((DevPersonas.HeaderName, "member")), AdminOnly, default);

        Assert.False(result.IsAllowed);
        Assert.Equal(HttpStatusCode.Forbidden, result.RefusalCode);
    }

    [Fact]
    public async Task SkipAuth_falls_back_to_the_default_persona_without_a_header()
    {
        var result = await Make(skipAuth: true)
            .AuthenticateAsync(Request(), AdminOnly, default);

        Assert.True(result.IsAllowed);
        Assert.Equal("11111111-1111-1111-1111-111111111111", result.Principal!.FindFirst("oid")?.Value);
    }

    /// <summary>An unsigned JWT carrying a kid header, so ExtractBearer accepts it
    /// and the stub validator decides the outcome.</summary>
    private static string TokenWithKid(string marker = "default")
    {
        static string Chunk(string json) =>
            Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(json))
                .TrimEnd('=').Replace('+', '-').Replace('/', '_');

        return $"{Chunk("{\"alg\":\"RS256\",\"kid\":\"test-key\"}")}." +
               $"{Chunk($"{{\"oid\":\"{marker}\"}}")}.sig";
    }

    // ── SEC-3: the members table is authoritative for roles ──────────────────

    private static Member MemberWith(params string[] roles) => new(
        Id:          "m1",
        Email:       "test.user@example.com",
        DisplayName: "Test User",
        Roles:       roles,
        Status:      "active",
        InvitedAt:   DateTime.UtcNow.AddDays(-30),
        AcceptedAt:  DateTime.UtcNow.AddDays(-29),
        InvitedBy:   "oid-admin",
        Oid:         "11111111-1111-1111-1111-111111111111");

    [Fact]
    public async Task Demoting_a_member_in_the_table_revokes_the_role_from_a_stale_JWT()
    {
        // Roles can arrive from two places: Entra app-role assignments baked into
        // the JWT, and the members table. Enrichment unioned them, so demoting
        // someone in the table left their old JWT role effective until the token
        // expired — the table was documented as the source of truth but could only
        // ever grant, never revoke.
        var repo = new FakeContentRepository { MemberByOid = MemberWith("Member") };

        var result = await Make(principal: PrincipalWith("Admin"), repo: repo)
            .AuthenticateAsync(Request(("Authorization", $"Bearer {TokenWithKid()}")), AdminOnly, default);

        Assert.False(result.IsAllowed);
        Assert.Equal(HttpStatusCode.Forbidden, result.RefusalCode);
    }

    [Fact]
    public async Task Table_roles_replace_JWT_roles_rather_than_adding_to_them()
    {
        // The claim set itself, not just the access decision: a stale role left on
        // the principal would still reach anything reading claims directly, such as
        // FunctionContextExtensions.IsAdmin().
        var repo = new FakeContentRepository { MemberByOid = MemberWith("Contributor") };

        var result = await Make(principal: PrincipalWith("Admin"), repo: repo)
            .AuthenticateAsync(Request(("Authorization", $"Bearer {TokenWithKid()}")), AnyAuthenticated, default);

        Assert.True(result.IsAllowed);
        var roles = result.Principal!.FindAll("roles").Select(c => c.Value).ToArray();
        Assert.Equal(new[] { "Contributor" }, roles);
    }

    [Fact]
    public async Task Table_roles_are_granted_when_the_JWT_carries_none()
    {
        // The original purpose of enrichment: roles live in the table, not in Entra.
        var repo = new FakeContentRepository { MemberByOid = MemberWith("Admin") };

        var result = await Make(principal: PrincipalWith(), repo: repo)
            .AuthenticateAsync(Request(("Authorization", $"Bearer {TokenWithKid()}")), AdminOnly, default);

        Assert.True(result.IsAllowed);
        Assert.Contains(result.Principal!.Claims, c => c.Type == "roles" && c.Value == "Admin");
    }

    [Fact]
    public async Task A_caller_with_no_member_record_holds_no_roles()
    {
        // Fail-closed: a valid CIAM token proves identity, not membership. Someone
        // holding an Entra app-role assignment but no member row gets nothing.
        var repo = new FakeContentRepository { MemberByOid = null };

        var result = await Make(principal: PrincipalWith("Admin"), repo: repo)
            .AuthenticateAsync(Request(("Authorization", $"Bearer {TokenWithKid()}")), AdminOnly, default);

        Assert.False(result.IsAllowed);
        Assert.Equal(HttpStatusCode.Forbidden, result.RefusalCode);
    }

    [Fact]
    public async Task A_caller_with_no_member_record_can_still_reach_the_relink_endpoints()
    {
        // The escape hatch that makes fail-closed survivable. /me and /whoami carry
        // [RequireRole] with no roles listed, so they stay reachable with none — and
        // GET /me is what re-links a stale OID by email and restores the roles.
        var repo = new FakeContentRepository { MemberByOid = null };

        var result = await Make(principal: PrincipalWith("Admin"), repo: repo)
            .AuthenticateAsync(Request(("Authorization", $"Bearer {TokenWithKid()}")), AnyAuthenticated, default);

        Assert.True(result.IsAllowed);
        Assert.Empty(result.Principal!.FindAll("roles"));
    }

    [Fact]
    public async Task A_storage_failure_falls_back_to_the_tokens_roles()
    {
        // Availability over strictness on the error path only: stripping roles when
        // the table is unreachable would turn a transient storage fault into a
        // site-wide lockout for every member at once.
        var repo = new FakeContentRepository
        {
            ThrowOnMemberLookup = new InvalidOperationException("table storage unreachable"),
        };

        var result = await Make(principal: PrincipalWith("Admin"), repo: repo)
            .AuthenticateAsync(Request(("Authorization", $"Bearer {TokenWithKid()}")), AdminOnly, default);

        Assert.True(result.IsAllowed);
    }

    [Fact]
    public async Task No_caller_holds_a_role_when_storage_is_unconfigured()
    {
        // Raised in review of #163: returning early here left the token's roles intact,
        // so a deployment missing its storage connection string would silently fall back
        // to Entra app-role assignments — the exact authority this change moves away from.
        var repo = new FakeContentRepository { Writes = false, MemberByOid = MemberWith("Admin") };

        var result = await Make(principal: PrincipalWith("Admin"), repo: repo)
            .AuthenticateAsync(Request(("Authorization", $"Bearer {TokenWithKid()}")), AdminOnly, default);

        Assert.False(result.IsAllowed);
        Assert.Equal(HttpStatusCode.Forbidden, result.RefusalCode);
    }

    [Fact]
    public async Task A_token_without_an_oid_claim_holds_no_roles()
    {
        // Nothing to look a member up by, so membership cannot be confirmed.
        var identity = new ClaimsIdentity("test", "name", "roles");
        identity.AddClaim(new Claim("name", "No Oid"));
        identity.AddClaim(new Claim("roles", "Admin"));
        var repo = new FakeContentRepository { MemberByOid = MemberWith("Admin") };

        var result = await Make(principal: new ClaimsPrincipal(identity), repo: repo)
            .AuthenticateAsync(Request(("Authorization", $"Bearer {TokenWithKid()}")), AdminOnly, default);

        Assert.False(result.IsAllowed);
        Assert.Equal(HttpStatusCode.Forbidden, result.RefusalCode);
    }
}
