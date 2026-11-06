using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using PartScout.Api.Infrastructure;

namespace PartScout.Tests;

public class UsersTests
{
    private const string Key = "CLE-MOUSER-TRES-SECRETE-123";
    private static readonly object Bom = new { name = "Privé", notes = "", lines = Array.Empty<object>() };

    [Fact]
    public async Task Les_cles_d_un_compte_ne_servent_pas_a_un_autre()
    {
        using var app = await new TestApp(demo: false).LoggedInAsync();
        var bob = await app.AddUserAsync("bob");
        (await app.Http.PutAsJsonAsync("/api/settings/secrets", new Dictionary<string, string> { ["MouserApiKey"] = Key })).EnsureSuccessStatusCode();

        var hers = await app.SearchAsync(new { query = "LM358" });
        Assert.NotEmpty(hers.PageOf("Mouser").GetProperty("parts").EnumerateArray());

        // Même recherche juste après : le cache de l'administrateur ne doit pas répondre à Bob, qui n'a pas de clé.
        var settings = await bob.GetFromJsonAsync<JsonElement>("/api/settings");
        Assert.False(settings.GetProperty("secretsSet").GetProperty("MouserApiKey").GetBoolean());
        var res = await bob.PostAsJsonAsync("/api/search", new { query = "LM358" });
        var text = await res.Content.ReadAsStringAsync();
        Assert.DoesNotContain("\"type\":\"page\"", text);
        Assert.Contains("\"type\":\"link\"", text);

        var calls = await bob.GetFromJsonAsync<JsonElement>("/api/suppliers");
        Assert.All(calls.EnumerateArray(), s => Assert.Equal(0, s.GetProperty("callsToday").GetInt64()));
    }

    [Fact]
    public async Task Un_blob_recopie_sur_un_autre_compte_ne_se_dechiffre_pas()
    {
        var vault = new SecretVault(TestApp.MasterKey);
        var blob = vault.Protect(SecretVault.Scoped(1, "MouserApiKey"), Key);
        Assert.Equal(Key, vault.Unprotect(SecretVault.Scoped(1, "MouserApiKey"), blob));
        Assert.Null(vault.Unprotect(SecretVault.Scoped(2, "MouserApiKey"), blob));
        await Task.CompletedTask;
    }

    [Fact]
    public async Task Le_bom_d_un_autre_compte_n_existe_pas()
    {
        using var app = await new TestApp().LoggedInAsync();
        var bob = await app.AddUserAsync("bob");
        var created = await (await app.Http.PostAsJsonAsync("/api/boms", Bom)).Content.ReadFromJsonAsync<JsonElement>();
        var id = created.GetProperty("id").GetInt64();

        Assert.Equal("[]", await bob.GetStringAsync("/api/boms"));
        Assert.Equal(HttpStatusCode.NotFound, (await bob.GetAsync($"/api/boms/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.GetAsync($"/api/boms/{id}/csv")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.PutAsJsonAsync($"/api/boms/{id}", Bom)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.DeleteAsync($"/api/boms/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await app.Http.GetAsync($"/api/boms/{id}")).StatusCode);
    }

    [Fact]
    public async Task L_historique_et_les_reglages_restent_personnels()
    {
        using var app = await new TestApp().LoggedInAsync();
        var bob = await app.AddUserAsync("bob");
        await app.SearchAsync(new { query = "LM358" });
        Assert.Equal("[]", await bob.GetStringAsync("/api/history"));

        (await bob.PutAsJsonAsync("/api/settings", new { digiKeySite = "UK" })).EnsureSuccessStatusCode();
        var mine = await app.Http.GetFromJsonAsync<JsonElement>("/api/settings");
        var his = await bob.GetFromJsonAsync<JsonElement>("/api/settings");
        Assert.Equal("CA", mine.GetProperty("settings").GetProperty("digiKeySite").GetString());
        Assert.Equal("UK", his.GetProperty("settings").GetProperty("digiKeySite").GetString());

        await bob.DeleteAsync("/api/history");
        Assert.Contains("LM358", await app.Http.GetStringAsync("/api/history"));
    }

    [Fact]
    public async Task Seul_un_administrateur_gere_les_comptes()
    {
        using var app = await new TestApp().LoggedInAsync();
        var bob = await app.AddUserAsync("bob");
        var users = await app.Http.GetFromJsonAsync<JsonElement>("/api/admin/users");
        var bobId = users.EnumerateArray().First(u => u.GetProperty("name").GetString() == "bob").GetProperty("id").GetInt64();
        var adminId = users.EnumerateArray().First(u => u.GetProperty("name").GetString() == "admin").GetProperty("id").GetInt64();
        Assert.DoesNotContain("password", users.ToString(), StringComparison.OrdinalIgnoreCase);

        Assert.Equal(HttpStatusCode.Forbidden, (await bob.GetAsync("/api/admin/users")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await bob.PostAsJsonAsync("/api/admin/users", new { name = "eve", password = TestApp.Password, isAdmin = true })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await bob.DeleteAsync($"/api/admin/users/{adminId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await bob.PutAsJsonAsync($"/api/admin/users/{adminId}/password", new { password = "nouveau-mot-de-passe" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await app.NewClient().GetAsync("/api/admin/users")).StatusCode);

        // Un administrateur reste toujours : pas de suppression de soi-même.
        Assert.Equal(HttpStatusCode.BadRequest, (await app.Http.DeleteAsync($"/api/admin/users/{adminId}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await bob.GetAsync("/api/boms")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await app.Http.DeleteAsync($"/api/admin/users/{bobId}")).StatusCode);
    }

    [Fact]
    public async Task Supprimer_un_compte_coupe_sa_session_et_retire_ses_donnees()
    {
        using var app = await new TestApp(demo: false).LoggedInAsync();
        var bob = await app.AddUserAsync("bob");
        await bob.PutAsJsonAsync("/api/settings/secrets", new Dictionary<string, string> { ["MouserApiKey"] = Key });
        await bob.PostAsJsonAsync("/api/boms", Bom);
        var users = await app.Http.GetFromJsonAsync<JsonElement>("/api/admin/users");
        var id = users.EnumerateArray().First(u => u.GetProperty("name").GetString() == "bob").GetProperty("id").GetInt64();

        (await app.Http.DeleteAsync($"/api/admin/users/{id}")).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Unauthorized, (await bob.GetAsync("/api/boms")).StatusCode);

        // Un nouveau compte du même nom repart de zéro.
        var again = await app.AddUserAsync("bob");
        Assert.Equal("[]", await again.GetStringAsync("/api/boms"));
        var s = await again.GetFromJsonAsync<JsonElement>("/api/settings");
        Assert.False(s.GetProperty("secretsSet").GetProperty("MouserApiKey").GetBoolean());
    }

    [Fact]
    public async Task Changer_de_mot_de_passe_coupe_les_autres_sessions()
    {
        using var app = await new TestApp().LoggedInAsync();
        var phone = app.NewClient();
        (await phone.PostAsJsonAsync("/api/auth/login", new { name = "admin", password = TestApp.Password })).EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.BadRequest, (await app.Http.PutAsJsonAsync("/api/me/password", new { current = "faux-mot-de-passe", password = "nouveau-mot-de-passe" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await app.Http.PutAsJsonAsync("/api/me/password", new { current = TestApp.Password, password = "court" })).StatusCode);
        (await app.Http.PutAsJsonAsync("/api/me/password", new { current = TestApp.Password, password = "nouveau-mot-de-passe" })).EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.OK, (await app.Http.GetAsync("/api/boms")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await phone.GetAsync("/api/boms")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await phone.PostAsJsonAsync("/api/auth/login", new { name = "admin", password = TestApp.Password })).StatusCode);
        (await phone.PostAsJsonAsync("/api/auth/login", new { name = "ADMIN", password = "nouveau-mot-de-passe" })).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Un_administrateur_reinitialise_le_mot_de_passe_d_un_autre()
    {
        using var app = await new TestApp().LoggedInAsync();
        var bob = await app.AddUserAsync("bob");
        var users = await app.Http.GetFromJsonAsync<JsonElement>("/api/admin/users");
        var id = users.EnumerateArray().First(u => u.GetProperty("name").GetString() == "bob").GetProperty("id").GetInt64();
        (await app.Http.PutAsJsonAsync($"/api/admin/users/{id}/password", new { password = "mot-de-passe-temporaire" })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Unauthorized, (await bob.GetAsync("/api/boms")).StatusCode);
        (await bob.PostAsJsonAsync("/api/auth/login", new { name = "bob", password = "mot-de-passe-temporaire" })).EnsureSuccessStatusCode();
    }

    [Theory]
    [InlineData("a", "un-mot-de-passe-solide", HttpStatusCode.BadRequest)]
    [InlineData("nom avec espace", "un-mot-de-passe-solide", HttpStatusCode.BadRequest)]
    [InlineData("<script>", "un-mot-de-passe-solide", HttpStatusCode.BadRequest)]
    [InlineData("bob", "court", HttpStatusCode.BadRequest)]
    [InlineData("ADMIN", "un-mot-de-passe-solide", HttpStatusCode.Conflict)]
    public async Task Un_compte_invalide_est_refuse(string name, string password, HttpStatusCode expected)
    {
        using var app = await new TestApp().LoggedInAsync();
        Assert.Equal(expected, (await app.Http.PostAsJsonAsync("/api/admin/users", new { name, password })).StatusCode);
    }

    [Fact]
    public async Task La_langue_suit_le_compte()
    {
        using var app = await new TestApp().LoggedInAsync();
        Assert.Equal(HttpStatusCode.BadRequest, (await app.Http.PutAsJsonAsync("/api/me/language", new { lang = "de" })).StatusCode);
        (await app.Http.PutAsJsonAsync("/api/me/language", new { lang = "en" })).EnsureSuccessStatusCode();
        var other = app.NewClient();
        await other.PostAsJsonAsync("/api/auth/login", new { name = "admin", password = TestApp.Password });
        var status = await other.GetFromJsonAsync<JsonElement>("/api/auth/status");
        Assert.Equal("en", status.GetProperty("user").GetProperty("lang").GetString());
    }

    [Fact]
    public async Task Un_compte_cree_avec_une_langue_la_garde()
    {
        using var app = await new TestApp().LoggedInAsync();
        (await app.Http.PostAsJsonAsync("/api/admin/users", new { name = "bob", password = TestApp.Password, lang = "fr" })).EnsureSuccessStatusCode();
        (await app.Http.PostAsJsonAsync("/api/admin/users", new { name = "eve", password = TestApp.Password, lang = "de" })).EnsureSuccessStatusCode();
        var users = await app.Http.GetFromJsonAsync<JsonElement>("/api/admin/users");
        string LangOf(string n) => users.EnumerateArray().First(u => u.GetProperty("name").GetString() == n).GetProperty("lang").GetString()!;
        Assert.Equal("fr", LangOf("bob"));
        Assert.Equal("", LangOf("eve")); // langue inconnue : aucune
    }

    // ---- Migration depuis le compte unique (version 0.1.x) ----

    private static void SeedLegacy(string dir, string masterKey)
    {
        using var c = new SqliteConnection($"Data Source={Path.Combine(dir, "partscout.db")};Pooling=False");
        c.Open();
        void Run(string sql, params (string, object)[] args)
        {
            using var cmd = c.CreateCommand();
            cmd.CommandText = sql;
            foreach (var (n, v) in args) cmd.Parameters.AddWithValue(n, v);
            cmd.ExecuteNonQuery();
        }
        Run("""
            CREATE TABLE kv(key TEXT PRIMARY KEY, value TEXT NOT NULL);
            CREATE TABLE secrets(name TEXT PRIMARY KEY, blob BLOB NOT NULL);
            CREATE TABLE boms(id INTEGER PRIMARY KEY AUTOINCREMENT, name TEXT NOT NULL, doc TEXT NOT NULL, updated_utc TEXT NOT NULL);
            CREATE TABLE history(query TEXT PRIMARY KEY COLLATE NOCASE, at_utc TEXT NOT NULL);
            CREATE TABLE api_calls(day TEXT NOT NULL, supplier TEXT NOT NULL, calls INTEGER NOT NULL, PRIMARY KEY(day, supplier));
            """);
        Run("INSERT INTO kv VALUES('password_hash',$h)", ("$h", new PasswordHasher<string>().HashPassword("owner", TestApp.Password)));
        Run("""INSERT INTO kv VALUES('settings','{"digiKeySite":"UK"}')""");
        Run("INSERT INTO secrets VALUES('MouserApiKey',$b)", ("$b", new SecretVault(masterKey).Protect("MouserApiKey", Key)));
        Run("""INSERT INTO boms(id,name,doc,updated_utc) VALUES(7,'Ancien','{"name":"Ancien","notes":"","lines":[]}','2026-11-04T00:00:00Z')""");
        Run("INSERT INTO history VALUES('NE555','2026-11-04T00:00:00Z')");
        Run("INSERT INTO api_calls VALUES($d,'Mouser',12)", ("$d", DateTime.UtcNow.ToString("yyyy-MM-dd")));
    }

    [Fact]
    public async Task L_ancien_compte_devient_administrateur_et_garde_tout()
    {
        using var app = new TestApp(demo: false, seed: d => SeedLegacy(d, TestApp.MasterKey));
        Assert.False((await app.Http.GetFromJsonAsync<JsonElement>("/api/auth/status")).GetProperty("setupRequired").GetBoolean());
        (await app.Http.PostAsJsonAsync("/api/auth/login", new { name = "admin", password = TestApp.Password })).EnsureSuccessStatusCode();

        var status = await app.Http.GetFromJsonAsync<JsonElement>("/api/auth/status");
        Assert.True(status.GetProperty("user").GetProperty("isAdmin").GetBoolean());

        var settings = await app.Http.GetFromJsonAsync<JsonElement>("/api/settings");
        Assert.Equal("UK", settings.GetProperty("settings").GetProperty("digiKeySite").GetString());
        Assert.True(settings.GetProperty("secretsSet").GetProperty("MouserApiKey").GetBoolean());
        Assert.Empty(settings.GetProperty("unreadableSecrets").EnumerateArray());
        Assert.Contains("Ancien", await app.Http.GetStringAsync("/api/boms"));
        Assert.Equal(HttpStatusCode.OK, (await app.Http.GetAsync("/api/boms/7")).StatusCode);
        Assert.Contains("NE555", await app.Http.GetStringAsync("/api/history"));
        var suppliers = await app.Http.GetFromJsonAsync<JsonElement>("/api/suppliers");
        Assert.Equal(12, suppliers.EnumerateArray().First(s => s.GetProperty("name").GetString() == "Mouser").GetProperty("callsToday").GetInt64());

        // La clé migrée sert encore pour de vrai, et un nouveau BOM ne reprend pas un ancien numéro.
        var events = await app.SearchAsync(new { query = "LM358" });
        Assert.NotEmpty(events.PageOf("Mouser").GetProperty("parts").EnumerateArray());
        var created = await (await app.Http.PostAsJsonAsync("/api/boms", Bom)).Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(created.GetProperty("id").GetInt64() > 7);

        // La copie d'avant la migration existe et contient l'ancien schéma.
        var copy = Directory.GetFiles(app.DataDir, "partscout.db.before-users-*").Single();
        using var c = new SqliteConnection($"Data Source={copy};Mode=ReadOnly;Pooling=False");
        c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT count(*) FROM kv WHERE key='password_hash'";
        Assert.Equal(1L, (long)cmd.ExecuteScalar()!);
    }

    [Fact]
    public void La_migration_ne_se_refait_pas()
    {
        var dir = Directory.CreateTempSubdirectory("partscout-mig").FullName;
        try
        {
            SeedLegacy(dir, TestApp.MasterKey);
            var vault = new SecretVault(TestApp.MasterKey);
            var path = Path.Combine(dir, "partscout.db");
            _ = new Db(path, vault);
            _ = new Db(path, vault);
            Assert.Single(Directory.GetFiles(dir, "partscout.db.before-users-*"));
            Assert.Equal("admin", new Db(path, vault).GetUser(1)?.Name);
        }
        finally { SqliteConnection.ClearAllPools(); Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task Une_cle_illisible_a_la_migration_reste_a_ressaisir()
    {
        using var app = new TestApp(demo: false, seed: d => SeedLegacy(d, new string('z', 40)));
        (await app.Http.PostAsJsonAsync("/api/auth/login", new { name = "admin", password = TestApp.Password })).EnsureSuccessStatusCode();
        var settings = await app.Http.GetFromJsonAsync<JsonElement>("/api/settings");
        Assert.Contains(settings.GetProperty("unreadableSecrets").EnumerateArray(), n => n.GetString() == "MouserApiKey");
    }
}
