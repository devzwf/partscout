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

/// <summary>Recherche, fournisseurs, historique et taux de change.</summary>
internal static class SearchEndpoints
{
    public static void Map(RouteGroupBuilder api)
    {
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
    }
}
