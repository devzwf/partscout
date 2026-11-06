using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
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

    public TestApp(bool demo = true, string? setupToken = null, Action<string>? seed = null)
    {
        seed?.Invoke(DataDir);
        Environment.SetEnvironmentVariable("PARTSCOUT_DATA", DataDir);
        Environment.SetEnvironmentVariable("PARTSCOUT_DEMO", demo ? "1" : null);
        Environment.SetEnvironmentVariable("PARTSCOUT_DEMO_FAST", "1");
        Environment.SetEnvironmentVariable("PARTSCOUT_MASTER_KEY", MasterKey);
        Environment.SetEnvironmentVariable("PARTSCOUT_SETUP_TOKEN", setupToken);
        WebHelper.Client = new HttpClient(new DemoHandler { Slow = false });
        _factory = new WebApplicationFactory<Program>();
        Http = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
    }

    public async Task<TestApp> LoggedInAsync()
    {
        (await Http.PostAsJsonAsync("/api/auth/setup", new { password = Password })).EnsureSuccessStatusCode();
        return this;
    }

    /// <summary>Un autre navigateur : ses cookies à lui.</summary>
    public HttpClient NewClient() => _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });

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

public static class Events
{
    public static string Type(this JsonElement e) => e.GetProperty("type").GetString()!;
    public static IEnumerable<JsonElement> OfType(this IEnumerable<JsonElement> all, string type) => all.Where(e => e.Type() == type);
    public static JsonElement PageOf(this IEnumerable<JsonElement> all, string supplier) =>
        all.OfType("page").First(e => e.GetProperty("supplier").GetString() == supplier).GetProperty("page");
}
