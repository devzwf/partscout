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

/// <summary>Réglages : les clés ne ressortent jamais, seulement « renseignée ou non ».</summary>
internal static class SettingsEndpoints
{
    public static void Map(RouteGroupBuilder api)
    {
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
    }
}
