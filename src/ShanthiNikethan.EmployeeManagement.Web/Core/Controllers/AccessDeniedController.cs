using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ShanthiNikethan.EmployeeManagement.Core.Controllers;

/// <summary>
/// Hand-written Access Denied page, same pattern as LocalAccountController —
/// this project has no MVC Razor Views infrastructure, so Microsoft.Identity.Web.UI's
/// built-in AccessDenied page (which needs one) 404s here. This replaces it.
///
/// [AllowAnonymous] is what actually matters for correctness: a user who
/// failed the allowlist check is, by definition, "authenticated but not
/// authorized" — if this page itself required authorization, we'd be back
/// to the exact infinite-redirect-loop bug this was built to fix. Marking
/// it AllowAnonymous exempts it from the FallbackPolicy entirely, which is
/// more robust than the earlier fix's approach of string-matching the
/// request path inside the policy's own assertion logic.
/// </summary>
[AllowAnonymous]
[Route("access-denied")]
public class AccessDeniedController : Controller
{
    [HttpGet]
    public IActionResult Index()
    {
        const string html = """
        <!DOCTYPE html>
        <html lang="en">
        <head>
            <meta charset="utf-8" />
            <meta name="viewport" content="width=device-width, initial-scale=1" />
            <title>Access Denied — Shanthi Nikethan Employee Management</title>
            <style>
                * { box-sizing: border-box; }
                html, body { margin: 0; height: 100%; }
                body {
                    font-family: "Inter", "Segoe UI", -apple-system, BlinkMacSystemFont, Roboto, Helvetica, Arial, sans-serif;
                    background: #f3f2f1; color: #242424;
                    display: flex; align-items: center; justify-content: center; min-height: 100vh;
                    padding: 24px;
                }
                .card {
                    background: #fff; border: 1px solid #e0e0e0; border-radius: 8px;
                    box-shadow: 0 0 2px rgba(0,0,0,0.12), 0 8px 24px rgba(0,0,0,0.10);
                    padding: 40px; max-width: 440px; width: 100%; text-align: center;
                }
                .icon-circle {
                    width: 56px; height: 56px; border-radius: 50%;
                    background: #fde7e9; color: #c50f1f;
                    display: flex; align-items: center; justify-content: center;
                    margin: 0 auto 20px auto;
                }
                h1 { font-size: 20px; font-weight: 700; margin: 0 0 10px 0; color: #242424; }
                p { font-size: 14px; color: #616161; line-height: 1.6; margin: 0 0 24px 0; }
                a.btn {
                    display: inline-block; background: #0f6cbd; color: #fff; text-decoration: none;
                    padding: 10px 22px; border-radius: 6px; font-size: 13.5px; font-weight: 600;
                }
                a.btn:hover { background: #115ea3; }
            </style>
        </head>
        <body>
            <div class="card">
                <div class="icon-circle">
                    <svg width="28" height="28" viewBox="0 0 20 20" fill="none" xmlns="http://www.w3.org/2000/svg">
                        <path d="M10 1.5c1.79 0 3.25 1.46 3.25 3.25v1.32c1.02.24 1.75 1.15 1.75 2.23v5.7a2.5 2.5 0 0 1-2.5 2.5h-5a2.5 2.5 0 0 1-2.5-2.5v-5.7c0-1.08.73-1.99 1.75-2.23V4.75C6.75 2.96 8.21 1.5 10 1.5Zm0 1.5c-.97 0-1.75.78-1.75 1.75v1.25h3.5V4.75c0-.97-.78-1.75-1.75-1.75Z" fill="currentColor"/>
                    </svg>
                </div>
                <h1>Access Denied</h1>
                <p>Your Microsoft account signed in successfully, but it isn't on the list of accounts authorized to use this system. If you believe this is a mistake, contact your system administrator.</p>
                <a class="btn" href="/signin">Back to Sign In</a>
            </div>
        </body>
        </html>
        """;
        return Content(html, "text/html");
    }
}
