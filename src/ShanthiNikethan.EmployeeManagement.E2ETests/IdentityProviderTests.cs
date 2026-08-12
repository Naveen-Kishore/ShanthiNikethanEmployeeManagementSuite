using Microsoft.Playwright;
using ShanthiNikethan.EmployeeManagement.E2ETests.TestHelpers;
using Xunit;

namespace ShanthiNikethan.EmployeeManagement.E2ETests;

public class IdentityProviderTests : IClassFixture<PlaywrightFixture>, IAsyncLifetime
{
    private readonly PlaywrightFixture _fixture;
    private IPage _page = null!;

    public IdentityProviderTests(PlaywrightFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync() => _page = await _fixture.NewIsolatedPageAsync();

    public async Task DisposeAsync() => await _page.Context.CloseAsync();

    /// <summary>
    /// Unlike GraphDiagnostics or the real Entra provisioning paths
    /// elsewhere in this app, everything on this page is genuinely safe
    /// to exercise fully - confirmed directly in the code, the snippet
    /// generator never calls a service, never touches the database, and
    /// never modifies the app's own running configuration. It only
    /// builds a string the person is expected to copy and paste
    /// themselves.
    /// </summary>
    private async Task NavigateToIdentityProviderAsync()
    {
        await _page.GetByText("Administration", new PageGetByTextOptions { Exact = true }).ClickAsync();
        await _page.GetByText("Identity Provider Settings", new PageGetByTextOptions { Exact = true }).ClickAsync();
    }

    [Fact]
    public async Task CurrentConfiguration_ShowsTheActiveTenantAndClientId_ReadOnly()
    {
        await AuthHelper.SignInAsync(_page);
        await NavigateToIdentityProviderAsync();

        // Scoped to the FIRST .calc-panel, AND to the <label> tag
        // specifically - the panel's own explanatory paragraph mentions
        // "Tenant ID" and "Client ID" in its prose too, so panel-level
        // scoping alone still collided with that unrelated text.
        var readOnlyPanel = _page.Locator(".calc-panel").First;
        await Assertions.Expect(readOnlyPanel.Locator("label").Filter(new LocatorFilterOptions { HasText = "Tenant ID" })).ToBeVisibleAsync();
        await Assertions.Expect(readOnlyPanel.Locator("label").Filter(new LocatorFilterOptions { HasText = "Client (App) ID" })).ToBeVisibleAsync();
    }

    [Fact]
    public async Task GenerateSnippet_WithoutASecret_OmitsTheClientSecretLine()
    {
        await AuthHelper.SignInAsync(_page);
        await NavigateToIdentityProviderAsync();

        var tenantId = Guid.NewGuid().ToString();
        var clientId = Guid.NewGuid().ToString();
        await _page.GetByPlaceholder("e.g. 11111111-2222-3333-4444-555555555555").FillAsync(tenantId);
        await _page.Keyboard.PressAsync("Tab"); // blur - plain @bind updates on change, not oninput
        await _page.GetByPlaceholder("e.g. 66666666-7777-8888-9999-000000000000").FillAsync(clientId);
        await _page.Keyboard.PressAsync("Tab");
        // Client Secret deliberately left blank.
        await _page.GetByText("Generate snippet").ClickAsync();

        var snippetBox = _page.Locator("#snippetBox");
        await Assertions.Expect(snippetBox).ToContainTextAsync(tenantId);
        await Assertions.Expect(snippetBox).ToContainTextAsync(clientId);
        await Assertions.Expect(snippetBox).Not.ToContainTextAsync("ClientSecret");
    }

    [Fact]
    public async Task GenerateSnippet_WithASecret_IncludesTheClientSecretLine()
    {
        await AuthHelper.SignInAsync(_page);
        await NavigateToIdentityProviderAsync();

        var tenantId = Guid.NewGuid().ToString();
        var clientId = Guid.NewGuid().ToString();
        var secret = $"E2ETestSecret{(uint)Guid.NewGuid().GetHashCode()}";
        await _page.GetByPlaceholder("e.g. 11111111-2222-3333-4444-555555555555").FillAsync(tenantId);
        await _page.Keyboard.PressAsync("Tab");
        await _page.GetByPlaceholder("e.g. 66666666-7777-8888-9999-000000000000").FillAsync(clientId);
        await _page.Keyboard.PressAsync("Tab");
        await _page.GetByPlaceholder("Leave blank if this app registration has no secret").FillAsync(secret);
        await _page.Keyboard.PressAsync("Tab");
        await _page.GetByText("Generate snippet").ClickAsync();

        var snippetBox = _page.Locator("#snippetBox");
        await Assertions.Expect(snippetBox).ToContainTextAsync("ClientSecret");
        await Assertions.Expect(snippetBox).ToContainTextAsync(secret);
    }

    [Fact]
    public async Task CopySnippet_ChangesTheButtonTextToConfirmIt()
    {
        // Confirms the UI's own feedback state changes - not attempting
        // to verify actual OS clipboard contents, which would need
        // browser permission handling beyond what this checks.
        await AuthHelper.SignInAsync(_page);
        await NavigateToIdentityProviderAsync();

        await _page.GetByPlaceholder("e.g. 11111111-2222-3333-4444-555555555555").FillAsync(Guid.NewGuid().ToString());
        await _page.Keyboard.PressAsync("Tab");
        await _page.GetByPlaceholder("e.g. 66666666-7777-8888-9999-000000000000").FillAsync(Guid.NewGuid().ToString());
        await _page.Keyboard.PressAsync("Tab");
        await _page.GetByText("Generate snippet").ClickAsync();

        await _page.GetByText("Copy to clipboard").ClickAsync();

        await Assertions.Expect(_page.GetByText("Copied!")).ToBeVisibleAsync();
    }
}
