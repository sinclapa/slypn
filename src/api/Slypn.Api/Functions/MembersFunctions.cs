using System.Net;
using Azure;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Attributes;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Enums;
using Microsoft.OpenApi.Models;
using Slypn.Api.Infrastructure;
using Slypn.Api.Models;
using Slypn.Api.Models.Inputs;
using Slypn.Api.Services;
using static Slypn.Api.Functions.FunctionHelpers;

namespace Slypn.Api.Functions;

public sealed class MembersFunctions(
    IContentRepository repo,
    IInviteService invites,
    IEntraUserService entra,
    ILogger<MembersFunctions> log)
{
    private static readonly HashSet<string> AllowedRoles = new(StringComparer.OrdinalIgnoreCase)
        { "Admin", "Contributor", "Member" };

    [Function("ListMembers")]
    [RequireRole("Admin")]
    [OpenApiOperation(operationId: "members.list", tags: new[] { "members" }, Summary = "List members", Description = "Returns all members.")]
    [OpenApiSecurity("bearer_auth", SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = "JWT")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(Member[]), Description = "List of members")]
    public async Task<HttpResponseData> List(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "members")] HttpRequestData req,
        CancellationToken ct)
    {
        if (!repo.SupportsWrites) return await WritesDisabled(req);
        try
        {
            var members = await repo.ListMembersAsync(ct);
            return await Ok(req, members);
        }
        catch (RequestFailedException ex) { return await MapStorageException(req, ex, log); }
    }

    [Function("InviteMember")]
    [RequireRole("Admin")]
    [OpenApiOperation(operationId: "members.invite", tags: new[] { "members" }, Summary = "Invite member", Description = "Creates or updates a member invitation.")]
    [OpenApiSecurity("bearer_auth", SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = "JWT")]
    [OpenApiRequestBody(contentType: "application/json", bodyType: typeof(MemberInviteInput), Required = true, Description = "Invitation payload.")]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.Created, contentType: "application/json", bodyType: typeof(object), Description = "Invitation result")]
    public async Task<HttpResponseData> Invite(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "members/invite")] HttpRequestData req,
        FunctionContext context,
        CancellationToken ct)
    {
        if (!repo.SupportsWrites) return await WritesDisabled(req);

        var (input, err) = await ReadValidatedAsync<MemberInviteInput>(req, ct);
        if (err is not null) return err;

        var badRoles = input!.Roles.Where(r => !AllowedRoles.Contains(r)).ToArray();
        if (badRoles.Length > 0)
            return await BadRequest(req, $"Unknown role(s): {string.Join(", ", badRoles)}. Allowed: Admin, Contributor, Member.");

        var email = input.Email.Trim().ToLowerInvariant();

        Member? existing;
        try { existing = await repo.GetMemberByEmailAsync(email, ct); }
        catch (RequestFailedException ex) { return await MapStorageException(req, ex, log); }

        var now = DateTime.UtcNow;
        Member member;
        if (existing is null)
        {
            member = new Member(
                Id:          Guid.NewGuid().ToString("N"),
                Email:       email,
                DisplayName: input.DisplayName.Trim(),
                Roles:       input.Roles.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                Status:      "invited",
                InvitedAt:   now,
                AcceptedAt:  null,
                InvitedBy:   context.GetUserOid(),
                Oid:         null);
        }
        else
        {
            var status = existing.AcceptedAt is null ? "invited" : "active";
            member = existing with
            {
                DisplayName = input.DisplayName.Trim(),
                Roles       = input.Roles.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                Status      = status,
                InvitedBy   = context.GetUserOid(),
            };
        }

        Member saved;
        try { saved = await repo.UpsertMemberAsync(member, existing?.Etag, ct); }
        catch (RequestFailedException ex) { return await MapStorageException(req, ex, log); }

        var inviteResult = await invites.SendInviteAsync(member.Email, member.DisplayName, ct);

        return await Created(req, new
        {
            member       = saved,
            inviteSent   = inviteResult.Sent,
            redeemUrl    = inviteResult.RedeemUrl,
            inviteReason = inviteResult.Reason,
        }, saved.Etag);
    }

    [Function("UpdateMemberRoles")]
    [RequireRole("Admin")]
    [OpenApiOperation(operationId: "members.updateRoles", tags: new[] { "members" }, Summary = "Update member roles", Description = "Replaces the roles on an existing member.")]
    [OpenApiSecurity("bearer_auth", SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = "JWT")]
    [OpenApiParameter(name: "id", In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = "Member id.")]
    [OpenApiRequestBody(contentType: "application/json", bodyType: typeof(MemberRolesInput), Required = true)]
    [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(Member), Description = "Updated member")]
    public async Task<HttpResponseData> UpdateRoles(
        [HttpTrigger(AuthorizationLevel.Anonymous, "patch", Route = "members/{id}")] HttpRequestData req,
        string id, FunctionContext context, CancellationToken ct)
    {
        if (!repo.SupportsWrites) return await WritesDisabled(req);

        var (input, err) = await ReadValidatedAsync<MemberRolesInput>(req, ct);
        if (err is not null) return err;

        var badRoles = input!.Roles.Where(r => !AllowedRoles.Contains(r)).ToArray();
        if (badRoles.Length > 0)
            return await BadRequest(req, $"Unknown role(s): {string.Join(", ", badRoles)}. Allowed: Admin, Contributor, Member.");

        Member? existing;
        try { existing = await repo.GetMemberByIdAsync(id, ct); }
        catch (RequestFailedException ex) { return await MapStorageException(req, ex, log); }
        if (existing is null) return await NotFound(req, "Member not found.");

        if (existing.Oid is not null && existing.Oid == context.GetUserOid())
            return await BadRequest(req, "You cannot change your own role.");

        var updated = existing with { Roles = input.Roles.Distinct(StringComparer.OrdinalIgnoreCase).ToList() };
        try
        {
            var saved = await repo.UpsertMemberAsync(updated, IfMatch(req), ct);
            return await Ok(req, saved, saved.Etag);
        }
        catch (RequestFailedException ex) { return await MapStorageException(req, ex, log); }
    }

    [Function("DeleteMember")]
    [RequireRole("Admin")]
    [OpenApiOperation(operationId: "members.delete", tags: new[] { "members" }, Summary = "Delete member", Description = "Removes a member record.")]
    [OpenApiSecurity("bearer_auth", SecuritySchemeType.Http, Scheme = OpenApiSecuritySchemeType.Bearer, BearerFormat = "JWT")]
    [OpenApiParameter(name: "id", In = ParameterLocation.Path, Required = true, Type = typeof(string), Description = "Member id.")]
    [OpenApiResponseWithoutBody(statusCode: HttpStatusCode.NoContent, Description = "Deleted")]
    public async Task<HttpResponseData> Delete(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "members/{id}")] HttpRequestData req,
        string id, FunctionContext context, CancellationToken ct)
    {
        if (!repo.SupportsWrites) return await WritesDisabled(req);

        Member? existing;
        try { existing = await repo.GetMemberByIdAsync(id, ct); }
        catch (RequestFailedException ex) { return await MapStorageException(req, ex, log); }
        if (existing is null) return await NotFound(req, "Member not found.");

        if (existing.Oid is not null && existing.Oid == context.GetUserOid())
            return await BadRequest(req, "You cannot remove yourself.");

        try
        {
            await repo.DeleteMemberAsync(id, IfMatch(req), ct);
        }
        catch (RequestFailedException ex) { return await MapStorageException(req, ex, log); }

        // Best-effort: remove the Entra account so the user can no longer sign in.
        // Only possible once the member completed sign-up and has an OID.
        if (!string.IsNullOrEmpty(existing.Oid))
            await entra.DeleteUserAsync(existing.Oid, ct);

        return NoContent(req);
    }
}
