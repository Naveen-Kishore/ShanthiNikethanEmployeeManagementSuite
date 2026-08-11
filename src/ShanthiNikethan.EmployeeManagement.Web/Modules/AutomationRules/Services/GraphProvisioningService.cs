using System.Security.Cryptography;
using Azure.Identity;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.Graph.Models.ODataErrors;

namespace ShanthiNikethan.EmployeeManagement.Modules.AutomationRules.Services;

public class GraphOperationResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
}

public class CreateUserResult : GraphOperationResult
{
    public string? ObjectId { get; set; }
    public string? TemporaryPassword { get; set; }
}

/// <summary>
/// The one place in the app that talks to Microsoft Graph for actual
/// tenant user/group management - app-only auth, not tied to any signed-in
/// person. Every method returns a result object rather than throwing on
/// the expected failure paths - a missing ClientSecret, a bad Object ID, a
/// group that doesn't exist, an expired 30-day recovery window - callers
/// (and ultimately whatever page is showing this to a person) get a clean
/// message to display, not a raw exception tearing down the whole page.
///
/// The underlying GraphServiceClient is built lazily, inside each method
/// call, rather than once eagerly via DI - the first version of this
/// registered it as a DI singleton constructed at first resolution, which
/// meant a misconfigured secret crashed the entire Blazor circuit the
/// moment anyone merely navigated to a page that injected this service,
/// rather than showing a clean error specifically where it mattered. This
/// version can never do that - the worst case now is one operation
/// reporting Success = false with a readable reason.
/// </summary>
public interface IGraphProvisioningService
{
    Task<CreateUserResult> CreateUserAsync(string displayName, string userPrincipalName, CancellationToken ct = default);
    Task<GraphOperationResult> DisableUserAsync(string objectId, CancellationToken ct = default);

    /// <summary>Reverses DisableUserAsync - for a temporary suspension being lifted (extended leave ending, etc.), distinct from the delete/restore cycle used for actual offboarding.</summary>
    Task<GraphOperationResult> EnableUserAsync(string objectId, CancellationToken ct = default);

    /// <summary>A real delete, not a disable - this is what triggers Entra's own native 30-day soft-delete/recovery window.</summary>
    Task<GraphOperationResult> DeleteUserAsync(string objectId, CancellationToken ct = default);

    /// <summary>Only works within Entra's 30-day window after DeleteUserAsync. Restores the account with the same Object ID and UPN - does NOT restore group memberships, those must be reapplied separately.</summary>
    Task<GraphOperationResult> RestoreUserAsync(string objectId, CancellationToken ct = default);

    Task<GraphOperationResult> AddToGroupAsync(string userObjectId, string groupObjectId, CancellationToken ct = default);
    Task<GraphOperationResult> RemoveFromGroupAsync(string userObjectId, string groupObjectId, CancellationToken ct = default);

    /// <summary>Resolves a group Object ID to its real display name - lets Automation Rules confirm a pasted GUID is genuinely a real, reachable group before Global Admin saves the rule.</summary>
    Task<(bool Found, string? DisplayName)> VerifyGroupAsync(string groupObjectId, CancellationToken ct = default);

    /// <summary>Same idea, for a user instead of a group - confirms a user Object ID still resolves to a real Entra account before something like RevertToEntraAsync trusts an archived value.</summary>
    Task<(bool Found, string? UserPrincipalName)> VerifyUserAsync(string userObjectId, CancellationToken ct = default);

    /// <summary>
    /// Checks Entra's own deleted-items list for a UPN still reserved by a
    /// recently soft-deleted user (within the 30-day recovery window) -
    /// this is what catches a stale reservation that has no corresponding
    /// record in this app's own database at all (e.g. one created and
    /// deleted via Graph Diagnostics testing, never linked to a real
    /// UserAccount). Complements, doesn't replace, the app-side check
    /// against UserAccount.
    /// </summary>
    Task<bool> IsUpnRecentlyDeletedAsync(string upn, CancellationToken ct = default);
}

public class GraphProvisioningService : IGraphProvisioningService
{
    private readonly IConfiguration _config;
    private readonly ILogger<GraphProvisioningService> _logger;

    public GraphProvisioningService(IConfiguration config, ILogger<GraphProvisioningService> logger)
    {
        _config = config;
        _logger = logger;
    }

    /// <summary>
    /// Builds a fresh GraphServiceClient from current configuration, or
    /// returns a clear reason why it couldn't. Called at the start of every
    /// public method below - nothing here ever throws past this point for
    /// a configuration problem specifically (genuine Graph API failures
    /// during the actual call are still caught separately by each method).
    /// </summary>
    private bool TryBuildClient(out GraphServiceClient? client, out string? error)
    {
        client = null;
        var tenantId = _config["AzureAd:TenantId"];
        var clientId = _config["AzureAd:ClientId"];
        var clientSecret = _config["AzureAd:ClientSecret"];

        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(tenantId)) missing.Add("AzureAd:TenantId");
        if (string.IsNullOrWhiteSpace(clientId)) missing.Add("AzureAd:ClientId");
        if (string.IsNullOrWhiteSpace(clientSecret)) missing.Add("AzureAd:ClientSecret");

        if (missing.Count > 0)
        {
            error = $"Graph client isn't configured - missing: {string.Join(", ", missing)}. " +
                    "AzureAd:ClientSecret specifically usually isn't in appsettings.json by design - " +
                    "set it via User Secrets locally (dotnet user-secrets set \"AzureAd:ClientSecret\" \"...\") " +
                    "or Key Vault/environment variables in a deployed environment.";
            return false;
        }

        try
        {
            var credential = new ClientSecretCredential(tenantId, clientId, clientSecret);
            client = new GraphServiceClient(credential, new[] { "https://graph.microsoft.com/.default" });
            error = null;
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to construct GraphServiceClient");
            error = $"Couldn't construct the Graph client: {ex.Message}";
            return false;
        }
    }

    public async Task<CreateUserResult> CreateUserAsync(string displayName, string userPrincipalName, CancellationToken ct = default)
    {
        if (!TryBuildClient(out var graph, out var configError))
            return new CreateUserResult { Success = false, ErrorMessage = configError };

        try
        {
            var tempPassword = GenerateTemporaryPassword();
            var mailNickname = userPrincipalName.Split('@')[0];

            var user = new User
            {
                AccountEnabled = true,
                DisplayName = displayName,
                MailNickname = mailNickname,
                UserPrincipalName = userPrincipalName,
                PasswordProfile = new PasswordProfile
                {
                    ForceChangePasswordNextSignIn = true,
                    Password = tempPassword
                }
            };

            var created = await graph!.Users.PostAsync(user, cancellationToken: ct);

            return new CreateUserResult
            {
                Success = true,
                ObjectId = created?.Id,
                TemporaryPassword = tempPassword
            };
        }
        catch (ODataError ex)
        {
            _logger.LogWarning(ex, "Graph CreateUser failed for {Upn}", userPrincipalName);
            return new CreateUserResult { Success = false, ErrorMessage = FriendlyMessage(ex) };
        }
    }

    public async Task<GraphOperationResult> DisableUserAsync(string objectId, CancellationToken ct = default)
    {
        if (!TryBuildClient(out var graph, out var configError))
            return new GraphOperationResult { Success = false, ErrorMessage = configError };

        try
        {
            await graph!.Users[objectId].PatchAsync(new User { AccountEnabled = false }, cancellationToken: ct);
            return new GraphOperationResult { Success = true };
        }
        catch (ODataError ex)
        {
            _logger.LogWarning(ex, "Graph DisableUser failed for {ObjectId}", objectId);
            return new GraphOperationResult { Success = false, ErrorMessage = FriendlyMessage(ex) };
        }
    }

    public async Task<GraphOperationResult> EnableUserAsync(string objectId, CancellationToken ct = default)
    {
        if (!TryBuildClient(out var graph, out var configError))
            return new GraphOperationResult { Success = false, ErrorMessage = configError };

        try
        {
            await graph!.Users[objectId].PatchAsync(new User { AccountEnabled = true }, cancellationToken: ct);
            return new GraphOperationResult { Success = true };
        }
        catch (ODataError ex)
        {
            _logger.LogWarning(ex, "Graph EnableUser failed for {ObjectId}", objectId);
            return new GraphOperationResult { Success = false, ErrorMessage = FriendlyMessage(ex) };
        }
    }

    public async Task<GraphOperationResult> DeleteUserAsync(string objectId, CancellationToken ct = default)
    {
        if (!TryBuildClient(out var graph, out var configError))
            return new GraphOperationResult { Success = false, ErrorMessage = configError };

        try
        {
            await graph!.Users[objectId].DeleteAsync(cancellationToken: ct);
            return new GraphOperationResult { Success = true };
        }
        catch (ODataError ex)
        {
            _logger.LogWarning(ex, "Graph DeleteUser failed for {ObjectId}", objectId);
            return new GraphOperationResult { Success = false, ErrorMessage = FriendlyMessage(ex) };
        }
    }

    public async Task<GraphOperationResult> RestoreUserAsync(string objectId, CancellationToken ct = default)
    {
        if (!TryBuildClient(out var graph, out var configError))
            return new GraphOperationResult { Success = false, ErrorMessage = configError };

        try
        {
            await graph!.Directory.DeletedItems[objectId].Restore.PostAsync(cancellationToken: ct);
            return new GraphOperationResult { Success = true };
        }
        catch (ODataError ex)
        {
            _logger.LogWarning(ex, "Graph RestoreUser failed for {ObjectId} - likely past the 30-day window or already restored", objectId);
            return new GraphOperationResult { Success = false, ErrorMessage = FriendlyMessage(ex) };
        }
    }

    public async Task<GraphOperationResult> AddToGroupAsync(string userObjectId, string groupObjectId, CancellationToken ct = default)
    {
        if (!TryBuildClient(out var graph, out var configError))
            return new GraphOperationResult { Success = false, ErrorMessage = configError };

        try
        {
            await graph!.Groups[groupObjectId].Members.Ref.PostAsync(new ReferenceCreate
            {
                OdataId = $"https://graph.microsoft.com/v1.0/directoryObjects/{userObjectId}"
            }, cancellationToken: ct);
            return new GraphOperationResult { Success = true };
        }
        catch (ODataError ex)
        {
            _logger.LogWarning(ex, "Graph AddToGroup failed - user {UserId}, group {GroupId}", userObjectId, groupObjectId);
            return new GraphOperationResult { Success = false, ErrorMessage = FriendlyMessage(ex) };
        }
    }

    public async Task<GraphOperationResult> RemoveFromGroupAsync(string userObjectId, string groupObjectId, CancellationToken ct = default)
    {
        if (!TryBuildClient(out var graph, out var configError))
            return new GraphOperationResult { Success = false, ErrorMessage = configError };

        try
        {
            await graph!.Groups[groupObjectId].Members[userObjectId].Ref.DeleteAsync(cancellationToken: ct);
            return new GraphOperationResult { Success = true };
        }
        catch (ODataError ex)
        {
            _logger.LogWarning(ex, "Graph RemoveFromGroup failed - user {UserId}, group {GroupId}", userObjectId, groupObjectId);
            return new GraphOperationResult { Success = false, ErrorMessage = FriendlyMessage(ex) };
        }
    }

    public async Task<(bool Found, string? DisplayName)> VerifyGroupAsync(string groupObjectId, CancellationToken ct = default)
    {
        if (!TryBuildClient(out var graph, out _))
            return (false, null);

        try
        {
            var group = await graph!.Groups[groupObjectId].GetAsync(cancellationToken: ct);
            return (true, group?.DisplayName);
        }
        catch (ODataError)
        {
            return (false, null);
        }
    }

    public async Task<(bool Found, string? UserPrincipalName)> VerifyUserAsync(string userObjectId, CancellationToken ct = default)
    {
        if (!TryBuildClient(out var graph, out _))
            return (false, null);

        try
        {
            var user = await graph!.Users[userObjectId].GetAsync(cancellationToken: ct);
            return (true, user?.UserPrincipalName);
        }
        catch (ODataError)
        {
            return (false, null);
        }
    }

    public async Task<bool> IsUpnRecentlyDeletedAsync(string upn, CancellationToken ct = default)
    {
        // Fails safe: if the client can't be built, or the query itself
        // fails for any reason, this returns false rather than blocking
        // onboarding on a check that couldn't run. The caller still gets
        // Graph's own rejection as a fallback if a genuine conflict exists -
        // this method only ever makes the failure message clearer, it's
        // never the sole gate standing between a bad UPN and creation.
        if (!TryBuildClient(out var graph, out _))
            return false;

        try
        {
            // OData single-quotes inside the filter value need doubling to
            // escape correctly - a UPN containing one verbatim would
            // otherwise break the filter syntax rather than just fail to
            // match.
            var escapedUpn = upn.Replace("'", "''");
            var result = await graph!.Directory.DeletedItems.GraphUser.GetAsync(config =>
            {
                config.QueryParameters.Filter = $"userPrincipalName eq '{escapedUpn}'";
            }, cancellationToken: ct);

            return result?.Value != null && result.Value.Count > 0;
        }
        catch (ODataError ex)
        {
            _logger.LogWarning(ex, "Graph deleted-items UPN check failed for {Upn} - treating as unknown rather than blocking onboarding on it", upn);
            return false;
        }
    }

    // Meets Entra's default complexity requirement (3 of 4: upper, lower,
    // digit, symbol) - forced change on first sign-in means this value
    // only ever needs to work once.
    private static string GenerateTemporaryPassword()
    {
        const string upper = "ABCDEFGHJKLMNPQRSTUVWXYZ"; // no I/O - avoids visual ambiguity with 1/0
        const string lower = "abcdefghijkmnpqrstuvwxyz";
        const string digits = "23456789";
        const string symbols = "!@#$%&*";
        var all = upper + lower + digits + symbols;

        Span<char> result = stackalloc char[12];
        result[0] = upper[RandomNumberGenerator.GetInt32(upper.Length)];
        result[1] = lower[RandomNumberGenerator.GetInt32(lower.Length)];
        result[2] = digits[RandomNumberGenerator.GetInt32(digits.Length)];
        result[3] = symbols[RandomNumberGenerator.GetInt32(symbols.Length)];
        for (int i = 4; i < result.Length; i++)
            result[i] = all[RandomNumberGenerator.GetInt32(all.Length)];

        for (int i = result.Length - 1; i > 0; i--)
        {
            int j = RandomNumberGenerator.GetInt32(i + 1);
            (result[i], result[j]) = (result[j], result[i]);
        }
        return new string(result);
    }

    private static string FriendlyMessage(ODataError ex) =>
        ex.Error?.Message ?? ex.Message;
}
