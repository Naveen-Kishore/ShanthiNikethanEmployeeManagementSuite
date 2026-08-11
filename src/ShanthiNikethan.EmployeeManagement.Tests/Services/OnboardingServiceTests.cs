using NSubstitute;
using ShanthiNikethan.EmployeeManagement.Core.Services;
using ShanthiNikethan.EmployeeManagement.Modules.Admin.Data;
using ShanthiNikethan.EmployeeManagement.Modules.Admin.Services;
using ShanthiNikethan.EmployeeManagement.Modules.AutomationRules.Data;
using ShanthiNikethan.EmployeeManagement.Modules.AutomationRules.Services;
using Xunit;

namespace ShanthiNikethan.EmployeeManagement.Tests.Services;

public class OnboardingServiceTests
{
    private readonly IUserAccountService _userAccountService;
    private readonly IGraphProvisioningService _graphService;
    private readonly IGroupAutomationService _automationService;
    private readonly IDashboardNotificationService _notificationService;
    private readonly OnboardingService _sut;

    public OnboardingServiceTests()
    {
        _userAccountService = Substitute.For<IUserAccountService>();
        _graphService = Substitute.For<IGraphProvisioningService>();
        _automationService = Substitute.For<IGroupAutomationService>();
        _notificationService = Substitute.For<IDashboardNotificationService>();
        _sut = new OnboardingService(_userAccountService, _graphService, _automationService, _notificationService);

        // Default happy-path stubs, overridden per-test as needed - keeps
        // each test focused on the one thing it's actually checking,
        // rather than re-stubbing the whole chain every time.
        _userAccountService.FindAnyByEntraUpnAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((UserAccount?)null);
        _graphService.CreateUserAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new CreateUserResult { Success = true, ObjectId = "new-object-id", TemporaryPassword = "Temp123!" });
        _automationService.ListRulesAsync(Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(new List<GroupAutomationRule>());
        _userAccountService.ListRoleGroupsAsync(Arg.Any<CancellationToken>())
            .Returns(new List<RoleGroup> { new() { Id = Guid.NewGuid(), Name = "Regular Staff" } });
    }

    // ====================================================================
    // UPN collision - checked BEFORE Graph is ever called
    // ====================================================================

    [Fact]
    public async Task EnableSignInAsync_Fails_WithoutCallingGraph_WhenUpnAlreadyBelongsToAnActiveAccount()
    {
        var existing = new UserAccount { Id = Guid.NewGuid(), DisplayName = "Existing Person", IsActive = true };
        _userAccountService.FindAnyByEntraUpnAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(existing);

        var result = await _sut.EnableSignInAsync(Guid.NewGuid(), "New Person", "taken@school.onmicrosoft.com", new List<Guid>());

        Assert.False(result.Success);
        Assert.Contains("Existing Person", result.ErrorMessage);
        Assert.DoesNotContain("currently deactivated", result.ErrorMessage);
        await _graphService.DidNotReceive().CreateUserAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EnableSignInAsync_MessageMentionsDeactivated_WhenTheExistingAccountIsInactive()
    {
        var existing = new UserAccount { Id = Guid.NewGuid(), DisplayName = "Existing Person", IsActive = false };
        _userAccountService.FindAnyByEntraUpnAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(existing);

        var result = await _sut.EnableSignInAsync(Guid.NewGuid(), "New Person", "taken@school.onmicrosoft.com", new List<Guid>());

        Assert.False(result.Success);
        Assert.Contains("currently deactivated", result.ErrorMessage);
    }

    // ====================================================================
    // Graph account creation failure
    // ====================================================================

    [Fact]
    public async Task EnableSignInAsync_Fails_AndStopsEntirely_WhenGraphCreateFails()
    {
        _graphService.CreateUserAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new CreateUserResult { Success = false, ErrorMessage = "Entra tenant is unreachable" });

        var result = await _sut.EnableSignInAsync(Guid.NewGuid(), "New Person", "new.person@school.onmicrosoft.com", new List<Guid>());

        Assert.False(result.Success);
        Assert.Equal("Entra tenant is unreachable", result.ErrorMessage);
        // Nothing downstream should have been touched at all.
        await _userAccountService.DidNotReceive().ListRoleGroupsAsync(Arg.Any<CancellationToken>());
        await _userAccountService.DidNotReceive().CreateUserAccountAsync(Arg.Any<UserAccount>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _notificationService.DidNotReceive().CreateAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>());
    }

    // ====================================================================
    // Automation rules - three distinct outcomes per rule, and all three
    // coexisting correctly in a single call.
    // ====================================================================

    [Fact]
    public async Task EnableSignInAsync_SilentlySkips_ARequestedRuleThatNoLongerExists()
    {
        // No warning, no crash - just skipped, e.g. the rule was disabled
        // or deleted after the UI loaded the checklist but before Save
        // was clicked.
        var missingRuleId = Guid.NewGuid();
        _automationService.ListRulesAsync(Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(new List<GroupAutomationRule>()); // empty - nothing matches

        var result = await _sut.EnableSignInAsync(Guid.NewGuid(), "New Person", "new.person@school.onmicrosoft.com", new List<Guid> { missingRuleId });

        Assert.True(result.Success);
        Assert.Empty(result.Warnings);
        await _graphService.DidNotReceive().AddToGroupAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EnableSignInAsync_AppliesAMatchingRule_AndRecordsTheAssignment()
    {
        var rule = new GroupAutomationRule { Id = Guid.NewGuid(), RuleName = "Teaching Staff Group", EntraGroupObjectId = "group-object-id", IsEnabled = true };
        _automationService.ListRulesAsync(Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(new List<GroupAutomationRule> { rule });
        _graphService.AddToGroupAsync("new-object-id", "group-object-id", Arg.Any<CancellationToken>()).Returns(new GraphOperationResult { Success = true });
        var staffId = Guid.NewGuid();

        var result = await _sut.EnableSignInAsync(staffId, "New Person", "new.person@school.onmicrosoft.com", new List<Guid> { rule.Id });

        Assert.True(result.Success);
        Assert.Empty(result.Warnings);
        await _automationService.Received(1).RecordAssignmentAsync(staffId, rule.Id, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EnableSignInAsync_WarnsButContinues_WhenARuleFailsToApply()
    {
        var rule = new GroupAutomationRule { Id = Guid.NewGuid(), RuleName = "Teaching Staff Group", EntraGroupObjectId = "group-object-id", IsEnabled = true };
        _automationService.ListRulesAsync(Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(new List<GroupAutomationRule> { rule });
        _graphService.AddToGroupAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new GraphOperationResult { Success = false, ErrorMessage = "Group not found" });

        var result = await _sut.EnableSignInAsync(Guid.NewGuid(), "New Person", "new.person@school.onmicrosoft.com", new List<Guid> { rule.Id });

        // Non-fatal - the overall provisioning still completes.
        Assert.True(result.Success);
        Assert.Single(result.Warnings);
        Assert.Contains("Teaching Staff Group", result.Warnings[0]);
        await _automationService.DidNotReceive().RecordAssignmentAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        // The account still gets created despite the rule failure.
        await _userAccountService.Received(1).CreateUserAccountAsync(Arg.Any<UserAccount>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EnableSignInAsync_HandlesAllThreeRuleOutcomes_TogetherInOneCall()
    {
        var appliedRule = new GroupAutomationRule { Id = Guid.NewGuid(), RuleName = "Applied Rule", EntraGroupObjectId = "applied-group", IsEnabled = true };
        var failedRule = new GroupAutomationRule { Id = Guid.NewGuid(), RuleName = "Failed Rule", EntraGroupObjectId = "failed-group", IsEnabled = true };
        var missingRuleId = Guid.NewGuid(); // never in the available list at all

        _automationService.ListRulesAsync(Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(new List<GroupAutomationRule> { appliedRule, failedRule });
        _graphService.AddToGroupAsync("new-object-id", "applied-group", Arg.Any<CancellationToken>()).Returns(new GraphOperationResult { Success = true });
        _graphService.AddToGroupAsync("new-object-id", "failed-group", Arg.Any<CancellationToken>()).Returns(new GraphOperationResult { Success = false, ErrorMessage = "Denied" });

        var result = await _sut.EnableSignInAsync(Guid.NewGuid(), "New Person", "new.person@school.onmicrosoft.com",
            new List<Guid> { appliedRule.Id, failedRule.Id, missingRuleId });

        Assert.True(result.Success);
        Assert.Single(result.Warnings); // only the genuinely failed one
        Assert.Contains("Failed Rule", result.Warnings[0]);
        await _automationService.Received(1).RecordAssignmentAsync(Arg.Any<Guid>(), appliedRule.Id, Arg.Any<CancellationToken>());
        await _automationService.DidNotReceive().RecordAssignmentAsync(Arg.Any<Guid>(), failedRule.Id, Arg.Any<CancellationToken>());
    }

    // ====================================================================
    // Missing "Regular Staff" role group - a genuinely counterintuitive
    // outcome worth pinning down explicitly: Success stays true here,
    // even though the account never actually gets linked.
    // ====================================================================

    [Fact]
    public async Task EnableSignInAsync_ReportsSuccessWithAWarning_WhenRegularStaffGroupIsMissing()
    {
        _userAccountService.ListRoleGroupsAsync(Arg.Any<CancellationToken>()).Returns(new List<RoleGroup>()); // no "Regular Staff" at all

        var result = await _sut.EnableSignInAsync(Guid.NewGuid(), "New Person", "new.person@school.onmicrosoft.com", new List<Guid>());

        // Documented as current, actual behavior, not necessarily
        // endorsed as ideal - Success is true despite the account never
        // being linked, because the Entra side did genuinely succeed.
        Assert.True(result.Success);
        Assert.Single(result.Warnings);
        Assert.Contains("Regular Staff", result.Warnings[0]);
        await _userAccountService.DidNotReceive().CreateUserAccountAsync(Arg.Any<UserAccount>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _notificationService.DidNotReceive().CreateAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>());
    }

    // ====================================================================
    // Full happy path - the exact fields the linked account and the
    // notification's deep-link get created with.
    // ====================================================================

    [Fact]
    public async Task EnableSignInAsync_CreatesTheAccountWithExactlyTheRightFields()
    {
        var staffId = Guid.NewGuid();
        var regularStaffGroupId = Guid.NewGuid();
        _userAccountService.ListRoleGroupsAsync(Arg.Any<CancellationToken>())
            .Returns(new List<RoleGroup> { new() { Id = regularStaffGroupId, Name = "Regular Staff" } });

        var result = await _sut.EnableSignInAsync(staffId, "New Person", "new.person@school.onmicrosoft.com", new List<Guid>());

        Assert.True(result.Success);
        await _userAccountService.Received(1).CreateUserAccountAsync(
            Arg.Is<UserAccount>(a =>
                a.DisplayName == "New Person" &&
                a.EntraUpn == "new.person@school.onmicrosoft.com" &&
                a.EntraObjectId == "new-object-id" &&
                a.StaffId == staffId &&
                a.RoleGroupId == regularStaffGroupId &&
                a.LocalLoginEnabled == false),
            null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EnableSignInAsync_NotifiesCorrespondent_WithTheSalaryTabDeepLink()
    {
        var staffId = Guid.NewGuid();

        await _sut.EnableSignInAsync(staffId, "New Person", "new.person@school.onmicrosoft.com", new List<Guid>());

        await _notificationService.Received(1).CreateAsync(
            Arg.Is<string>(m => m.Contains("New Person") && m.Contains("salary")),
            "Correspondent",
            $"/staff?open={staffId}&tab=Salary",
            Arg.Any<DateTime?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EnableSignInAsync_ReturnsTheTemporaryPassword_OnSuccess()
    {
        var result = await _sut.EnableSignInAsync(Guid.NewGuid(), "New Person", "new.person@school.onmicrosoft.com", new List<Guid>());

        Assert.Equal("Temp123!", result.TemporaryPassword);
    }

    // ====================================================================
    // Progress callback - real, user-facing behavior (the status text
    // shown during provisioning), worth verifying directly rather than
    // just trusting it fires.
    // ====================================================================

    [Fact]
    public async Task EnableSignInAsync_ReportsProgress_AtEachMajorStep()
    {
        var rule = new GroupAutomationRule { Id = Guid.NewGuid(), RuleName = "Some Rule", EntraGroupObjectId = "some-group", IsEnabled = true };
        _automationService.ListRulesAsync(Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(new List<GroupAutomationRule> { rule });
        _graphService.AddToGroupAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(new GraphOperationResult { Success = true });

        var progressMessages = new List<string>();
        await _sut.EnableSignInAsync(Guid.NewGuid(), "New Person", "new.person@school.onmicrosoft.com",
            new List<Guid> { rule.Id }, onProgress: progressMessages.Add);

        Assert.Contains(progressMessages, m => m.Contains("Creating Entra sign-in"));
        Assert.Contains(progressMessages, m => m.Contains("Some Rule"));
        Assert.Contains(progressMessages, m => m.Contains("Linking the app account"));
        // Order matters - creation must be reported before linking.
        var createIndex = progressMessages.FindIndex(m => m.Contains("Creating Entra sign-in"));
        var linkIndex = progressMessages.FindIndex(m => m.Contains("Linking the app account"));
        Assert.True(createIndex < linkIndex);
    }

    // ====================================================================
    // Unexpected exceptions - the outer catch, not just Graph's own
    // structured failure results.
    // ====================================================================

    [Fact]
    public async Task EnableSignInAsync_CatchesAnUnexpectedException_RatherThanCrashing()
    {
        _graphService.CreateUserAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<CreateUserResult>(new InvalidOperationException("Something genuinely unexpected")));

        var result = await _sut.EnableSignInAsync(Guid.NewGuid(), "New Person", "new.person@school.onmicrosoft.com", new List<Guid>());

        Assert.False(result.Success);
        Assert.Contains("unexpected problem", result.ErrorMessage);
        Assert.Contains("Something genuinely unexpected", result.ErrorMessage);
    }

    [Fact]
    public async Task EnableSignInAsync_HandlesGracefully_IfGraphReportsSuccessButOmitsObjectId()
    {
        // ObjectId is read via a null-forgiving operator (graphResult.ObjectId!),
        // trusting Graph's own contract that Success=true always comes
        // with a real ObjectId. This confirms that if that contract were
        // ever violated - a defensive-coding gap, not a hypothetical -
        // the outer catch still turns it into a clean failure result
        // rather than an unhandled NullReferenceException reaching the
        // caller.
        _graphService.CreateUserAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new CreateUserResult { Success = true, ObjectId = null, TemporaryPassword = "Temp123!" });

        var result = await _sut.EnableSignInAsync(Guid.NewGuid(), "New Person", "new.person@school.onmicrosoft.com", new List<Guid>());

        Assert.False(result.Success);
        Assert.Contains("unexpected problem", result.ErrorMessage);
    }
}
