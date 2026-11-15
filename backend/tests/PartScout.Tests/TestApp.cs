using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using PartScout.Api.Demo;
using PartScout.Suppliers;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace PartScout.Tests;

/// <summary>Démarre l'application complète sur une base temporaire. Les fournisseurs sont simulés par DemoHandler.</summary>
public sealed class TestApp : IDisposable
{
    public string DataDir { get; } = Directory.CreateTempSubdirectory("partscout-test").FullName;
    private readonly WebApplicationFactory<Program> _factory;
    public HttpClient Http { get; }
    public const string Password = "un-mot-de-passe-solide";

    public const string MasterKey = "cle-maitresse-de-test-assez-longue-0123456789"; // gitleaks:allow (clé fictive des tests)

    /// <summary>L'horloge vue par l'app : les tests la font avancer.</summary>
    public ManualTime Clock { get; } = new();

    public TestApp(bool demo = true, string? setupToken = null, Action<string>? seed = null, string? cookieSecure = null)
    {
        seed?.Invoke(DataDir);
        Environment.SetEnvironmentVariable("PARTSCOUT_DATA", DataDir);
        Environment.SetEnvironmentVariable("PARTSCOUT_DEMO", demo ? "1" : null);
        Environment.SetEnvironmentVariable("PARTSCOUT_DEMO_FAST", "1");
        Environment.SetEnvironmentVariable("PARTSCOUT_MASTER_KEY", MasterKey);
        Environment.SetEnvironmentVariable("PARTSCOUT_SETUP_TOKEN", setupToken);
        Environment.SetEnvironmentVariable("PARTSCOUT_COOKIE_SECURE", cookieSecure);
        WebHelper.Client = new HttpClient(new DemoHandler { Slow = false });
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
            b.ConfigureTestServices(s => s.AddSingleton<TimeProvider>(Clock)));
        Http = NewClient();
    }

    public async Task<TestApp> LoggedInAsync()
    {
        (await Http.PostAsJsonAsync("/api/auth/setup", new { password = Password })).EnsureSuccessStatusCode();
        return this;
    }

    /// <summary>Un autre navigateur : ses cookies à lui. Il envoie X-PartScout, comme l'interface.</summary>
    public HttpClient NewClient()
    {
        var client = BareClient();
        client.DefaultRequestHeaders.Add("X-PartScout", "1");
        return client;
    }

    /// <summary>Un client sans X-PartScout : ce que peut faire une page d'un autre site.</summary>
    public HttpClient BareClient() => _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });

    /// <summary>Crée un compte (connecté en administrateur sur Http) et renvoie un navigateur connecté sous ce compte.</summary>
    public async Task<HttpClient> AddUserAsync(string name, string password = Password)
    {
        (await Http.PostAsJsonAsync("/api/admin/users", new { name, password })).EnsureSuccessStatusCode();
        var client = NewClient();
        (await client.PostAsJsonAsync("/api/auth/login", new { name, password })).EnsureSuccessStatusCode();
        return client;
    }

    public async Task<List<JsonElement>> SearchAsync(object request)
    {
        var res = await Http.PostAsJsonAsync("/api/search", request);
        res.EnsureSuccessStatusCode();
        Assert.Equal("application/x-ndjson", res.Content.Headers.ContentType?.MediaType);
        var text = await res.Content.ReadAsStringAsync();
        return text.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => JsonDocument.Parse(l).RootElement.Clone()).ToList();
    }

    public void Dispose()
    {
        Http.Dispose();
        _factory.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(DataDir, true); } catch (IOException) { }
    }
}

/// <summary>Horloge réglable à la main.</summary>
public sealed class ManualTime : TimeProvider
{
    private DateTimeOffset _now = DateTimeOffset.UtcNow;
    public override DateTimeOffset GetUtcNow() => _now;
    public void Advance(TimeSpan by) => _now += by;
}

public static class Events
{
    public static string Type(this JsonElement e) => e.GetProperty("type").GetString()!;
    public static IEnumerable<JsonElement> OfType(this IEnumerable<JsonElement> all, string type) => all.Where(e => e.Type() == type);
    public static JsonElement PageOf(this IEnumerable<JsonElement> all, string supplier) =>
        all.OfType("page").First(e => e.GetProperty("supplier").GetString() == supplier).GetProperty("page");
}
