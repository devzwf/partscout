using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Identity;
using PartScout.Api.Infrastructure;
using PartScout.Api.Services;
using PartScout.Suppliers;

namespace PartScout.Api.Endpoints;

/// <summary>Connexion, « Mon compte » et gestion des comptes. Le premier compte, créé au premier démarrage, est administrateur et crée les autres.</summary>
internal static class AuthEndpoints
{
    private static readonly PasswordHasher<string> hasher = new();
    // Pour un nom inconnu, on vérifie quand même une empreinte : le temps de réponse ne révèle pas quels comptes existent.
    private static readonly string decoyHash = hasher.HashPassword("", Db.NewStamp());

    internal static async Task SignIn(HttpContext c, long id, string name, string stamp)
    {
        var signedIn = c.RequestServices.GetRequiredService<TimeProvider>().GetUtcNow().ToUnixTimeSeconds();
        var identity = new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, id.ToString()), new Claim(ClaimTypes.Name, name), new Claim(Program.StampClaim, stamp),
            new Claim(Program.SignedInClaim, signedIn.ToString())
        }, CookieAuthenticationDefaults.AuthenticationScheme);
        await c.SignInAsync(new ClaimsPrincipal(identity), new AuthenticationProperties { IsPersistent = true });
    }

    internal static IResult Bad(string key, string message) => Results.BadRequest(new { messageKey = key, message });
    internal static string CleanName(string? name) => (name ?? "").Trim();
    internal static bool ValidName(string name) => name.Length is >= 2 and <= 40 && name.All(ch => char.IsLetterOrDigit(ch) || ch is '.' or '_' or '-');
    internal static bool ValidPassword(string? password) => (password ?? "").Length >= 10;
    internal const string NameRule = "Name: 2 to 40 letters, digits, dots, dashes or underscores.";
    internal const string PasswordRule = "Password: at least 10 characters.";

    public static void Map(WebApplication app, RouteGroupBuilder api)
    {
        app.MapGet("/api/auth/status", (HttpContext c, Db db, AppOptions o) =>
        {
            var hasUsers = db.HasUsers();
            var user = c.User.Identity?.IsAuthenticated == true ? db.GetUser(c.UserId()) : null;
            return new
            {
                setupRequired = !hasUsers,
                setupTokenRequired = o.SetupToken != null && !hasUsers,
                authenticated = user != null,
                demo = o.Demo,
                user = user == null ? null : new { user.Id, user.Name, user.IsAdmin, user.Lang }
            };
        });

        app.MapPost("/api/auth/setup", async (SetupRequest r, HttpContext c, Db db, AppOptions o) =>
        {
            if (o.SetupToken != null && !System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
                    System.Text.Encoding.UTF8.GetBytes(r.Token ?? ""), System.Text.Encoding.UTF8.GetBytes(o.SetupToken)))
                return Results.Json(new { messageKey = "badSetupToken", message = "Incorrect setup token." }, statusCode: 403);
            var name = CleanName(r.Name) is { Length: > 0 } given ? given : "admin";
            if (!ValidName(name)) return Bad("nameInvalid", NameRule);
            if (!ValidPassword(r.Password)) return Bad("passwordTooShort", PasswordRule);
            var id = db.CreateFirstUser(name, hasher.HashPassword(name, r.Password!));
            if (id == null) return Results.Conflict(new { messageKey = "accountExists", message = "The account already exists." });
            var user = db.GetUser(id.Value)!;
            await SignIn(c, user.Id, user.Name, user.Stamp);
            return Results.Ok();
        }).RequireRateLimiting("login");

        app.MapPost("/api/auth/login", async (LoginRequest r, HttpContext c, Db db) =>
        {
            var user = db.GetUserByName(CleanName(r.Name));
            var result = hasher.VerifyHashedPassword(user?.Name ?? "", user?.PasswordHash ?? decoyHash, r.Password ?? "");
            if (user == null || result == PasswordVerificationResult.Failed)
                return Results.Json(new { messageKey = "wrongLogin", message = "Incorrect name or password." }, statusCode: 401);
            await SignIn(c, user.Id, user.Name, user.Stamp);
            return Results.Ok();
        }).RequireRateLimiting("login");

        app.MapPost("/api/auth/logout", async (HttpContext c) =>
        {
            await c.SignOutAsync();
            return Results.Ok();
        });

        // ---- Mon compte ----
        api.MapPut("/me/language", (LanguageRequest r, HttpContext c, Db db) =>
        {
            if (r.Lang is not ("fr" or "en")) return Results.BadRequest();
            db.SetLanguage(c.UserId(), r.Lang);
            return Results.NoContent();
        });
        api.MapPut("/me/password", async (ChangePasswordRequest r, HttpContext c, Db db) =>
        {
            var user = db.GetUser(c.UserId());
            if (user == null) return Results.Unauthorized();
            if (hasher.VerifyHashedPassword(user.Name, user.PasswordHash, r.Current ?? "") == PasswordVerificationResult.Failed)
                return Bad("wrongCurrentPassword", "Incorrect current password.");
            if (!ValidPassword(r.Password)) return Bad("passwordTooShort", PasswordRule);
            var stamp = db.SetPassword(user.Id, hasher.HashPassword(user.Name, r.Password!))!;
            await SignIn(c, user.Id, user.Name, stamp); // les autres sessions tombent, pas celle-ci
            return Results.NoContent();
        }).RequireRateLimiting("login");

        // ---- Comptes : réservé aux administrateurs, vérifié en base à chaque requête ----
        var admin = api.MapGroup("/admin/users").AddEndpointFilter(async (ctx, next) =>
        {
            var c = ctx.HttpContext;
            return c.RequestServices.GetRequiredService<Db>().GetUser(c.UserId())?.IsAdmin == true ? await next(ctx) : Results.StatusCode(403);
        });
        admin.MapGet("", (Db db) => db.ListUsers().Select(u => new { u.Id, u.Name, u.IsAdmin, u.Lang, u.CreatedUtc }));
        admin.MapPost("", (CreateUserRequest r, Db db) =>
        {
            var name = CleanName(r.Name);
            if (!ValidName(name)) return Bad("nameInvalid", NameRule);
            if (!ValidPassword(r.Password)) return Bad("passwordTooShort", PasswordRule);
            var id = db.CreateUser(name, hasher.HashPassword(name, r.Password!), r.IsAdmin, r.Lang is "fr" or "en" ? r.Lang : "");
            return id == null
                ? Results.Conflict(new { messageKey = "nameTaken", message = "This name is already taken." })
                : Results.Ok(new { id });
        });
        // Sur son propre compte, on passe par « Mon compte » : il y faut l'ancien mot de passe, et un administrateur reste toujours.
        admin.MapPut("/{id:long}/password", (long id, SetPasswordRequest r, HttpContext c, Db db) =>
        {
            if (id == c.UserId()) return Bad("notOnSelf", "Not on your own account.");
            var user = db.GetUser(id);
            if (user == null) return Results.NotFound();
            if (!ValidPassword(r.Password)) return Bad("passwordTooShort", PasswordRule);
            db.SetPassword(id, hasher.HashPassword(user.Name, r.Password!));
            return Results.NoContent();
        });
        admin.MapDelete("/{id:long}", (long id, HttpContext c, Db db, SettingsService s) =>
        {
            if (id == c.UserId()) return Bad("notOnSelf", "Not on your own account.");
            if (!db.DeleteUser(id)) return Results.NotFound();
            s.Forget(id);
            return Results.NoContent();
        });
    }
}

public sealed record SetupRequest(string? Password, string? Token, string? Name);
public sealed record LoginRequest(string? Name, string? Password);
public sealed record LanguageRequest(string? Lang);
public sealed record ChangePasswordRequest(string? Current, string? Password);
public sealed record CreateUserRequest(string? Name, string? Password, bool IsAdmin = false, string? Lang = null);
public sealed record SetPasswordRequest(string? Password);
