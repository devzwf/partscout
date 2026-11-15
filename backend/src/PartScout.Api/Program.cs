using System.Security.Claims;
using System.Text.Json;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PartScout.Api.Demo;
using PartScout.Api.Endpoints;
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
// Sans ce contrôle, un dossier hôte appartenant à root se solde par une erreur SQLite incompréhensible.
try
{
    Directory.CreateDirectory(options.DataDir);
    using var writeProbe = new FileStream(Path.Combine(options.DataDir, $".write-test-{Guid.NewGuid():N}"), FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose);
}
catch (Exception e) when (e is UnauthorizedAccessException or IOException)
{
    Console.Error.WriteLine($"PartScout cannot write to its data folder ({options.DataDir}): {e.Message}");
    Console.Error.WriteLine("On a host folder, give it to the user running the container (uid 1654 by default): chown -R 1654:1654 /path/to/data");
    return 1;
}

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

// Aucun corps de requête légitime n'approche cette taille (le plus gros : un import CSV de BOM). Voir aussi BodyLimit plus bas.
builder.WebHost.ConfigureKestrel(k => k.Limits.MaxRequestBodySize = BodyLimit.Large);

builder.Services.AddSingleton(options);
builder.Services.TryAddSingleton(TimeProvider.System);
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
    // auto : Secure dès que la requête est en HTTPS (derrière le proxy, via X-Forwarded-Proto) ; HTTP simple sur le réseau local possible.
    o.Cookie.SecurePolicy = options.CookieAlwaysSecure ? CookieSecurePolicy.Always : CookieSecurePolicy.SameAsRequest;
    o.ExpireTimeSpan = TimeSpan.FromDays(30);
    o.SlidingExpiration = true;
    o.Events.OnRedirectToLogin = c => { c.Response.StatusCode = 401; return Task.CompletedTask; };
    o.Events.OnRedirectToAccessDenied = c => { c.Response.StatusCode = 403; return Task.CompletedTask; };
    // Compte supprimé ou mot de passe changé : la session tombe aussitôt, sans attendre la fin du cookie.
    // Et quel que soit l'usage, une connexion ne dure pas plus de SessionMaxAge : l'expiration glissante seule la prolongerait sans fin.
    o.Events.OnValidatePrincipal = async c =>
    {
        var user = long.TryParse(c.Principal?.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
            ? c.HttpContext.RequestServices.GetRequiredService<Db>().GetUser(id) : null;
        var now = c.HttpContext.RequestServices.GetRequiredService<TimeProvider>().GetUtcNow();
        var tooOld = !long.TryParse(c.Principal?.FindFirstValue(SignedInClaim), out var signedIn)
            || now - DateTimeOffset.FromUnixTimeSeconds(signedIn) > SessionMaxAge;
        if (user == null || user.Stamp != c.Principal?.FindFirstValue(StampClaim) || tooOld)
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

app.UseSecurity();

app.UseDefaultFiles();
app.UseStaticFiles();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/healthz", () => Results.Ok(new { status = "ok" }));

var api = app.MapGroup("/api").RequireAuthorization();
AuthEndpoints.Map(app, api);
SearchEndpoints.Map(api);
SettingsEndpoints.Map(api);
BomEndpoints.Map(api);

app.Map("/api/{**rest}", () => Results.NotFound());
app.MapFallbackToFile("index.html");
await app.RunAsync();
return 0;

public partial class Program
{
    internal const string StampClaim = "stamp";
    internal const string SignedInClaim = "signed_in";
    /// <summary>Durée maximale d'une connexion, même utilisée tous les jours. Changer son mot de passe la renouvelle.</summary>
    internal static readonly TimeSpan SessionMaxAge = TimeSpan.FromDays(90);
}

internal static class HttpContextUser
{
    /// <summary>L'identifiant du compte connecté. La session a été vérifiée en base, voir OnValidatePrincipal.</summary>
    public static long UserId(this HttpContext c) => long.TryParse(c.User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : 0;
}
