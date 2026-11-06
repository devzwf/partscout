using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using PartScout.Api.Infrastructure;

namespace PartScout.Tests;

public class SecretsTests
{
    private const string Key = "CLE-MOUSER-TRES-SECRETE-123";

    [Fact]
    public async Task Une_cle_est_chiffree_au_repos_et_ne_ressort_jamais()
    {
        using var app = await new TestApp(demo: false).LoggedInAsync();
        (await app.Http.PutAsJsonAsync("/api/settings/secrets", new Dictionary<string, string> { ["MouserApiKey"] = Key })).EnsureSuccessStatusCode();

        var settings = await app.Http.GetStringAsync("/api/settings");
        Assert.DoesNotContain(Key, settings);
        Assert.True(JsonDocument.Parse(settings).RootElement.GetProperty("secretsSet").GetProperty("MouserApiKey").GetBoolean());

        foreach (var file in Directory.GetFiles(app.DataDir, "*", SearchOption.AllDirectories))
        {
            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            Assert.DoesNotContain(Key, await reader.ReadToEndAsync());
        }

        // La clé sert bien : Mouser passe du lien web à l'API.
        var events = await app.SearchAsync(new { query = "LM358" });
        Assert.NotEmpty(events.PageOf("Mouser").GetProperty("parts").EnumerateArray());
        Assert.Contains(events.OfType("link"), e => e.GetProperty("supplier").GetString() == "DigiKey"); // sans clé

        // Une valeur vide l'efface.
        await app.Http.PutAsJsonAsync("/api/settings/secrets", new Dictionary<string, string> { ["MouserApiKey"] = "" });
        var after = await app.Http.GetFromJsonAsync<JsonElement>("/api/settings");
        Assert.False(after.GetProperty("secretsSet").GetProperty("MouserApiKey").GetBoolean());
    }

    [Fact]
    public async Task Un_reglage_envoye_avec_une_cle_ne_la_stocke_pas_en_clair()
    {
        using var app = await new TestApp(demo: false).LoggedInAsync();
        await app.Http.PutAsJsonAsync("/api/settings", new { mouserApiKey = Key, digiKeySite = "CA" });
        Assert.DoesNotContain(Key, await app.Http.GetStringAsync("/api/settings"));
    }

    [Theory]
    [InlineData("javascript:alert(1)//{0}")]
    [InlineData("https://exemple.test/sans-marqueur")]
    public async Task Une_adresse_de_recherche_invalide_est_refusee(string url)
    {
        using var app = await new TestApp().LoggedInAsync();
        var res = await app.Http.PutAsJsonAsync("/api/settings", new { customSuppliers = new[] { new { name = "X", searchUrl = url } } });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public void Le_coffre_rejette_mauvaise_cle_et_blob_deplace()
    {
        var vault = new SecretVault(new string('a', 40));
        var blob = vault.Protect("A", "valeur");
        Assert.Equal("valeur", vault.Unprotect("A", blob));
        Assert.Null(vault.Unprotect("B", blob));
        Assert.Null(new SecretVault(new string('b', 40)).Unprotect("A", blob));
        Assert.Throws<InvalidOperationException>(() => new SecretVault("trop-courte"));
    }

    [Fact]
    public void Les_messages_d_erreur_sont_nettoyes()
    {
        var clean = Safe.Scrub("HTTP 401 https://api.mouser.com/x?apiKey=ABC&y=1 callInfo.apiKey=DEF jeton SECRET", new[] { "SECRET" });
        Assert.DoesNotContain("ABC", clean);
        Assert.DoesNotContain("DEF", clean);
        Assert.DoesNotContain("SECRET", clean);
        Assert.Contains("y=1", clean);
    }

    [Theory]
    [InlineData("https://a.test/x", "https://a.test/x")]
    [InlineData("//a.test/x", "https://a.test/x")]
    [InlineData("javascript:alert(1)", "")]
    [InlineData("data:text/html,x", "")]
    [InlineData("ftp://a.test", "")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void Seuls_http_et_https_passent(string? input, string expected) => Assert.Equal(expected, Safe.Url(input));
}
