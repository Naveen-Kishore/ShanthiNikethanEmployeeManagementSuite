using Microsoft.EntityFrameworkCore;
using NSubstitute;
using ShanthiNikethan.EmployeeManagement.Core.Data;
using ShanthiNikethan.EmployeeManagement.Core.Services;
using ShanthiNikethan.EmployeeManagement.Modules.Admin.Data;
using ShanthiNikethan.EmployeeManagement.Modules.Admin.Services;
using ShanthiNikethan.EmployeeManagement.Modules.AutomationRules.Services;
using ShanthiNikethan.EmployeeManagement.Modules.StaffProfile.Services;
using Xunit;

namespace ShanthiNikethan.EmployeeManagement.Tests.Services;

public class OffboardingServiceTests
{
    private readonly IStaffProfileService _staffService;
    private readonly IUserAccountService _userAccountService;
    private readonly IGraphProvisioningService _graphService;
    private readonly IAuditService _audit;
    private readonly OffboardingService _sut;

    public OffboardingServiceTests()
    {
        // OffboardingService's IDbContextFactory field is injected but
        // never actually referenced anywhere in its own code - confirmed
        // by reading the real file, not assumed. This service is pure
        // orchestration over the other three, so everything here is a
        // plain mock, not a real database like UserAccountServiceTests
        // needed.
        var dbFactory = Substitute.For<IDbContextFactory<AppDbContext>>();
        _staffService = Substitute.For<IStaffProfileService>();
        _userAccountService = Substitute.For<IUserAccountService>();
        _graphService = Substitute.For<IGraphProvisioningService>();
        _audit = Substitute.For<IAuditService>();
        _sut = new OffboardingService(dbFactory, _staffService, _userAccountService, _graphService, _audit);
    }

    private static UserAccount MakeAccount(Guid? id = null, Guid? staffId = null, string? entraObjectId = null) => new()
    {
        Id = id ?? Guid.NewGuid(),
        DisplayName = "Test Person",
        StaffId = staffId,
        EntraObjectId = entraObjectId
    };

    // ====================================================================
    // OffboardByStaffIdAsync
    // ====================================================================

    [Fact]
    public async Task OffboardByStaffIdAsync_CascadesToLinkedAccount_AndSoftDeletesStaff()
    {
        var staffId = Guid.NewGuid();
        var account = MakeAccount(staffId: staffId, entraObjectId: "real-object-id");
        _userAccountService.ListUsersAsync(Arg.Any<CancellationToken>()).Returns(new List<UserAccount> { account });
        _graphService.DeleteUserAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(new GraphOperationResult { Success = true });

        var result = await _sut.OffboardByStaffIdAsync(staffId, "Resigned");

        Assert.True(result.Success);
        Assert.Empty(result.Warnings);
        await _graphService.Received(1).DeleteUserAsync("real-object-id", Arg.Any<CancellationToken>());
        await _userAccountService.Received(1).DeactivateUserAccountAsync(account.Id, Arg.Any<CancellationToken>());
        await _staffService.Received(1).SoftDeleteAsync(staffId, "Resigned", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OffboardByStaffIdAsync_StillSoftDeletesStaff_WhenNoLinkedAccountExists()
    {
        var staffId = Guid.NewGuid();
        _userAccountService.ListUsersAsync(Arg.Any<CancellationToken>()).Returns(new List<UserAccount>()); // nobody linked to this staff member

        var result = await _sut.OffboardByStaffIdAsync(staffId, "Resigned");

        Assert.True(result.Success);
        await _staffService.Received(1).SoftDeleteAsync(staffId, "Resigned", Arg.Any<CancellationToken>());
        // Cascade never attempted - there's genuinely nothing to cascade to.
        await _graphService.DidNotReceive().DeleteUserAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _userAccountService.DidNotReceive().DeactivateUserAccountAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OffboardByStaffIdAsync_DoesNotCallGraph_WhenLinkedAccountHasNoEntraIdentity()
    {
        var staffId = Guid.NewGuid();
        var account = MakeAccount(staffId: staffId, entraObjectId: null); // local-login-only account
        _userAccountService.ListUsersAsync(Arg.Any<CancellationToken>()).Returns(new List<UserAccount> { account });

        var result = await _sut.OffboardByStaffIdAsync(staffId, null);

        Assert.True(result.Success);
        Assert.Empty(result.Warnings); // no spurious warning for a correctly-absent Entra identity
        await _graphService.DidNotReceive().DeleteUserAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _userAccountService.Received(1).DeactivateUserAccountAsync(account.Id, Arg.Any<CancellationToken>());
    }

    // ====================================================================
    // OffboardByUserAccountIdAsync
    // ====================================================================

    [Fact]
    public async Task OffboardByUserAccountIdAsync_ReturnsFailure_WithoutThrowing_WhenAccountNotFound()
    {
        var accountId = Guid.NewGuid();
        _userAccountService.GetByIdAsync(accountId, Arg.Any<CancellationToken>()).Returns((UserAccount?)null);

        var result = await _sut.OffboardByUserAccountIdAsync(accountId, null);

        // Deliberately different from most of UserAccountService's own
        // "not found" methods, which throw - this one returns a failed
        // result instead, since offboarding a nonexistent account is a
        // normal outcome to report, not a system error.
        Assert.False(result.Success);
        Assert.Equal("Account not found.", result.ErrorMessage);
    }

    [Fact]
    public async Task OffboardByUserAccountIdAsync_SoftDeletesStaff_WhenAccountIsLinked()
    {
        var accountId = Guid.NewGuid();
        var staffId = Guid.NewGuid();
        var account = MakeAccount(id: accountId, staffId: staffId, entraObjectId: "real-object-id");
        _userAccountService.GetByIdAsync(accountId, Arg.Any<CancellationToken>()).Returns(account);
        _graphService.DeleteUserAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(new GraphOperationResult { Success = true });

        var result = await _sut.OffboardByUserAccountIdAsync(accountId, "Resigned");

        Assert.True(result.Success);
        await _staffService.Received(1).SoftDeleteAsync(staffId, "Resigned", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OffboardByUserAccountIdAsync_NeverTouchesStaff_ForAStandaloneAccount()
    {
        var accountId = Guid.NewGuid();
        var account = MakeAccount(id: accountId, staffId: null, entraObjectId: null); // e.g. a break-glass admin account
        _userAccountService.GetByIdAsync(accountId, Arg.Any<CancellationToken>()).Returns(account);

        var result = await _sut.OffboardByUserAccountIdAsync(accountId, "No longer needed");

        Assert.True(result.Success);
        await _staffService.DidNotReceive().SoftDeleteAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _userAccountService.Received(1).DeactivateUserAccountAsync(accountId, Arg.Any<CancellationToken>());
    }

    // ====================================================================
    // Failure/warning handling in the cascade - this is best-effort by
    // design, not all-or-nothing. Each of these confirms one failure
    // point doesn't block the rest of the offboarding from completing.
    // ====================================================================

    [Fact]
    public async Task Cascade_AddsWarning_ButStaysSuccessful_WhenGraphDeleteFails()
    {
        var accountId = Guid.NewGuid();
        var account = MakeAccount(id: accountId, entraObjectId: "real-object-id");
        _userAccountService.GetByIdAsync(accountId, Arg.Any<CancellationToken>()).Returns(account);
        _graphService.DeleteUserAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new GraphOperationResult { Success = false, ErrorMessage = "Entra is temporarily unreachable" });

        var result = await _sut.OffboardByUserAccountIdAsync(accountId, null);

        Assert.True(result.Success);
        Assert.Single(result.Warnings);
        Assert.Contains("Entra is temporarily unreachable", result.Warnings[0]);
        // The account still gets deactivated app-side, regardless of the Graph failure.
        await _userAccountService.Received(1).DeactivateUserAccountAsync(accountId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Cascade_AddsWarning_WhenDeactivateThrows_RatherThanCrashingTheWholeOffboard()
    {
        var accountId = Guid.NewGuid();
        var staffId = Guid.NewGuid();
        var account = MakeAccount(id: accountId, staffId: staffId);
        _userAccountService.GetByIdAsync(accountId, Arg.Any<CancellationToken>()).Returns(account);
        _userAccountService.When(x => x.DeactivateUserAccountAsync(accountId, Arg.Any<CancellationToken>()))
            .Do(x => throw new InvalidOperationException("Can't deactivate your own account."));

        var result = await _sut.OffboardByUserAccountIdAsync(accountId, "Resigned");

        Assert.True(result.Success); // caught, not propagated
        Assert.Single(result.Warnings);
        Assert.Contains("couldn't be deactivated", result.Warnings[0]);
        // Even with that failure, the Staff soft-delete still goes
        // through - best-effort, not all-or-nothing.
        await _staffService.Received(1).SoftDeleteAsync(staffId, "Resigned", Arg.Any<CancellationToken>());
    }

    // ====================================================================
    // Audit content
    // ====================================================================

    [Fact]
    public async Task Cascade_LogsAnOffboardEntry_ForTheLinkedAccount()
    {
        var accountId = Guid.NewGuid();
        var account = MakeAccount(id: accountId, entraObjectId: "real-object-id");
        _userAccountService.GetByIdAsync(accountId, Arg.Any<CancellationToken>()).Returns(account);
        _graphService.DeleteUserAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(new GraphOperationResult { Success = true });

        await _sut.OffboardByUserAccountIdAsync(accountId, null);

        await _audit.Received(1).LogAsync(
            "Admin", "UserAccount", accountId.ToString(), "Offboard",
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Is<string>(c => c != null && c.Contains("Cascaded")),
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<bool>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    // ====================================================================
    // Three remaining genuine gaps, not just padding: both cascade
    // failures happening in the SAME call (not yet tested together),
    // correct filtering when multiple accounts exist (previous tests
    // only ever had a list of one, which could pass even with a broken
    // filter), and explicit confirmation that a null reason survives
    // the passthrough rather than being silently swapped for something else.
    // ====================================================================

    [Fact]
    public async Task Cascade_AccumulatesBothWarnings_WhenGraphAndDeactivateBothFail()
    {
        var accountId = Guid.NewGuid();
        var staffId = Guid.NewGuid();
        var account = MakeAccount(id: accountId, staffId: staffId, entraObjectId: "real-object-id");
        _userAccountService.GetByIdAsync(accountId, Arg.Any<CancellationToken>()).Returns(account);
        _graphService.DeleteUserAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new GraphOperationResult { Success = false, ErrorMessage = "Entra is temporarily unreachable" });
        _userAccountService.When(x => x.DeactivateUserAccountAsync(accountId, Arg.Any<CancellationToken>()))
            .Do(x => throw new InvalidOperationException("Can't deactivate your own account."));

        var result = await _sut.OffboardByUserAccountIdAsync(accountId, "Resigned");

        // Both failures independently reported, not one silently
        // swallowing the other - and neither one is fatal on its own,
        // so the Staff soft-delete still goes through regardless.
        Assert.True(result.Success);
        Assert.Equal(2, result.Warnings.Count);
        Assert.Contains(result.Warnings, w => w.Contains("Entra is temporarily unreachable"));
        Assert.Contains(result.Warnings, w => w.Contains("couldn't be deactivated"));
        await _staffService.Received(1).SoftDeleteAsync(staffId, "Resigned", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OffboardByStaffIdAsync_FindsTheCorrectAccount_AmongSeveralOthers()
    {
        // Every earlier "linked account" test used a list of exactly one
        // account - which could pass even if the StaffId filter itself
        // were broken (e.g. always returning the first item regardless
        // of match). This confirms the filter genuinely discriminates.
        var targetStaffId = Guid.NewGuid();
        var targetAccount = MakeAccount(staffId: targetStaffId, entraObjectId: "target-object-id");
        var otherAccount1 = MakeAccount(staffId: Guid.NewGuid(), entraObjectId: "other-1");
        var otherAccount2 = MakeAccount(staffId: Guid.NewGuid(), entraObjectId: "other-2");
        _userAccountService.ListUsersAsync(Arg.Any<CancellationToken>())
            .Returns(new List<UserAccount> { otherAccount1, targetAccount, otherAccount2 });
        _graphService.DeleteUserAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(new GraphOperationResult { Success = true });

        await _sut.OffboardByStaffIdAsync(targetStaffId, null);

        // Only the genuinely matching account's Entra identity was
        // touched - not either of the other two.
        await _graphService.Received(1).DeleteUserAsync("target-object-id", Arg.Any<CancellationToken>());
        await _graphService.DidNotReceive().DeleteUserAsync("other-1", Arg.Any<CancellationToken>());
        await _graphService.DidNotReceive().DeleteUserAsync("other-2", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OffboardByStaffIdAsync_PassesANullReason_ThroughUnchanged()
    {
        var staffId = Guid.NewGuid();
        _userAccountService.ListUsersAsync(Arg.Any<CancellationToken>()).Returns(new List<UserAccount>());

        await _sut.OffboardByStaffIdAsync(staffId, null);

        await _staffService.Received(1).SoftDeleteAsync(staffId, null, Arg.Any<CancellationToken>());
    }
}
