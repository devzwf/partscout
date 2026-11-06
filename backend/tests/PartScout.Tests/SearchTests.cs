using System.Net.Http.Json;
using System.Text.Json;

namespace PartScout.Tests;

public class SearchTests
{
    private static string[] Mpns(JsonElement page) =>
        page.GetProperty("parts").EnumerateArray().Select(p => p.GetProperty("manufacturerPartNumber").GetString()!).ToArray();

    [Fact]
    public async Task Le_flux_contient_chaque_fournisseur_et_se_termine()
    {
        using var app = await new TestApp().LoggedInAsync();
        var events = await app.SearchAsync(new { query = "LM358" });

        Assert.Equal("started", events[0].Type());
        Assert.Equal("done", events[^1].Type());
        var started = events[0].GetProperty("suppliers").EnumerateArray().Select(e => e.GetString()).ToList();
        Assert.Contains("DigiKey", started);
        Assert.DoesNotContain("Farnell", started); // R.-U. : décoché par défaut au Canada
        foreach (var s in new[] { "DigiKey", "Mouser", "Newark", "TME" }) Assert.NotEmpty(Mpns(events.PageOf(s)));
        Assert.Contains(events.OfType("link"), e => e.GetProperty("supplier").GetString() == "LCSC");
    }

    [Fact]
    public async Task DigiKey_retient_l_emballage_au_plus_petit_MOQ_et_corrige_le_lien_datasheet()
    {
        using var app = await new TestApp().LoggedInAsync();
        var part = (await app.SearchAsync(new { query = "LM358" })).PageOf("DigiKey").GetProperty("parts")[0];
        Assert.Equal("DEMO-LM358DR-CT", part.GetProperty("supplierPartNumber").GetString());
        Assert.Equal(1, part.GetProperty("minOrderQty").GetInt32());
        Assert.Equal("Instrumentation, Op Amps", part.GetProperty("category").GetString());
        Assert.StartsWith("https://", part.GetProperty("datasheetUrl").GetString());
    }

    [Fact]
    public async Task Les_parseurs_lisent_prix_textes_et_champs_particuliers()
    {
        using var app = await new TestApp().LoggedInAsync();
        var events = await app.SearchAsync(new { query = "LM358" });

        var mouser = events.PageOf("Mouser").GetProperty("parts")[0];
        Assert.Equal(0.59m, mouser.GetProperty("priceBreaks")[0].GetProperty("price").GetDecimal()); // « CA$0.59 »
        Assert.Equal("CAD", mouser.GetProperty("currency").GetString());

        var newark = events.PageOf("Newark").GetProperty("parts")[0];
        Assert.Equal("Operational Amplifier", newark.GetProperty("category").GetString()); // tiré du nom
        Assert.Equal(5, newark.GetProperty("minOrderQty").GetInt32()); // champ mal orthographié par l'API
        Assert.Equal("USD", newark.GetProperty("currency").GetString());

        var tme = events.PageOf("TME").GetProperty("parts")[0];
        Assert.Equal("SO8", tme.GetProperty("package").GetString()); // tiré de la description
        Assert.Equal(2450, tme.GetProperty("stock").GetInt64()); // deuxième appel : prix et stock
    }

    [Fact]
    public async Task Une_panne_n_empeche_pas_les_autres_et_garde_le_lien()
    {
        using var app = await new TestApp().LoggedInAsync();
        var events = await app.SearchAsync(new { query = "panne" });

        var error = events.OfType("error").Single();
        Assert.Equal("TME", error.GetProperty("supplier").GetString());
        Assert.Contains("503", error.GetProperty("message").GetString());
        Assert.StartsWith("https://", error.GetProperty("url").GetString());
        Assert.NotEmpty(Mpns(events.PageOf("DigiKey")));
        Assert.Equal("done", events[^1].Type());
    }

    [Fact]
    public async Task Le_cache_evite_un_second_appel_sauf_si_on_actualise()
    {
        using var app = await new TestApp().LoggedInAsync();
        Assert.False((await app.SearchAsync(new { query = "LM358" })).PageOf("DigiKey").GetProperty("cached").GetBoolean());
        Assert.True((await app.SearchAsync(new { query = "lm358" })).PageOf("DigiKey").GetProperty("cached").GetBoolean());
        Assert.False((await app.SearchAsync(new { query = "LM358", refresh = true })).PageOf("DigiKey").GetProperty("cached").GetBoolean());

        var suppliers = await app.Http.GetFromJsonAsync<JsonElement>("/api/suppliers");
        var count = suppliers.EnumerateArray().First(s => s.GetProperty("name").GetString() == "TME").GetProperty("callsToday").GetInt64();
        Assert.Equal(4, count); // 2 recherches réelles × 2 appels
    }

    [Fact]
    public async Task Un_code_fournisseur_est_retrouve_puis_cherche_ailleurs_par_MPN()
    {
        using var app = await new TestApp().LoggedInAsync();
        var events = await app.SearchAsync(new { query = "595-LM358DR", mode = "SupplierCode" });

        var resolved = events.OfType("codeResolved").Single();
        Assert.Equal("LM358DR", resolved.GetProperty("mpn").GetString());
        // DigiKey ne reconnaît pas le code : il est réinterrogé sur le MPN exact.
        Assert.Contains(events.OfType("page"), e =>
            e.GetProperty("supplier").GetString() == "DigiKey" && e.GetProperty("page").GetProperty("mode").GetString() == "ExactPartNumber" &&
            Mpns(e.GetProperty("page")).SequenceEqual(new[] { "LM358DR" }));
    }

    [Fact]
    public async Task Charger_plus_renvoie_la_page_suivante()
    {
        using var app = await new TestApp().LoggedInAsync();
        var first = (await app.SearchAsync(new { query = "LM358", maxResults = 5 })).PageOf("DigiKey");
        Assert.True(first.GetProperty("hasMore").GetBoolean());
        Assert.Equal(5, first.GetProperty("nextOffset").GetInt32());

        var res = await app.Http.PostAsJsonAsync("/api/search/more", new { supplier = "DigiKey", query = "LM358", mode = "Keyword", offset = 5, pageSize = 5 });
        var more = (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("page");
        Assert.Equal(new[] { "LM358DRG4" }, Mpns(more));
        Assert.False(more.GetProperty("hasMore").GetBoolean());
    }

    [Fact]
    public async Task Une_recherche_vide_est_refusee_et_l_historique_se_remplit()
    {
        using var app = await new TestApp().LoggedInAsync();
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, (await app.Http.PostAsJsonAsync("/api/search", new { query = "   " })).StatusCode);
        await app.SearchAsync(new { query = "LM358" });
        await app.SearchAsync(new { query = "NE555" });
        await app.SearchAsync(new { query = "lm358" });
        var history = await app.Http.GetFromJsonAsync<string[]>("/api/history");
        Assert.Equal(2, history!.Length); // sans doublon, le plus récent d'abord
        Assert.Equal("NE555", history[1]);
    }

    [Fact]
    public async Task Les_taux_sont_servis()
    {
        using var app = await new TestApp().LoggedInAsync();
        var rates = await app.Http.GetFromJsonAsync<JsonElement>("/api/rates");
        Assert.Equal(1.52m, rates.GetProperty("perEuro").GetProperty("CAD").GetDecimal());
    }
}
