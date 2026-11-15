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

/// <summary>Listes de composants (BOM) : propres à chaque compte.</summary>
internal static class BomEndpoints
{
    private static Bom CleanBom(Bom b)
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
    private static string BomJson(Bom b) => JsonSerializer.Serialize(b, Json.Options);

    public static void Map(RouteGroupBuilder api)
    {
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
    }
}

public sealed record ImportRequest(string? Csv, string? Name);
