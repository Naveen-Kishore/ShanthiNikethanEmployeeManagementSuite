using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShanthiNikethan.EmployeeManagement.Core.Services;
using ShanthiNikethan.EmployeeManagement.Modules.Admin.Services;

namespace ShanthiNikethan.EmployeeManagement.Core.Controllers;

/// <summary>
/// Handles the local-credential fallback login — deliberately a plain MVC
/// controller, not a Blazor page. Routes.razor's AuthorizeRouteView
/// enforces the app's global "must be authenticated" policy on every
/// Blazor-routed page, which would make a Blazor-based login page
/// unreachable by the very unauthenticated users who need it. A plain
/// controller action marked [AllowAnonymous] sits outside that pipeline
/// entirely, so it stays reachable regardless of auth state — which is
/// the whole point of an emergency fallback.
///
/// Returns hand-written HTML directly rather than a .cshtml view, since
/// this project has no MVC Views infrastructure otherwise (it's a
/// Blazor-first app) and setting one up just for this one simple form
/// isn't worth the added surface area.
/// </summary>
[AllowAnonymous]
[Route("signin")]
public class LocalAccountController : Controller
{
    private readonly IUserAccountService _userAccountService;
    private readonly IAuditService _audit;

    public LocalAccountController(IUserAccountService userAccountService, IAuditService audit)
    {
        _userAccountService = userAccountService;
        _audit = audit;
    }

    [HttpGet]
    public IActionResult Login(string? returnUrl = null, string? error = null)
    {
        var safeReturnUrl = string.IsNullOrWhiteSpace(returnUrl) || !returnUrl.StartsWith('/') ? "/" : returnUrl;
        var encodedReturnUrl = HtmlEncoder.Default.Encode(safeReturnUrl);
        var showFallbackForm = error == "1"; // a failed attempt should re-open the form it came from, not reset to the choice screen

        var errorHtml = error == "1"
            ? "<div class=\"local-login-error\">Incorrect username or password.</div>"
            : "";

        var html = $$"""
        <!DOCTYPE html>
        <html lang="en">
        <head>
            <meta charset="utf-8" />
            <meta name="viewport" content="width=device-width, initial-scale=1" />
            <title>Sign In — Shanthi Nikethan Employee Management</title>
            <style>
                * { box-sizing: border-box; }
                html, body { margin: 0; height: 100%; }
                body {
                    font-family: "Inter", "Segoe UI", -apple-system, BlinkMacSystemFont, Roboto, Helvetica, Arial, sans-serif;
                    position: relative; overflow: hidden; min-height: 100vh;
                }

                /* ---- Background is a real photo of the school campus (a sunset over the
                       treeline), not an abstract effect — genuinely yours rather than
                       reverse-engineered from someone else's site, and it sidesteps the
                       compression-grain problem entirely: there's no compressed video to
                       amplify, just a normal JPEG, so brightening it never surfaces artifacts.

                       Filters (saturate/contrast/brightness) are gentle — enhancing the
                       photo's own real colors, not forcing it into a flat brand-purple tint
                       the way the old grayscale-then-recolor video trick did. That trick made
                       sense for abstract smoke; it would have destroyed what's actually
                       beautiful about a real sunset photo.

                       The gradient scrim is what makes it work as a login background rather
                       than just a pretty photo: dark enough on the left for the white logo to
                       stay legible against whatever sky color happens to be there, light
                       enough on the right for the dark heading text and white buttons to read
                       clearly, with the same left-to-right personality the old design had. ---- */
                .signin-bg {
                    position: fixed; inset: 0; z-index: 0; overflow: hidden;
                    background: #6933b6; /* fallback while the image loads */
                }
                .signin-bg-photo {
                    position: absolute; inset: 0; width: 100%; height: 100%;
                    object-fit: cover; object-position: center 30%;
                    filter: saturate(1.1) contrast(1.05) brightness(0.72);
                }
                .signin-bg-scrim {
                    position: absolute; inset: 0;
                    background: linear-gradient(115deg,
                        rgba(6,6,10,0.85) 0%,
                        rgba(12,12,16,0.60) 22%,
                        rgba(12,12,16,0.28) 40%,
                        rgba(12,12,16,0) 55%);
                }

                .signin-split { position: relative; z-index: 1; display: flex; min-height: 100vh; }

                .signin-brand-col {
                    flex: 0 0 60%; display: flex; align-items: center; justify-content: center; padding: 24px;
                }
                .signin-brand-col img {
                    width: 260px; max-width: 42%; opacity: 0.98;
                }

                .signin-actions-col {
                    flex: 1 1 40%; background: #fbfaf9; position: relative;
                    display: flex; align-items: center; justify-content: center; padding: 24px;
                }
                .signin-actions-inner { width: 100%; max-width: 380px; }
                .welcome-heading {
                    margin: 0 0 6px 0; color: #1f1f1f; font-size: 28px; font-weight: 700;
                    letter-spacing: -0.01em; line-height: 1.2;
                }
                .welcome-subhead {
                    margin: 0 0 26px 0; color: #5c5c5c; font-size: 14px; font-weight: 500; line-height: 1.4;
                }
                .security-notice {
                    margin-top: 18px; display: flex; align-items: flex-start; gap: 7px;
                    font-size: 13px; color: #4a4a4a; line-height: 1.5;
                }
                .security-notice svg { flex-shrink: 0; width: 15px; height: 15px; margin-top: 2px; }

                .page-footer {
                    position: absolute; left: 24px; right: 24px; bottom: 20px;
                    text-align: center; font-size: 13px; color: #6b6b6b;
                }

                /* ---- Mobile: collapse to a single purple column (matches Pickit's own narrow-
                       viewport behaviour) instead of squeezing the two-panel split into a sliver. ---- */
                @media (max-width: 780px) {
                    .signin-split { flex-direction: column; }
                    .signin-brand-col { flex: 0 0 auto; padding: 56px 24px 8px; }
                    .signin-brand-col img { width: 220px; max-width: 56%; }
                    .signin-actions-col { flex: 1 1 auto; flex-direction: column; background: transparent; padding: 24px 24px 56px; }
                    .welcome-heading { color: #fff; text-align: center; }
                    .welcome-subhead { color: #ece3fa; text-align: center; }
                    .signin-actions-inner form label,
                    .signin-subtitle { color: #f1e9fb; }
                    .security-notice { color: #f1e9fb; }
                    .page-footer { position: static; margin-top: 28px; color: #e5d9f5; }
                }

                .signin-btn {
                    display: flex; align-items: center; justify-content: center; gap: 12px;
                    width: 100%; background: #fff; color: #242424; border: none; border-radius: 6px;
                    padding: 13px 16px; font-size: 14.5px; font-weight: 600; cursor: pointer;
                    margin-bottom: 14px; text-decoration: none; font-family: inherit;
                    box-shadow: 0 0 2px rgba(0,0,0,0.12), 0 2px 4px rgba(0,0,0,0.14);
                    transition: background 120ms cubic-bezier(0.33, 0, 0.67, 1);
                }
                .signin-btn:hover { background: #f5f3fa; }

                .ms-logo { width: 18px; height: 18px; flex-shrink: 0; display: grid; grid-template-columns: 1fr 1fr; gap: 1px; }
                .ms-logo span { display: block; }
                .ms-logo span:nth-child(1) { background: #f25022; }
                .ms-logo span:nth-child(2) { background: #7fba00; }
                .ms-logo span:nth-child(3) { background: #00a4ef; }
                .ms-logo span:nth-child(4) { background: #ffb900; }

                #fallbackChoice { display: {{(showFallbackForm ? "none" : "block")}}; }
                #fallbackForm { display: {{(showFallbackForm ? "block" : "none")}}; }

                .signin-actions-inner form .form-row { margin-bottom: 14px; }
                .signin-actions-inner form label { display: block; color: #3d2a5c; font-size: 12.5px; font-weight: 500; margin-bottom: 5px; }
                .signin-actions-inner form input {
                    width: 100%; background: #fff; border: 1px solid #b9a3d9; border-radius: 6px;
                    padding: 8px 10px; font-size: 13.5px; color: #242424; font-family: inherit;
                }
                .signin-actions-inner form input:focus { outline: none; border-color: #333333; box-shadow: inset 0 -2px 0 0 #333333; }
                .local-login-error { background: #fde2e2; color: #b42318; padding: 9px 12px; border-radius: 6px; font-size: 12.5px; margin-bottom: 14px; }
                .signin-subtitle { color: #3d2a5c; font-size: 12.5px; margin: -16px 0 20px 0; }
                .signin-back-link {
                    display: block; text-align: center; margin-top: 14px; font-size: 12.5px;
                    color: #333333; text-decoration: none; cursor: pointer; background: none; border: none;
                    width: 100%; font-family: inherit;
                }
                .signin-back-link:hover { text-decoration: underline; }
            </style>
        </head>
        <body>
            <div class="signin-bg">
                <img class="signin-bg-photo" src="/img/signin-background.jpg" alt="" oncontextmenu="return false;" />
                <div class="signin-bg-scrim"></div>
            </div>
            <div class="signin-split">
            <div class="signin-brand-col">
                <img src="/img/logo-emblem-full-white.png" alt="Shanthi Nikethan Matric Higher Secondary School" oncontextmenu="return false;" />
            </div>
            <div class="signin-actions-col">
                <div class="signin-actions-inner">
                    <h1 class="welcome-heading">Staff Sign In</h1>
                    <p class="welcome-subhead">Authorized personnel authentication required.</p>

                    <div id="fallbackChoice">
                        <a class="signin-btn" href="/MicrosoftIdentity/Account/SignIn?redirectUri={{encodedReturnUrl}}">
                            <span class="ms-logo"><span></span><span></span><span></span><span></span></span>
                            Sign in with Microsoft
                        </a>
                        <button type="button" class="signin-btn" onclick="document.getElementById('fallbackChoice').style.display='none'; document.getElementById('fallbackForm').style.display='block'; document.getElementById('fallbackUsername').focus();">
                            Sign in with fallback account
                        </button>
                    </div>

                    <div id="fallbackForm">
                        <p class="signin-subtitle">Emergency fallback — use only if Microsoft sign-in is unavailable.</p>
                        {{errorHtml}}
                        <form method="post" action="/signin">
                            <input type="hidden" name="returnUrl" value="{{encodedReturnUrl}}" />
                            <div class="form-row">
                                <label>Username</label>
                                <input type="text" id="fallbackUsername" name="username" autocomplete="username" required autofocus />
                            </div>
                            <div class="form-row">
                                <label>Password</label>
                                <input type="password" name="password" autocomplete="current-password" required />
                            </div>
                            <button type="submit" class="signin-btn">Sign in</button>
                        </form>
                        <button type="button" class="signin-back-link" onclick="document.getElementById('fallbackForm').style.display='none'; document.getElementById('fallbackChoice').style.display='block';">← Back</button>
                    </div>

                    <div class="security-notice">
                        <svg viewBox="0 0 20 20" fill="none" xmlns="http://www.w3.org/2000/svg"><path d="M10 1C12.2091 1 14 2.79086 14 5V7.0498C15.1411 7.28142 16 8.29051 16 9.5V15.5C16 16.8807 14.8807 18 13.5 18H6.5C5.11929 18 4 16.8807 4 15.5V9.5C4 8.29051 4.85886 7.28142 6 7.0498V5C6 2.79086 7.79086 1 10 1ZM6.5 8C5.67157 8 5 8.67157 5 9.5V15.5C5 16.3284 5.67157 17 6.5 17H13.5C14.3284 17 15 16.3284 15 15.5V9.5C15 8.67157 14.3284 8 13.5 8H6.5ZM10 11.5C10.5523 11.5 11 11.9477 11 12.5C11 13.0523 10.5523 13.5 10 13.5C9.44772 13.5 9 13.0523 9 12.5C9 11.9477 9.44772 11.5 10 11.5ZM10 2C8.34315 2 7 3.34315 7 5V7H13V5C13 3.34315 11.6569 2 10 2Z" fill="currentColor"/></svg>
                        <span>Restricted to authorized Shanthi Nikethan staff. Access attempts are monitored and logged.</span>
                    </div>
                </div>
                <div class="page-footer">© 2026 Shanthi Nikethan Educational Trust. All rights reserved.</div>
            </div>
            </div>
        </body>
        </html>
        """;

        return Content(html, "text/html");
    }

    [HttpPost]
    public async Task<IActionResult> Login(string username, string password, string? returnUrl)
    {
        var account = await _userAccountService.VerifyLocalLoginAsync(username, password);
        if (account == null)
        {
            // Log the failed attempt against whatever username was typed -
            // there's no real account to attribute it to, but the attempted
            // username itself is exactly the useful signal for spotting
            // repeated guessing against this fallback path.
            await _audit.LogAsync("Auth", "Session", null, "SignInFailed",
                context: $"Local sign-in failed for username \"{username}\"",
                actorDisplayNameOverride: username, actorObjectIdOverride: "(local, unverified)");

            var redirectBack = "/signin?error=1";
            if (!string.IsNullOrWhiteSpace(returnUrl))
                redirectBack += $"&returnUrl={Uri.EscapeDataString(returnUrl)}";
            return Redirect(redirectBack);
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, account.DisplayName),
            new("ua_id", account.Id.ToString())
        };
        var identity = new ClaimsIdentity(claims, "LocalAuth");
        var principal = new ClaimsPrincipal(identity);

        await HttpContext.SignInAsync("LocalAuth", principal, new AuthenticationProperties
        {
            IsPersistent = false,
            ExpiresUtc = DateTimeOffset.UtcNow.AddHours(8)
        });

        // Explicit actor override here too: SignInAsync above doesn't
        // retroactively update HttpContext.User for the rest of THIS
        // request, so ICurrentUser would still report the pre-signin
        // (anonymous) state if we relied on it instead.
        await _audit.LogAsync("Auth", "Session", account.Id.ToString(), "SignIn",
            context: "Local fallback account sign-in",
            actorDisplayNameOverride: account.DisplayName, actorObjectIdOverride: $"local:{account.Id}");

        var target = string.IsNullOrWhiteSpace(returnUrl) || !returnUrl.StartsWith('/') ? "/" : returnUrl;
        return LocalRedirect(target);
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        await _audit.LogAsync("Auth", "Session", null, "SignOut", context: "Local fallback account sign-out");
        await HttpContext.SignOutAsync("LocalAuth");
        return LocalRedirect("/signin");
    }
}
