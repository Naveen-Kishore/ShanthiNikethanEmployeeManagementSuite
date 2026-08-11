using Microsoft.Playwright;
using ShanthiNikethan.EmployeeManagement.E2ETests;

namespace ShanthiNikethan.EmployeeManagement.E2ETests.TestHelpers;

/// <summary>
/// Shared across every E2E test that needs to start from a signed-in
/// state (which is nearly all of them) - extracted from SignInTests.cs
/// specifically so the actual selectors for the sign-in form live in
/// exactly one place, not copy-pasted into every new test file.
/// </summary>
public static class AuthHelper
{
    public static async Task SignInAsync(IPage page)
    {
        await page.GotoAsync($"{E2EConfig.BaseUrl}/signin");
        await page.ClickAsync("#showFallbackForm");
        await page.FillAsync("#fallbackUsername", E2EConfig.TestUsername);
        await page.FillAsync("input[name='password']", E2EConfig.TestPassword);
        await page.ClickAsync("button[type='submit']");
        await page.WaitForURLAsync($"{E2EConfig.BaseUrl}/dashboard", new PageWaitForURLOptions { Timeout = 15000 });
    }
}
