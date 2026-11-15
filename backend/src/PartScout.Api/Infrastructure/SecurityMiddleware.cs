using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Identity;
using PartScout.Api.Infrastructure;
using PartScout.Api.Services;
using PartScout.Suppliers;

namespace PartScout.Api.Infrastructure;

/// <summary>En-têtes de sécurité, taille des corps et protection CSRF, pour toutes les requêtes.</summary>
internal static class SecurityMiddleware
{
    public static void UseSecurity(this WebApplication app)
    {
        app.Use(async (c, next) =>
        {
            var h = c.Response.Headers;
            h["X-Content-Type-Options"] = "nosniff";
            h["Referrer-Policy"] = "no-referrer";
            h["Content-Security-Policy"] = "default-src 'self'; img-src 'self' https: data:; style-src 'self' 'unsafe-inline'; connect-src 'self'; frame-ancestors 'none'; base-uri 'none'; form-action 'self'";
            await next();
        });

        // Taille des corps : petite partout, plus large pour les BOM (import CSV). Content-Length est vérifié tout de suite ;
        // sans lui (envoi par morceaux), Kestrel coupe à la limite donnée ici.
        app.Use(async (c, next) =>
        {
            if (c.Request.Path.StartsWithSegments("/api"))
            {
                var limit = c.Request.Path.StartsWithSegments("/api/boms") ? BodyLimit.Large : BodyLimit.Small;
                if (c.Request.ContentLength > limit)
                {
                    c.Response.StatusCode = 413;
                    await c.Response.WriteAsJsonAsync(new { messageKey = "tooLarge", message = "Request too large." });
                    return;
                }
                if (c.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } size) size.MaxRequestBodySize = limit;
            }
            await next();
        });

        // CSRF : toute écriture sur l'API doit porter X-PartScout. Un formulaire d'un autre site ne peut pas l'ajouter, et un script
        // d'une autre origine (même un sous-domaine voisin, que SameSite considère comme le même site) déclencherait une requête
        // préalable CORS, que l'app n'accepte jamais.
        app.Use(async (c, next) =>
        {
            if (c.Request.Path.StartsWithSegments("/api") && !HttpMethods.IsGet(c.Request.Method) && !HttpMethods.IsHead(c.Request.Method)
                && c.Request.Headers["X-PartScout"] != "1")
            {
                c.Response.StatusCode = 403;
                await c.Response.WriteAsJsonAsync(new { messageKey = "staleClient", message = "Reload the page." });
                return;
            }
            await next();
        });
    }
}

internal static class BodyLimit
{
    public const long Small = 64 * 1024;
    public const long Large = 1024 * 1024;
}
