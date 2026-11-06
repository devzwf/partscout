using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using PartScout.Suppliers;

namespace PartScout.Tests;

public class BomTests
{
    // En-têtes écrits par l'app d'origine (Models/Bom.cs).
    private const string UpstreamCsv = "\"Refs\",\"Value\",\"Footprint\",\"Qty\",\"DNP\",\"Supplier\",\"Supplier Part No\",\"MPN\",\"Manufacturer\",\"Description\",\"Unit Price\",\"Currency\",\"Price Breaks\",\"Product URL\",\"Stock\",\"Date Added\"\n" +
        "\"U1,U2\",\"LM358DR\",\"8-SOIC\",\"12\",\"\",\"DigiKey\",\"296-1395-1-ND\",\"LM358DR\",\"Texas Instruments\",\"Ampli, double\",\"0.62\",\"CAD\",\"1:0.62;10:0.41\",\"https://www.digikey.ca/x\",\"48210\",\"2026-11-04\"\n" +
        "\"R1\",\"10k\",\"0805\",\"1\",\"DNP\",\"Mouser\",\"71-CRCW\",\"CRCW080510K\",\"Vishay\",\"\",\"\",\"\",\"\",\"\",\"\",\"\"\n" +
        "\n\"#BOM Name\",\"Alim labo\"\n\"#Notes\",\"Une note\"\n";

    [Fact]
    public void Un_CSV_de_l_app_d_origine_se_lit_et_se_reecrit_sans_perte()
    {
        var bom = Bom.Parse(UpstreamCsv);
        Assert.Equal("Alim labo", bom.Name);
        Assert.Equal("Une note", bom.Notes);
        Assert.Equal(2, bom.Lines.Count);
        var l = bom.Lines[0];
        Assert.Equal(("U1,U2", 12, "Ampli, double", 0.62m), (l.Refs, l.Qty, l.Description, l.UnitPrice));
        Assert.Equal(0.41m, l.PriceEach()); // palier de 10 pour 12 pièces
        Assert.False(bom.Lines[1].Include);

        var again = Bom.Parse(bom.ToCsv());
        Assert.Equal(JsonSerializer.Serialize(bom), JsonSerializer.Serialize(again));
        Assert.StartsWith("\"Refs\",\"Value\",\"Footprint\",\"Qty\",\"DNP\"", bom.ToCsv());
    }

    [Theory]
    [InlineData("=cmd()", "'=cmd()")]
    [InlineData("@SUM(A1)", "'@SUM(A1)")]
    [InlineData("+1+1", "'+1+1")]
    [InlineData("-2+3", "'-2+3")]
    [InlineData("-40", "-40")]
    [InlineData("LM358", "LM358")]
    public void Les_formules_sont_neutralisees_a_l_export(string input, string expected)
    {
        var bom = new Bom { Lines = { new BomLine { Description = input } } };
        Assert.Equal(expected, Bom.Parse(bom.ToCsv()).Lines[0].Description);
    }

    [Fact]
    public async Task Cycle_complet_par_l_API()
    {
        using var app = await new TestApp().LoggedInAsync();
        var created = await (await app.Http.PostAsJsonAsync("/api/boms/import", new { csv = UpstreamCsv })).Content.ReadFromJsonAsync<JsonElement>();
        var id = created.GetProperty("id").GetInt64();
        Assert.Equal(2, created.GetProperty("lines").GetInt32());

        var bom = await app.Http.GetFromJsonAsync<JsonElement>($"/api/boms/{id}");
        Assert.Equal("Alim labo", bom.GetProperty("name").GetString());
        Assert.Equal(2, bom.GetProperty("lines")[0].GetProperty("priceBreaks").GetArrayLength());

        var update = new { name = "Renommée", notes = "n", lines = new[] { new { qty = 0, supplier = "S", supplierPartNumber = "P", productUrl = "javascript:alert(1)" } } };
        Assert.Equal(HttpStatusCode.NoContent, (await app.Http.PutAsJsonAsync($"/api/boms/{id}", update)).StatusCode);
        var line = (await app.Http.GetFromJsonAsync<JsonElement>($"/api/boms/{id}")).GetProperty("lines")[0];
        Assert.Equal(1, line.GetProperty("qty").GetInt32()); // quantité ramenée à 1
        Assert.Equal("", line.GetProperty("productUrl").GetString()); // lien dangereux retiré

        var csv = await app.Http.GetAsync($"/api/boms/{id}/csv");
        var bytes = await csv.Content.ReadAsByteArrayAsync();
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes[..3]); // en-tête UTF-8 pour les tableurs
        Assert.Contains("Renomm", (await app.Http.GetFromJsonAsync<JsonElement>("/api/boms")).ToString());

        Assert.Equal(HttpStatusCode.NoContent, (await app.Http.DeleteAsync($"/api/boms/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await app.Http.GetAsync($"/api/boms/{id}")).StatusCode);
    }

    [Fact]
    public async Task Un_fichier_sans_ligne_est_refuse()
    {
        using var app = await new TestApp().LoggedInAsync();
        Assert.Equal(HttpStatusCode.BadRequest, (await app.Http.PostAsJsonAsync("/api/boms/import", new { csv = "" })).StatusCode);
    }
}
