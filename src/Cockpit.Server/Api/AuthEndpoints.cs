using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Cockpit.Shared;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace Cockpit.Server.Api;

/// <summary>Single-user password gate, enabled only when COCKPIT_PASSWORD is set. Cookie session, 90 days sliding.</summary>
public sealed record AuthOptions(bool Enabled, string? Password);

public static class AuthEndpoints
{
    public const string LoginRateLimitPolicy = "login";

    public static void MapAuth(this RouteGroupBuilder api)
    {
        api.MapGet("/auth/status", (HttpContext ctx, AuthOptions auth) => Results.Ok(new AuthStatusDto
        {
            Required = auth.Enabled,
            Authenticated = !auth.Enabled || ctx.User.Identity?.IsAuthenticated == true,
        })).AllowAnonymous();

        api.MapPost("/auth/login", async (LoginRequest req, HttpContext ctx, AuthOptions auth) =>
        {
            if (!auth.Enabled) return Results.NoContent();
            var expected = Encoding.UTF8.GetBytes(auth.Password ?? "");
            var given = Encoding.UTF8.GetBytes(req.Password ?? "");
            var ok = expected.Length == given.Length && CryptographicOperations.FixedTimeEquals(expected, given);
            if (!ok)
            {
                await Task.Delay(Random.Shared.Next(300, 800)); // blunt brute-force a little more
                return Results.Json(new ApiError { Code = "invalid_password", Message = "Wrong password." }, statusCode: 401);
            }
            var identity = new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, "owner") }, CookieAuthenticationDefaults.AuthenticationScheme);
            await ctx.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity), new AuthenticationProperties { IsPersistent = true });
            return Results.NoContent();
        }).AllowAnonymous().RequireRateLimiting(LoginRateLimitPolicy);

        api.MapPost("/auth/logout", async (HttpContext ctx) =>
        {
            await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.NoContent();
        }).AllowAnonymous();
    }
}
