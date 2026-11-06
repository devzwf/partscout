using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Microsoft.Extensions.Caching.Memory;
using PartScout.Api.Infrastructure;
using PartScout.Suppliers;

namespace PartScout.Api.Services;

public sealed record SearchRequest(string Query, SearchMode Mode = SearchMode.Keyword, int MaxResults = 25,
    bool InStockOnly = false, bool RohsOnly = false, List<string>? Suppliers = null, bool Refresh = false);

public sealed record MoreRequest(string Supplier, string Query, SearchMode Mode, int Offset, int PageSize,
    bool InStockOnly = false, bool RohsOnly = false);

/// <summary>Une page d'un fournisseur, telle qu'envoyée au navigateur.</summary>
public sealed record SupplierPage(string Supplier, string Query, SearchMode Mode, List<PartResult> Parts, int? Total,
    int NextOffset, bool HasMore, long ElapsedMs, bool Cached, DateTime FetchedUtc);

/// <summary>Événements du flux de recherche ; le champ <c>type</c> dit lequel c'est.</summary>
public sealed record SearchEvent(string Type)
{
    public IReadOnlyList<string>? Suppliers { get; init; }
    public string? Supplier { get; init; }
    public SupplierPage? Page { get; init; }
    public string? Url { get; init; }
    public string? Message { get; init; }
    /// <summary>Clé que l'interface traduit ; Message reste le texte en anglais.</summary>
    public string? MessageKey { get; init; }
    public string? Code { get; init; }
    public string? Mpn { get; init; }
    public long? ElapsedMs { get; init; }
}

/// <summary>Logique de recherche sortie de la fenêtre principale de l'app d'origine (RunSearchAsync, SearchByOrderCodeAsync, FetchPageAsync).</summary>
public sealed class SearchService
{
    private readonly SettingsService _settings;
    private readonly IMemoryCache _cache;
    private readonly Db _db;
    private readonly ILogger<SearchService> _log;

    public SearchService(SettingsService settings, IMemoryCache cache, Db db, ILogger<SearchService> log)
    {
        _settings = settings;
        _cache = cache;
        _db = db;
        _log = log;
    }

    public UserSearch For(long user) => new(user, _settings.For(user), _cache, _db, _log);
}

/// <summary>Les recherches d'un utilisateur, avec ses clés, ses réglages et son compteur d'appels.</summary>
public sealed class UserSearch
{
    private static readonly TimeSpan CacheFor = TimeSpan.FromMinutes(15);
    private readonly long _user;
    private readonly UserSettings _settings;
    private readonly IMemoryCache _cache;
    private readonly Db _db;
    private readonly ILogger _log;

    public UserSearch(long user, UserSettings settings, IMemoryCache cache, Db db, ILogger log)
    {
        _user = user;
        _settings = settings;
        _cache = cache;
        _db = db;
        _log = log;
    }

    /// <summary>Fournisseurs à interroger : ceux demandés, ou ceux activés dans les réglages.</summary>
    private List<Supplier> Select(List<string>? names)
    {
        var settings = _settings.Current;
        return _settings.Suppliers
            .Where(s => names is { Count: > 0 } ? names.Contains(s.Name, StringComparer.OrdinalIgnoreCase) : settings.IsEnabled(s.Name))
            .ToList();
    }

    public async IAsyncEnumerable<SearchEvent> StreamAsync(SearchRequest request, [EnumeratorCancellation] CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var selected = Select(request.Suppliers);
        yield return new SearchEvent("started") { Suppliers = selected.Select(s => s.Name).ToList() };

        // Chaque fournisseur écrit dans le canal dès qu'il répond : le plus lent ne retient personne.
        var channel = Channel.CreateUnbounded<SearchEvent>();
        var work = Task.Run(async () =>
        {
            try
            {
                if (request.Mode == SearchMode.SupplierCode) await ByOrderCodeAsync(selected, request, channel.Writer, ct);
                else await Task.WhenAll(selected.Select(s => OneAsync(s, request.Query, request.Mode, request, channel.Writer, ct)));
            }
            finally
            {
                channel.Writer.TryComplete();
            }
        }, CancellationToken.None);

        await foreach (var e in channel.Reader.ReadAllAsync(ct)) yield return e;
        await work;
        yield return new SearchEvent("done") { ElapsedMs = sw.ElapsedMilliseconds };
    }

    public async Task<SearchEvent> MoreAsync(MoreRequest r, CancellationToken ct)
    {
        var s = _settings.Suppliers.FirstOrDefault(x => string.Equals(x.Name, r.Supplier, StringComparison.OrdinalIgnoreCase));
        if (s == null) return new SearchEvent("error") { Supplier = r.Supplier, MessageKey = "supplierUnknown", Message = "Unknown supplier." };
        if (!s.IsConfigured || !s.CanLoadMore(r.Mode)) return new SearchEvent("error") { Supplier = s.Name, MessageKey = "noMorePages", Message = "No more pages for this supplier." };
        try
        {
            var filters = new SupplierFilters { InStockOnly = r.InStockOnly, RohsOnly = r.RohsOnly };
            var page = await FetchAsync(s, r.Query, r.Mode, Clamp(r.PageSize), Math.Max(0, r.Offset), filters, false, ct);
            return new SearchEvent("page") { Supplier = s.Name, Page = page };
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            return new SearchEvent("error") { Supplier = s.Name, Message = Describe(s, ex) };
        }
    }

    /// <summary>Essai minimal d'une clé, depuis la page des réglages. Compte dans le quota, sans passer par le cache.</summary>
    public async Task<(bool Ok, string? MessageKey, string Message, string? Count)> TestAsync(string name, CancellationToken ct)
    {
        var s = _settings.Suppliers.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
        if (s == null) return (false, "supplierUnknown", "Unknown supplier.", null);
        if (!s.IsConfigured) return (false, "keyMissing", "Key missing.", null);
        try
        {
            var page = await FetchAsync(s, "LM358", SearchMode.Keyword, 1, 0, SupplierFilters.None, true, ct);
            var count = page.Total?.ToString() ?? page.Parts.Count.ToString();
            return (true, "testOk", $"Connection OK: {count} result(s) for LM358.", count);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            return (false, null, Describe(s, ex), null);
        }
    }

    private async Task<int> OneAsync(Supplier s, string query, SearchMode mode, SearchRequest r, ChannelWriter<SearchEvent> w, CancellationToken ct)
    {
        if (!s.IsConfigured)
        {
            await w.WriteAsync(new SearchEvent("link") { Supplier = s.Name, Url = Safe.Url(s.GetSearchUrl(query)) }, ct);
            return 0;
        }
        try
        {
            var filters = new SupplierFilters { InStockOnly = r.InStockOnly, RohsOnly = r.RohsOnly };
            var page = await FetchAsync(s, query, mode, Clamp(r.MaxResults), 0, filters, r.Refresh, ct);
            await w.WriteAsync(new SearchEvent("page") { Supplier = s.Name, Page = page }, ct);
            return page.Parts.Count;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            // Clé refusée, quota, panne : on le dit, et le lien vers le site du fournisseur reste utilisable.
            await w.WriteAsync(new SearchEvent("error") { Supplier = s.Name, Message = Describe(s, ex), Url = Safe.Url(s.GetSearchUrl(query)) }, ct);
            return 0;
        }
    }

    /// <summary>Le code appartient à un seul fournisseur : on le trouve, puis on cherche son MPN exact chez les autres.</summary>
    private async Task ByOrderCodeAsync(List<Supplier> selected, SearchRequest r, ChannelWriter<SearchEvent> w, CancellationToken ct)
    {
        var withKey = selected.Where(s => s.IsConfigured).ToList();
        var pages = new System.Collections.Concurrent.ConcurrentBag<SupplierPage>();
        var capture = Channel.CreateUnbounded<SearchEvent>();
        var counts = await Task.WhenAll(withKey.Select(async s =>
        {
            var n = await OneAsync(s, r.Query, SearchMode.SupplierCode, r, capture.Writer, ct);
            return (Supplier: s, Count: n);
        }));
        capture.Writer.TryComplete();
        await foreach (var e in capture.Reader.ReadAllAsync(ct))
        {
            if (e.Page != null) pages.Add(e.Page);
            await w.WriteAsync(e, ct);
        }

        var hit = pages.SelectMany(p => p.Parts).FirstOrDefault(p => p.ManufacturerPartNumber.Length > 0);
        if (hit == null)
        {
            await w.WriteAsync(new SearchEvent("codeResolved") { Code = r.Query }, ct);
            foreach (var s in selected.Except(withKey))
                await w.WriteAsync(new SearchEvent("link") { Supplier = s.Name, Url = Safe.Url(s.GetSearchUrl(r.Query)) }, ct);
            return;
        }

        var mpn = hit.ManufacturerPartNumber;
        await w.WriteAsync(new SearchEvent("codeResolved") { Code = r.Query, Mpn = mpn, Supplier = hit.Supplier }, ct);
        var others = counts.Where(c => c.Count == 0).Select(c => c.Supplier).Concat(selected.Except(withKey));
        await Task.WhenAll(others.Select(s => OneAsync(s, mpn, SearchMode.ExactPartNumber, r, w, ct)));
    }

    private async Task<SupplierPage> FetchAsync(Supplier s, string query, SearchMode mode, int size, int offset, SupplierFilters filters, bool refresh, CancellationToken ct)
    {
        // Le cache reste par personne : leurs clés, leurs pays et leurs devises donnent des réponses différentes.
        var key = (_user, _settings.Revision, s.Name, query.ToUpperInvariant(), mode, size, offset, filters.InStockOnly, filters.RohsOnly);
        if (!refresh && _cache.TryGetValue<SupplierPage>(key, out var cached) && cached != null)
            return cached with { Cached = true, ElapsedMs = 0 };

        var sw = Stopwatch.StartNew();
        // TME fait deux appels par recherche (produits, puis prix et stock).
        _db.CountCalls(_user, s.Name, s is TmeSupplier ? 2 : 1);
        var found = await s.SearchAsync(query, mode, size, offset, filters, ct);
        foreach (var p in found.Parts)
        {
            p.ProductUrl = Safe.Url(p.ProductUrl);
            p.DatasheetUrl = Safe.Url(p.DatasheetUrl);
            p.ImageUrl = Safe.Url(p.ImageUrl);
            p.LargeImageUrl = Safe.Url(p.LargeImageUrl);
        }
        var next = offset + size;
        var more = s.CanLoadMore(mode) && (found.TotalAvailable is { } total ? next < total : found.Parts.Count >= size);
        var page = new SupplierPage(s.Name, query, mode, found.Parts, found.TotalAvailable, next, more, sw.ElapsedMilliseconds, false, DateTime.UtcNow);
        _cache.Set(key, page, CacheFor);
        return page;
    }

    private string Describe(Supplier s, Exception ex)
    {
        var message = Safe.Scrub(ex.Message, _settings.SecretValues);
        _log.LogWarning("Search failed for {Supplier}: {Message}", s.Name, message);
        return message;
    }

    private static int Clamp(int size) => Math.Clamp(size, 1, 50);
}
