using System.Net;
using System.Text;
using System.Text.Json;

namespace PartScout.Api.Demo;

/// <summary>
/// Mode démo et tests : répond à la place des fournisseurs, au format que leurs parseurs attendent.
/// Ces réponses sont écrites d'après le code des parseurs et NE SONT PAS des captures d'API réelles. Tout est fictif.
/// Une requête contenant « panne » fait échouer TME, « rien » ne renvoie aucun résultat.
/// </summary>
public sealed class DemoHandler : HttpMessageHandler
{
    public const string FakeSecret = "demo-secret-0123456789";

    /// <summary>Attente simulée, pour voir les résultats arriver l'un après l'autre. 0 dans les tests.</summary>
    public bool Slow { get; init; } = true;

    private static readonly (string Mpn, string Mfr, string Type, string Pkg, string Desc, string Status, long Stock)[] Catalog =
    {
        ("LM358DR", "Texas Instruments", "Instrumentation, Op Amps", "8-SOIC", "IC OPAMP GP 2 CIRCUIT 8SOIC", "Active", 48210),
        ("LM358N", "Texas Instruments", "Instrumentation, Op Amps", "8-PDIP", "IC OPAMP GP 2 CIRCUIT 8DIP", "Active", 15340),
        ("LM358PWR", "Texas Instruments", "Instrumentation, Op Amps", "8-TSSOP", "IC OPAMP GP 2 CIRCUIT 8TSSOP", "Active", 6120),
        ("LM358DT", "STMicroelectronics", "Instrumentation, Op Amps", "8-SO", "IC OPAMP GP 2 CIRCUIT 8SO", "Not For New Designs", 4310),
        ("LM358AN/NOPB", "Texas Instruments", "Instrumentation, Op Amps", "8-PDIP", "IC OPAMP GP 2 CIRCUIT 8DIP", "Obsolete", 0),
    };

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var url = request.RequestUri!;
        var body = request.Content == null ? "" : await request.Content.ReadAsStringAsync(ct);
        var all = Uri.UnescapeDataString(url.Query) + " " + body;
        if (Slow) await Task.Delay(Delay(url.Host), ct);
        if (url.Host == "api.tme.eu" && all.Contains("panne", StringComparison.OrdinalIgnoreCase))
            return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { Content = new StringContent("Service down (demo)") };
        var none = all.Contains("rien", StringComparison.OrdinalIgnoreCase);
        object reply = url.Host switch
        {
            "api.digikey.com" => url.AbsolutePath.EndsWith("token") ? new { access_token = "demo", expires_in = 600 } : DigiKey(none, body),
            "api.mouser.com" => Mouser(none),
            "api.element14.com" => Element14(none, url.Query),
            "api.tme.eu" => Tme(url.AbsolutePath, none, all),
            _ when url.Host.Contains("frankfurter") => new { @base = "EUR", date = "2026-11-03", rates = new Dictionary<string, decimal> { ["CAD"] = 1.52m, ["USD"] = 1.08m, ["GBP"] = 0.86m, ["PLN"] = 4.3m } },
            _ => throw new HttpRequestException("Demo: unexpected host " + url.Host)
        };
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(reply), Encoding.UTF8, "application/json") };
    }

    private static int Delay(string host) => host switch { "api.digikey.com" => 300, "api.mouser.com" => 700, "api.element14.com" => 1200, "api.tme.eu" => 900, _ => 0 };

    private static object DigiKey(bool none, string body)
    {
        using var doc = JsonDocument.Parse(body.Length > 0 ? body : "{}");
        var offset = doc.RootElement.TryGetProperty("Offset", out var o) ? o.GetInt32() : 0;
        var products = none ? Array.Empty<object>() : (offset == 0 ? Catalog : Catalog.Take(0).Append(("LM358DRG4", "Texas Instruments", "Instrumentation, Op Amps", "8-SOIC", "IC OPAMP GP 2 CIRCUIT 8SOIC (page 2)", "Active", 2100L)).ToArray())
            .Select((p, i) => (object)new
            {
                ManufacturerProductNumber = p.Item1,
                Manufacturer = new { Name = p.Item2 },
                Description = new { ProductDescription = p.Item5 },
                Category = new { Name = "Integrated Circuits (ICs)", ChildCategories = new[] { new { Name = p.Item3, ChildCategories = Array.Empty<object>() } } },
                Parameters = new[] { new { ParameterText = "Supplier Device Package", ValueText = p.Item4 } },
                QuantityAvailable = p.Item7,
                ProductStatus = new { Status = p.Item6 },
                ProductUrl = "https://www.digikey.ca/en/products/detail/demo/" + Uri.EscapeDataString(p.Item1),
                DatasheetUrl = "//www.ti.com/lit/ds/symlink/lm358.pdf",
                PhotoUrl = "",
                ManufacturerLeadWeeks = "6",
                // Deux emballages : le parseur doit retenir celui au plus petit MOQ.
                ProductVariations = new object[]
                {
                    new { DigiKeyProductNumber = "DEMO-" + p.Item1 + "-TR", MinimumOrderQuantity = 2500, PackageType = new { Name = "Tape & Reel (TR)" }, StandardPricing = new[] { new { BreakQuantity = 2500, UnitPrice = 0.18m } } },
                    new { DigiKeyProductNumber = "DEMO-" + p.Item1 + "-CT", MinimumOrderQuantity = 1, PackageType = new { Name = "Cut Tape (CT)" }, StandardPricing = new[] { new { BreakQuantity = 1, UnitPrice = 0.62m + i * 0.1m }, new { BreakQuantity = 10, UnitPrice = 0.41m + i * 0.1m }, new { BreakQuantity = 100, UnitPrice = 0.27m + i * 0.1m } } },
                }
            }).ToArray();
        return new { ProductsCount = none ? 0 : 6, ExactMatches = Array.Empty<object>(), Products = products };
    }

    private static object Mouser(bool none)
    {
        var parts = none ? Array.Empty<object>() : Catalog.Take(4).Select((p, i) => (object)new
        {
            Availability = p.Item7 > 0 ? $"{p.Item7 / 2:N0} In Stock" : "None",
            AvailabilityInStock = (p.Item7 / 2).ToString(),
            LeadTime = "42 Days",
            MouserPartNumber = "595-" + p.Item1,
            ManufacturerPartNumber = p.Item1,
            Manufacturer = p.Item2,
            Description = "Operational Amplifiers - Op Amps " + p.Item5,
            Category = "Operational Amplifiers - Op Amps",
            Min = "1",
            LifecycleStatus = p.Item6 == "Active" ? "" : p.Item6,
            ProductDetailUrl = "https://www.mouser.ca/ProductDetail/demo/" + Uri.EscapeDataString(p.Item1),
            DataSheetUrl = "https://www.ti.com/lit/ds/symlink/lm358.pdf",
            PriceBreaks = new[] { new { Price = $"CA${0.59m + i * 0.09m:0.00}", Quantity = 1, Currency = "CAD" }, new { Price = $"CA${0.43m + i * 0.09m:0.00}", Quantity = 10, Currency = "CAD" }, new { Price = $"CA${0.26m + i * 0.09m:0.00}", Quantity = 100, Currency = "CAD" } }
        }).ToArray();
        return new { Errors = Array.Empty<object>(), SearchResults = new { NumberOfResult = parts.Length, Parts = parts } };
    }

    private static object Element14(bool none, string query)
    {
        var store = query.Contains("newark") ? "newark" : "farnell";
        var products = none ? Array.Empty<object>() : Catalog.Take(3).Select((p, i) => (object)new
        {
            sku = store == "newark" ? "73K012" + i : "24601" + i,
            stock = new { level = p.Item7 / 5, leastLeadTime = "42" },
            translatedMinimumOrderQuality = i == 0 ? 5 : 1,
            translatedManufacturerPartNumber = p.Item1,
            brandName = p.Item2,
            displayName = $"{p.Item2.ToUpperInvariant()} - {p.Item1} - Operational Amplifier, Dual, 700 kHz",
            productStatus = "STOCKED",
            prices = new[] { new { from = 1, cost = 0.45m + i * 0.1m }, new { from = 25, cost = 0.29m + i * 0.1m } },
            datasheets = new[] { new { url = "https://www.farnell.com/datasheets/demo.pdf" } },
            attributes = new[] { new { attributeLabel = "IC Case / Package", attributeValue = p.Item4 } }
        }).ToArray();
        return new { keywordSearchReturn = new { numberOfResults = products.Length, products } };
    }

    private static object Tme(string path, bool none, string all)
    {
        if (path.EndsWith("/auth/token")) return new { access_token = "demo", expires_in = 300 };
        if (path.EndsWith("/products/search"))
        {
            var elements = none ? Array.Empty<object>() : Catalog.Take(2).Select(p => (object)new
            {
                symbol = p.Item1,
                manufacturer_symbols = new[] { p.Item1 },
                manufacturer = new { name = p.Item2 },
                description = "IC: operational amplifier; 700kHz; SO8; 3÷32VDC",
                minimal_amount = 10,
                product_status = new[] { "AVAILABLE" }
            }).ToArray();
            return new { status = "OK", data = new { counters = new { count = elements.Length }, products = new { elements } } };
        }
        return new
        {
            status = "OK",
            data = new
            {
                elements = Catalog.Take(2).Select(p => new
                {
                    symbol = p.Item1,
                    stock_quantity = 2450,
                    prices = new { currency = "USD", type = "NET", elements = new[] { new { amount = 10, price = 0.24m }, new { amount = 100, price = 0.17m } } }
                })
            }
        };
    }
}
