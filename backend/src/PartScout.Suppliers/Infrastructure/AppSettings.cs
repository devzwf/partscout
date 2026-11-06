using System;
using System.Collections.Generic;
using System.Linq;

namespace PartScout.Suppliers;

/// <summary>
/// PartScout: version serveur des réglages de l'app d'origine. Mêmes noms de propriétés pour que Supplier.cs reste
/// identique, mais sans fichiers ni fenêtres, et avec des valeurs par défaut pour le Canada. Le stockage est dans l'API.
/// </summary>
public class AppSettings
{
    public const string RS = "RS Components";
    public const string Farnell = "Farnell";
    public const string CPC = "CPC";
    public const string DigiKey = "DigiKey";
    public const string Mouser = "Mouser";
    public const string Newark = "Newark";
    public const string TME = "TME";
    public const string Rapid = "Rapid";
    public const string LCSC = "LCSC";
    public const string AliExpress = "AliExpress";

    public static readonly string[] SupplierOrder = { DigiKey, Mouser, Newark, TME, LCSC, AliExpress, Farnell, CPC, RS, Rapid };

    /// <summary>Fournisseurs du Royaume-Uni : présents mais décochés par défaut au Canada.</summary>
    public static readonly string[] DisabledByDefault = { Farnell, CPC, RS, Rapid };

    public static readonly Dictionary<string, string> DefaultSearchUrls = new()
    {
        { RS, "https://uk.rs-online.com/web/c/?searchTerm={0}" },
        { Farnell, "https://uk.farnell.com/search?st={0}" },
        { CPC, "https://cpc.farnell.com/search?st={0}" },
        { DigiKey, "https://www.digikey.ca/en/products/result?keywords={0}" },
        { Mouser, "https://www.mouser.ca/c/?q={0}" },
        { Newark, "https://www.newark.com/search?st={0}" },
        { TME, "https://www.tme.eu/en/katalog/?search={0}" },
        { Rapid, "https://www.rapidonline.com/search?query={0}" },
        { LCSC, "https://www.lcsc.com/search?q={0}" },
        { AliExpress, "https://www.aliexpress.com/wholesale?SearchText={0}" }
    };

    /// <summary>Noms des secrets dans le coffre.</summary>
    public static readonly string[] SecretNames =
        { nameof(MouserApiKey), nameof(DigiKeyClientId), nameof(DigiKeyClientSecret), nameof(Element14ApiKey), nameof(TmeToken), nameof(TmeSecret) };

    // Secrets : renseignés par l'API depuis le coffre, jamais sérialisés vers le client.
    [System.Text.Json.Serialization.JsonIgnore] public string MouserApiKey { get; set; } = "";
    [System.Text.Json.Serialization.JsonIgnore] public string DigiKeyClientId { get; set; } = "";
    [System.Text.Json.Serialization.JsonIgnore] public string DigiKeyClientSecret { get; set; } = "";
    [System.Text.Json.Serialization.JsonIgnore] public string Element14ApiKey { get; set; } = "";
    [System.Text.Json.Serialization.JsonIgnore] public string TmeToken { get; set; } = "";
    [System.Text.Json.Serialization.JsonIgnore] public string TmeSecret { get; set; } = "";

    public string DigiKeySite { get; set; } = "CA";
    public string DigiKeyLanguage { get; set; } = "en";
    public string DigiKeyCurrency { get; set; } = "CAD";
    public string FarnellStore { get; set; } = "export.farnell.com";
    public string TmeCountry { get; set; } = "CA";
    public string TmeCurrency { get; set; } = "USD"; // TME ne facture pas en CAD
    public string ConvertCurrency { get; set; } = "CAD";
    public List<CustomSupplier> CustomSuppliers { get; set; } = new();
    public Dictionary<string, bool> EnabledSuppliers { get; set; } = new();
    public Dictionary<string, string> SearchUrls { get; set; } = new();

    public string GetSearchUrl(string supplierName)
    {
        if (SearchUrls.TryGetValue(supplierName, out var url) && !string.IsNullOrWhiteSpace(url)) return url;
        return DefaultSearchUrl(supplierName, FarnellStore);
    }

    public static string DefaultSearchUrl(string supplierName, string? farnellStore)
    {
        if (supplierName == Farnell) return $"https://{Element14Supplier.FindFarnellStore(farnellStore).Id}/search?st={{0}}";
        return DefaultSearchUrls.TryGetValue(supplierName, out var url) ? url : "";
    }

    public bool IsEnabled(string supplierName) =>
        EnabledSuppliers.TryGetValue(supplierName, out var v) ? v : !DisabledByDefault.Contains(supplierName);
}

public class CustomSupplier
{
    public string Name { get; set; } = "";
    public string SearchUrl { get; set; } = "";
}
