using System.Text.Json;
using PartScout.Api.Infrastructure;
using PartScout.Suppliers;

namespace PartScout.Api.Services;

public sealed record RatesDto(string RatesDate, DateTime? FetchedUtc, Dictionary<string, decimal> PerEuro, bool Stale, string? Error);

/// <summary>Taux BCE gardés en base : rafraîchis après 12 h ; en cas d'échec les derniers taux connus restent utilisés.</summary>
public sealed class RatesService
{
    private const string Key = "rates";
    private readonly Db _db;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public RatesService(Db db) => _db = db;

    public async Task<RatesDto> GetAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var current = Read();
            if (!current.NeedsRefresh) return Dto(current, false, null);
            try
            {
                var fresh = await ExchangeRates.DownloadAsync(ct);
                _db.SetValue(Key, JsonSerializer.Serialize(new Stored(fresh.RatesDate, fresh.FetchedUtc, fresh.PerEuro), Json.Options));
                return Dto(fresh, false, null);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                return Dto(current, true, ex.Message);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private ExchangeRates Read()
    {
        var json = _db.GetValue(Key);
        if (json == null) return new ExchangeRates();
        var s = JsonSerializer.Deserialize<Stored>(json, Json.Options);
        return s == null ? new ExchangeRates() : new ExchangeRates { RatesDate = s.RatesDate, FetchedUtc = s.FetchedUtc, PerEuro = s.PerEuro };
    }

    private static RatesDto Dto(ExchangeRates r, bool stale, string? error) => new(r.RatesDate, r.FetchedUtc, r.PerEuro, stale, error);

    private sealed record Stored(string RatesDate, DateTime? FetchedUtc, Dictionary<string, decimal> PerEuro);
}
