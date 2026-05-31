using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Slypn.Api.Infrastructure;
using Slypn.Api.Models;
using Slypn.Api.Models.Inputs;
using Slypn.Api.Services;
using static Slypn.Api.Functions.FunctionHelpers;

namespace Slypn.Api.Functions;

public sealed class MembersFunctions(
    IContentRepository repo,
    IInviteService invites,
    ILogger<MembersFunctions> log)
{
    private static readonly HashSet<string> AllowedRoles = new(StringComparer.OrdinalIgnoreCase)
        { "Admin", "Contributor", "Member" };

    [Function("ListMembers")]
    [RequireRole("Admin")]
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
        catch (CosmosException ex) { return await MapCosmosException(req, ex, log); }
    }

    [Function("InviteMember")]
    [RequireRole("Admin")]
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
        catch (CosmosException ex) { return await MapCosmosException(req, ex, log); }

        var now = DateTime.UtcNow;
        var member = existing is null
            ? new Member(
                Id:          Guid.NewGuid().ToString("N"),
                Email:       email,
                DisplayName: input.DisplayName.Trim(),
                Roles:       input.Roles.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                Status:      "invited",
                InvitedAt:   now,
                AcceptedAt:  null,
                InvitedBy:   context.GetUserOid(),
                Oid:         null)
            : existing with
            {
                DisplayName = input.DisplayName.Trim(),
                Roles       = input.Roles.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                Status      = existing.AcceptedAt is null ? "invited" : "active",
                InvitedBy   = context.GetUserOid(),
            };

        Member saved;
        try { saved = await repo.UpsertMemberAsync(member, existing?.Etag, ct); }
        catch (CosmosException ex) { return await MapCosmosException(req, ex, log); }

        var inviteResult = await invites.SendInviteAsync(member.Email, member.DisplayName, ct);

        return await Created(req, new
        {
            member       = saved,
            inviteSent   = inviteResult.Sent,
            redeemUrl    = inviteResult.RedeemUrl,
            inviteReason = inviteResult.Reason,
        }, saved.Etag);
    }
}
