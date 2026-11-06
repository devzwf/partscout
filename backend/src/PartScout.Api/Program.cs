using System.Security.Claims;
using System.Text.Json;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using PartScout.Api.Demo;
using PartScout.Api.Infrastructure;
using PartScout.Api.Services;
using PartScout.Suppliers;

// L'image n'a ni shell ni curl : la sonde de santé du conteneur passe par ce binaire.
if (args.Contains("--healthcheck"))
{
    using var probe = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
    try { return (await probe.GetAsync("http://127.0.0.1:8080/healthz")).IsSuccessStatusCode ? 0 : 1; }
    catch (Exception) { return 1; }
}

var builder = WebApplication.CreateBuilder(args);
var options = AppOptions.FromEnvironment(builder.Configuration);
Directory.CreateDirectory(options.DataDir);

// En démo, et là seulement, les fournisseurs sont simulés : aucun appel ne sort.
if (options.Demo) WebHelper.Client = new HttpClient(new DemoHandler { Slow = builder.Configuration["PARTSCOUT_DEMO_FAST"] is null });

// Derrière un reverse proxy : sans cela, le cookie ne passe pas en Secure et tout le monde partage la limite de connexions.
// Seul ce proxy est cru, sinon n'importe qui pourrait se dire en HTTPS ou changer d'adresse pour contourner la limite.
var trustedProxy = System.Net.IPAddress.TryParse(builder.Configuration["PARTSCOUT_TRUSTED_PROXY"], out var proxyAddress) ? proxyAddress : null;
if (trustedProxy != null)
{
    builder.Services.Configure<ForwardedHeadersOptions>(o =>
    {
        o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        o.ForwardLimit = 1;
        o.KnownIPNetworks.Clear();
        o.KnownProxies.Clear();
        o.KnownProxies.Add(trustedProxy);
    });
}

builder.Services.AddSingleton(options);
var vault = new SecretVault(options.MasterKey);
builder.Services.AddSingleton(new Db(Path.Combine(options.DataDir, "partscout.db"), vault));
builder.Services.AddSingleton(vault);
builder.Services.AddSingleton<SettingsService>();
builder.Services.AddSingleton<SearchService>();
builder.Services.AddSingleton<RatesService>();
builder.Services.AddHostedService<BackupService>();
builder.Services.AddMemoryCache();
builder.Services.ConfigureHttpJsonOptions(o => Json.Configure(o.SerializerOptions));
builder.Services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(options.DataDir, "keys"))).SetApplicationName("PartScout");
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(o =>
{
    o.Cookie.Name = "partscout";
    o.Cookie.HttpOnly = true;
    o.Cookie.SameSite = SameSiteMode.Strict;
    o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest; // http sur le LAN pour le moment
    o.ExpireTimeSpan = TimeSpan.FromDays(30);
    o.SlidingExpiration = true;
    o.Events.OnRedirectToLogin = c => { c.Response.StatusCode = 401; return Task.CompletedTask; };
    o.Events.OnRedirectToAccessDenied = c => { c.Response.StatusCode = 403; return Task.CompletedTask; };
    // Compte supprimé ou mot de passe changé : la session tombe aussitôt, sans attendre la fin du cookie.
    o.Events.OnValidatePrincipal = async c =>
    {
        var user = long.TryParse(c.Principal?.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
            ? c.HttpContext.RequestServices.GetRequiredService<Db>().GetUser(id) : null;
        if (user == null || user.Stamp != c.Principal?.FindFirstValue(StampClaim))
        {
            c.RejectPrincipal();
            await c.HttpContext.SignOutAsync();
        }
    };
});
builder.Services.AddAuthorization();
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = 429;
    o.AddPolicy("login", c => RateLimitPartition.GetFixedWindowLimiter(c.Connection.RemoteIpAddress?.ToString() ?? "?",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1) }));
});

var app = builder.Build();

if (trustedProxy != null) app.UseForwardedHeaders();

app.Use(async (c, next) =>
{
    var h = c.Response.Headers;
    h["X-Content-Type-Options"] = "nosniff";
    h["Referrer-Policy"] = "no-referrer";
    h["Content-Security-Policy"] = "default-src 'self'; img-src 'self' https: data:; style-src 'self' 'unsafe-inline'; connect-src 'self'; frame-ancestors 'none'; base-uri 'none'; form-action 'self'";
    await next();
});
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/healthz", () => Results.Ok(new { status = "ok" }));

// ---- Connexion : le premier compte, créé au premier démarrage, est administrateur et crée les autres ----
var hasher = new PasswordHasher<string>();
// Pour un nom inconnu, on vérifie quand même une empreinte : le temps de réponse ne révèle pas quels comptes existent.
var decoyHash = hasher.HashPassword("", Db.NewStamp());

static async Task SignIn(HttpContext c, long id, string name, string stamp)
{
    var identity = new ClaimsIdentity(new[]
    {
        new Claim(ClaimTypes.NameIdentifier, id.ToString()), new Claim(ClaimTypes.Name, name), new Claim(StampClaim, stamp)
    }, CookieAuthenticationDefaults.AuthenticationScheme);
    await c.SignInAsync(new ClaimsPrincipal(identity), new AuthenticationProperties { IsPersistent = true });
}

static IResult Bad(string key, string message) => Results.BadRequest(new { messageKey = key, message });
static string CleanName(string? name) => (name ?? "").Trim();
static bool ValidName(string name) => name.Length is >= 2 and <= 40 && name.All(ch => char.IsLetterOrDigit(ch) || ch is '.' or '_' or '-');
static bool ValidPassword(string? password) => (password ?? "").Length >= 10;
const string NameRule = "Name: 2 to 40 letters, digits, dots, dashes or underscores.";
const string PasswordRule = "Password: at least 10 characters.";

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

var api = app.MapGroup("/api").RequireAuthorization();

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

// ---- Recherche ----
api.MapPost("/search", async (SearchRequest r, HttpContext c, SearchService search, Db db) =>
{
    var query = (r.Query ?? "").Trim();
    if (query.Length is 0 or > 200) { c.Response.StatusCode = 400; return; }
    db.AddHistory(c.UserId(), query);
    c.Response.ContentType = "application/x-ndjson";
    c.Response.Headers["X-Accel-Buffering"] = "no"; // nginx ne doit pas retenir le flux
    c.Response.Headers.CacheControl = "no-store";
    await c.Response.StartAsync(c.RequestAborted);
    try
    {
        await foreach (var e in search.For(c.UserId()).StreamAsync(r with { Query = query }, c.RequestAborted))
        {
            await JsonSerializer.SerializeAsync(c.Response.Body, e, Json.Options, c.RequestAborted);
            await c.Response.Body.WriteAsync("\n"u8.ToArray(), c.RequestAborted);
            await c.Response.Body.FlushAsync(c.RequestAborted);
        }
    }
    catch (OperationCanceledException) when (c.RequestAborted.IsCancellationRequested)
    {
        // Le navigateur a annulé : les appels en cours sont déjà arrêtés.
    }
});
api.MapPost("/search/more", (MoreRequest r, SearchService s, HttpContext c) => s.For(c.UserId()).MoreAsync(r, c.RequestAborted));

api.MapGet("/suppliers", (SettingsService settings, HttpContext c, Db db) =>
{
    var s = settings.For(c.UserId());
    var calls = db.CallsToday(c.UserId());
    return s.Suppliers.Select(x => new
    {
        name = x.Name,
        kind = x is ApiSupplier ? "api" : "link",
        configured = x.IsConfigured,
        enabled = s.Current.IsEnabled(x.Name),
        canLoadMore = x is ApiSupplier,
        callsToday = calls.GetValueOrDefault(x.Name)
    });
});
api.MapPost("/suppliers/{name}/test", async (string name, SearchService s, HttpContext c) =>
{
    var (ok, messageKey, message, count) = await s.For(c.UserId()).TestAsync(name, c.RequestAborted);
    return new { ok, messageKey, message, count };
});

api.MapGet("/history", (HttpContext c, Db db) => db.GetHistory(c.UserId(), 30));
api.MapDelete("/history", (HttpContext c, Db db) => { db.ClearHistory(c.UserId()); return Results.NoContent(); });
api.MapGet("/rates", (RatesService r, HttpContext c) => r.GetAsync(c.RequestAborted));

// ---- Réglages : les clés ne ressortent jamais, seulement « renseignée ou non » ----
api.MapGet("/settings", (SettingsService settings, HttpContext c, AppOptions o) =>
{
    var s = settings.For(c.UserId());
    return new
    {
        settings = s.Current,
        secretsSet = o.Demo ? AppSettings.SecretNames.ToDictionary(n => n, _ => false) : s.SecretsSet(),
        unreadableSecrets = s.UnreadableSecrets,
        searchUrls = s.Suppliers.ToDictionary(x => x.Name, x => x.SearchUrlTemplate)
    };
});
api.MapPut("/settings", (AppSettings incoming, SettingsService s, HttpContext c) =>
{
    foreach (var (name, url) in incoming.SearchUrls.Select(kv => (kv.Key, kv.Value)).Concat(incoming.CustomSuppliers.Select(c => (c.Name, c.SearchUrl))))
    {
        if (string.IsNullOrWhiteSpace(url)) continue;
        if (Safe.Url(url.Replace("{0}", "x")).Length == 0 || !url.Contains("{0}"))
            return Results.BadRequest(new { messageKey = "badSearchUrl", name, message = $"{name}: the address must start with http(s):// and contain {{0}}." });
    }
    s.For(c.UserId()).Save(incoming);
    return Results.NoContent();
});
api.MapPut("/settings/secrets", (Dictionary<string, string> values, SettingsService s, HttpContext c, AppOptions o) =>
{
    if (o.Demo) return Results.BadRequest(new { messageKey = "demoNoKeys", message = "Demo mode: no keys are saved." });
    s.For(c.UserId()).SaveSecrets(values);
    return Results.NoContent();
});
api.MapGet("/meta", (AppOptions o) => new
{
    demo = o.Demo,
    version = typeof(Program).Assembly.GetName().Version?.ToString(3),
    digiKeySites = DigiKeySupplier.Sites,
    digiKeyCurrencies = DigiKeySupplier.Currencies,
    farnellStores = Element14Supplier.FarnellStores.Select(x => new { x.Id, x.Country, x.Currency }),
    tmeCountries = TmeSupplier.Countries,
    tmeCurrencies = TmeSupplier.Currencies,
    convertCurrencies = ExchangeRates.Currencies
});

// ---- BOM ----
static Bom CleanBom(Bom b)
{
    b.Name = (b.Name ?? "").Trim();
    b.Notes ??= "";
    foreach (var l in b.Lines)
    {
        l.Qty = Math.Max(1, l.Qty);
        l.ProductUrl = Safe.Url(l.ProductUrl);
    }
    return b;
}
static string BomJson(Bom b) => JsonSerializer.Serialize(b, Json.Options);

api.MapGet("/boms", (HttpContext c, Db db) => db.ListBoms(c.UserId()).Select(b => new { b.Id, b.Name, b.UpdatedUtc }));
api.MapPost("/boms", (Bom b, HttpContext c, Db db) =>
{
    CleanBom(b);
    return Results.Ok(new { id = db.InsertBom(c.UserId(), b.DisplayName, BomJson(b)) });
});
api.MapGet("/boms/{id:long}", (long id, HttpContext c, Db db) =>
{
    var doc = db.GetBom(c.UserId(), id);
    return doc == null ? Results.NotFound() : Results.Text(doc, "application/json");
});
api.MapPut("/boms/{id:long}", (long id, Bom b, HttpContext c, Db db) =>
{
    CleanBom(b);
    return db.UpdateBom(c.UserId(), id, b.DisplayName, BomJson(b)) ? Results.NoContent() : Results.NotFound();
});
api.MapDelete("/boms/{id:long}", (long id, HttpContext c, Db db) => db.DeleteBom(c.UserId(), id) ? Results.NoContent() : Results.NotFound());
api.MapGet("/boms/{id:long}/csv", (long id, HttpContext c, Db db) =>
{
    var doc = db.GetBom(c.UserId(), id);
    if (doc == null) return Results.NotFound();
    var bom = JsonSerializer.Deserialize<Bom>(doc, Json.Options)!;
    // Avec l'en-tête UTF-8, comme l'app d'origine, pour que les tableurs affichent bien les symboles.
    var bytes = new System.Text.UTF8Encoding(true).GetPreamble().Concat(System.Text.Encoding.UTF8.GetBytes(bom.ToCsv())).ToArray();
    var safeName = string.Concat(bom.DisplayName.Select(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' ? ch : '_'));
    return Results.File(bytes, "text/csv; charset=utf-8", safeName + ".csv");
});
api.MapPost("/boms/import", (ImportRequest r, HttpContext c, Db db) =>
{
    var bom = CleanBom(Bom.Parse(r.Csv ?? ""));
    if (bom.Lines.Count == 0) return Results.BadRequest(new { messageKey = "bomNoLines", message = "No rows recognised in this file." });
    if (bom.Name.Length == 0) bom.Name = (r.Name ?? "").Trim();
    return Results.Ok(new { id = db.InsertBom(c.UserId(), bom.DisplayName, BomJson(bom)), lines = bom.Lines.Count });
});

app.Map("/api/{**rest}", () => Results.NotFound());
app.MapFallbackToFile("index.html");
await app.RunAsync();
return 0;

public sealed record SetupRequest(string? Password, string? Token, string? Name);
public sealed record LoginRequest(string? Name, string? Password);
public sealed record LanguageRequest(string? Lang);
public sealed record ChangePasswordRequest(string? Current, string? Password);
public sealed record CreateUserRequest(string? Name, string? Password, bool IsAdmin = false, string? Lang = null);
public sealed record SetPasswordRequest(string? Password);
public sealed record ImportRequest(string? Csv, string? Name);
public partial class Program
{
    internal const string StampClaim = "stamp";
}

internal static class HttpContextUser
{
    /// <summary>L'identifiant du compte connecté. La session a été vérifiée en base, voir OnValidatePrincipal.</summary>
    public static long UserId(this HttpContext c) => long.TryParse(c.User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : 0;
}
