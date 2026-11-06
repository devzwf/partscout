using System.Collections.Concurrent;
using System.Text.Json;
using PartScout.Api.Demo;
using PartScout.Api.Infrastructure;
using PartScout.Suppliers;

namespace PartScout.Api.Services;

/// <summary>Les réglages de chaque utilisateur, gardés en mémoire une fois chargés.</summary>
public sealed class SettingsService
{
    private readonly Db _db;
    private readonly SecretVault _vault;
    private readonly bool _demo;
    private readonly ConcurrentDictionary<long, UserSettings> _users = new();

    public SettingsService(Db db, SecretVault vault, AppOptions options)
    {
        _db = db;
        _vault = vault;
        _demo = options.Demo;
    }

    public UserSettings For(long user) => _users.GetOrAdd(user, id => new UserSettings(id, _db, _vault, _demo));

    /// <summary>Compte supprimé : ses clés et ses jetons quittent la mémoire.</summary>
    public void Forget(long user) => _users.TryRemove(user, out _);
}

/// <summary>Réglages et clés d'un utilisateur. Garde sa liste de fournisseurs en mémoire : leurs jetons OAuth y vivent. Tout changement la reconstruit.</summary>
public sealed class UserSettings
{
    private const string Key = "settings";
    private readonly long _user;
    private readonly Db _db;
    private readonly SecretVault _vault;
    private readonly bool _demo;
    private readonly Lock _lock = new();
    private AppSettings? _settings;
    private List<Supplier>? _suppliers;

    public UserSettings(long user, Db db, SecretVault vault, bool demo)
    {
        _user = user;
        _db = db;
        _vault = vault;
        _demo = demo;
    }

    /// <summary>Change à chaque modification : fait partie de la clé du cache de recherche.</summary>
    public int Revision { get; private set; }

    public AppSettings Current
    {
        get
        {
            lock (_lock) return _settings ??= Load();
        }
    }

    public List<Supplier> Suppliers
    {
        get
        {
            lock (_lock) return _suppliers ??= SupplierFactory.Create(_settings ??= Load());
        }
    }

    public IReadOnlyList<string> SecretValues
    {
        get
        {
            var s = Current;
            return AppSettings.SecretNames.Select(n => Get(s, n)).Where(v => v.Length > 0).ToList();
        }
    }

    public Dictionary<string, bool> SecretsSet()
    {
        var s = Current;
        return AppSettings.SecretNames.ToDictionary(n => n, n => Get(s, n).Length > 0);
    }

    /// <summary>Clés illisibles avec la clé maîtresse actuelle : à ressaisir.</summary>
    public List<string> UnreadableSecrets { get; private set; } = new();

    public void Save(AppSettings incoming)
    {
        lock (_lock)
        {
            _db.SetValue(_user, Key, JsonSerializer.Serialize(incoming, Json.Options));
            Invalidate();
        }
    }

    /// <summary>Une valeur vide efface la clé ; un nom absent n'y change rien.</summary>
    public void SaveSecrets(Dictionary<string, string> values)
    {
        lock (_lock)
        {
            foreach (var (name, value) in values)
            {
                if (!AppSettings.SecretNames.Contains(name)) continue;
                var v = value.Trim();
                _db.SetSecret(_user, name, v.Length == 0 ? null : _vault.Protect(SecretVault.Scoped(_user, name), v));
            }
            Invalidate();
        }
    }

    private void Invalidate()
    {
        _settings = null;
        _suppliers = null;
        Revision++;
    }

    private AppSettings Load()
    {
        var json = _db.GetValue(_user, Key);
        var s = json == null ? new AppSettings() : JsonSerializer.Deserialize<AppSettings>(json, Json.Options) ?? new AppSettings();
        var blobs = _db.AllSecrets(_user);
        var unreadable = new List<string>();
        foreach (var name in AppSettings.SecretNames)
        {
            var value = "";
            if (blobs.TryGetValue(name, out var blob))
            {
                var plain = _vault.Unprotect(SecretVault.Scoped(_user, name), blob);
                if (plain == null) unreadable.Add(name);
                value = plain ?? "";
            }
            if (_demo && value.Length == 0) value = DemoHandler.FakeSecret;
            Set(s, name, value);
        }
        UnreadableSecrets = unreadable;
        return s;
    }

    private static string Get(AppSettings s, string name) => name switch
    {
        nameof(AppSettings.MouserApiKey) => s.MouserApiKey,
        nameof(AppSettings.DigiKeyClientId) => s.DigiKeyClientId,
        nameof(AppSettings.DigiKeyClientSecret) => s.DigiKeyClientSecret,
        nameof(AppSettings.Element14ApiKey) => s.Element14ApiKey,
        nameof(AppSettings.TmeToken) => s.TmeToken,
        nameof(AppSettings.TmeSecret) => s.TmeSecret,
        _ => ""
    };

    private static void Set(AppSettings s, string name, string value)
    {
        switch (name)
        {
            case nameof(AppSettings.MouserApiKey): s.MouserApiKey = value; break;
            case nameof(AppSettings.DigiKeyClientId): s.DigiKeyClientId = value; break;
            case nameof(AppSettings.DigiKeyClientSecret): s.DigiKeyClientSecret = value; break;
            case nameof(AppSettings.Element14ApiKey): s.Element14ApiKey = value; break;
            case nameof(AppSettings.TmeToken): s.TmeToken = value; break;
            case nameof(AppSettings.TmeSecret): s.TmeSecret = value; break;
        }
    }
}
