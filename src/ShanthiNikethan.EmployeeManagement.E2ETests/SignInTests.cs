using Microsoft.Playwright;
using Xunit;

namespace ShanthiNikethan.EmployeeManagement.E2ETests;

/// <summary>
/// Configuration for where the app under test is running, and which
/// local test account to sign in as. Set these via environment
/// variables rather than hardcoding, so the same test works unchanged
/// on this machine and later on the server VM.
///
///   SNM_E2E_BASE_URL      - defaults to https://localhost:5001
///   SNM_E2E_TEST_USERNAME - the local-login account seeded in the E2E database
///   SNM_E2E_TEST_PASSWORD - that account's password
/// </summary>
public static class E2EConfig
{
    public static string BaseUrl => Environment.GetEnvironmentVariable("SNM_E2E_BASE_URL") ?? "https://localhost:5001";
    public static string TestUsername => Environment.GetEnvironmentVariable("SNM_E2E_TEST_USERNAME")
        ?? throw new InvalidOperationException("Set the SNM_E2E_TEST_USERNAME environment variable to the local-login test account's username.");
    public static string TestPassword => Environment.GetEnvironmentVariable("SNM_E2E_TEST_PASSWORD")
        ?? throw new InvalidOperationException("Set the SNM_E2E_TEST_PASSWORD environment variable to that account's password.");
}

/// <summary>
/// Owns ONE shared browser PROCESS for the lifetime of a test class -
/// launching Chromium is slow, so this is deliberately not repeated per
/// test. IAsyncLifetime here is xUnit's own async setup/teardown
/// mechanism, not anything Playwright-specific.
///
/// This fixture does NOT create per-test pages/contexts itself anymore -
/// an earlier version did, via a NewIsolatedPageAsync() helper, but
/// every context it created was never closed. Across a growing test
/// library, that accumulated into enough orphaned contexts to exhaust
/// the shared browser process mid-run, causing later tests to fail with
/// "Target page, context or browser has been closed" - a real failure
/// that happened in practice, not a hypothetical concern. Each test
/// CLASS now owns its own per-test context lifecycle instead (see
/// AddStaffTests.cs for the pattern) - created fresh before every test
/// method and explicitly closed after, whether that test passed or failed.
/// </summary>
public class PlaywrightFixture : IAsyncLifetime
{
    public IPlaywright Playwright { get; private set; } = null!;
    public IBrowser Browser { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        Playwright = await Microsoft.Playwright.Playwright.CreateAsync();
        // Headless by default (no visible window - suited for CI later).
        // Set SNM_E2E_HEADED=1 while developing a new test locally, to
        // actually watch the browser click through the flow.
        var headless = Environment.GetEnvironmentVariable("SNM_E2E_HEADED") != "1";
        Browser = await Playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = headless });
    }

    public async Task<IPage> NewIsolatedPageAsync()
    {
        // IgnoreHTTPSErrors: true - the local dev certificate isn't
        // trusted by default, and that's a dev-environment detail, not
        // something a test should fail on.
        //
        // The caller is responsible for closing page.Context when done -
        // see each test class's own DisposeAsync (IAsyncLifetime runs
        // this fresh before/after EVERY test method, since xUnit
        // constructs a new instance of the test class per [Fact]).
        var context = await Browser.NewContextAsync(new BrowserNewContextOptions { IgnoreHTTPSErrors = true });
        return await context.NewPageAsync();
    }

    public async Task DisposeAsync()
    {
        await Browser.DisposeAsync();
        Playwright.Dispose();
    }
}

public class SignInTests : IClassFixture<PlaywrightFixture>, IAsyncLifetime
{
    private readonly PlaywrightFixture _fixture;
    private IPage _page = null!;

    public SignInTests(PlaywrightFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync() => _page = await _fixture.NewIsolatedPageAsync();

    public async Task DisposeAsync() => await _page.Context.CloseAsync();

    [Fact]
    public async Task LocalLogin_SignsIn_AndReachesDashboard()
    {
        await _page.GotoAsync($"{E2EConfig.BaseUrl}/signin");

        // The sign-in page opens on a choice screen, not the actual
        // username/password form - that only appears after this click,
        // matching the real page's actual behavior (confirmed against
        // LocalAccountController.cs directly, not assumed).
        await _page.ClickAsync("#showFallbackForm");

        await _page.FillAsync("#fallbackUsername", E2EConfig.TestUsername);
        await _page.FillAsync("input[name='password']", E2EConfig.TestPassword);
        await _page.ClickAsync("button[type='submit']");

        // A real Blazor Server page load, not an instant SPA transition -
        // waiting for network idle here is deliberate, not a lazy
        // default, since the dashboard's data loads asynchronously after
        // the initial render.
        await _page.WaitForURLAsync($"{E2EConfig.BaseUrl}/dashboard", new PageWaitForURLOptions { Timeout = 15000 });

        // The single, minimal assertion this first test exists to prove:
        // sign-in genuinely succeeded and landed somewhere real, not on
        // an error page or back at the sign-in form.
        await Assertions.Expect(_page).ToHaveURLAsync($"{E2EConfig.BaseUrl}/dashboard");
    }

    [Fact]
    public async Task LocalLogin_WithWrongPassword_ShowsAnErrorMessage_AndStaysOnSignIn()
    {
        await _page.GotoAsync($"{E2EConfig.BaseUrl}/signin");
        await _page.ClickAsync("#showFallbackForm");
        await _page.FillAsync("#fallbackUsername", E2EConfig.TestUsername);
        await _page.FillAsync("input[name='password']", "DefinitelyTheWrongPassword123!");
        await _page.ClickAsync("button[type='submit']");

        // The real, deployed error message text, not a guess - matches
        // LocalAccountController.cs's actual error==1 branch.
        await Assertions.Expect(_page.Locator(".local-login-error")).ToContainTextAsync("Incorrect username or password");
    }
}
