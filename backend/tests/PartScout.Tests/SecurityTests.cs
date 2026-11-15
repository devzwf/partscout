using System.Net;
using System.Net.Http.Json;
using System.Text;

namespace PartScout.Tests;

/// <summary>Suites de l'audit de sécurité : CSRF, taille des corps, durée des sessions, cookie Secure.</summary>
public class SecurityTests
{
    [Fact]
    public async Task Une_ecriture_sans_X_PartScout_est_refusee()
    {
        using var app = await new TestApp().LoggedInAsync();
        var bare = app.BareClient();
        // Même session, mais sans l'en-tête : ce que pourrait faire un formulaire ou un script d'un autre site.
        (await bare.PostAsJsonAsync("/api/auth/login", new { name = "admin", password = TestApp.Password })).Dispose();
        var res = await bare.PostAsJsonAsync("/api/boms", new { name = "x", lines = Array.Empty<object>() });
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
        Assert.Contains("staleClient", await res.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Forbidden, (await bare.DeleteAsync("/api/history")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await bare.PostAsJsonAsync("/api/admin/users", new { name = "pirate", password = TestApp.Password })).StatusCode);
    }

    [Fact]
    public async Task La_connexion_aussi_exige_X_PartScout()
    {
        using var app = await new TestApp().LoggedInAsync();
        var bare = app.BareClient();
        Assert.Equal(HttpStatusCode.Forbidden, (await bare.PostAsJsonAsync("/api/auth/login", new { name = "admin", password = TestApp.Password })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await bare.GetAsync("/api/boms")).StatusCode);
    }

    [Fact]
    public async Task Les_lectures_n_exigent_pas_X_PartScout()
    {
        using var app = new TestApp();
        Assert.Equal(HttpStatusCode.OK, (await app.BareClient().GetAsync("/api/auth/status")).StatusCode);
    }

    private static StringContent Body(int size) => new("{\"name\":\"" + new string('a', size) + "\"}", Encoding.UTF8, "application/json");

    [Fact]
    public async Task Un_corps_trop_gros_est_refuse()
    {
        using var app = await new TestApp().LoggedInAsync();
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, (await app.Http.PutAsync("/api/settings", Body(100 * 1024))).StatusCode);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, (await app.Http.PostAsync("/api/auth/login", Body(100 * 1024))).StatusCode);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, (await app.Http.PostAsync("/api/boms/import", Body(2 * 1024 * 1024))).StatusCode);
    }

    [Fact]
    public async Task Une_grosse_BOM_reste_acceptee()
    {
        using var app = await new TestApp().LoggedInAsync();
        var csv = "\"Refs\",\"Value\",\"Qty\",\"MPN\"\n" + string.Concat(Enumerable.Range(0, 3000).Select(i => $"\"R{i}\",\"10k\",\"1\",\"CRCW0805-{i:D6}\"\n"));
        Assert.True(csv.Length > 64 * 1024);
        var res = await app.Http.PostAsJsonAsync("/api/boms/import", new { csv, name = "grosse" });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }

    [Fact]
    public async Task Une_session_ne_depasse_pas_90_jours_meme_utilisee()
    {
        using var app = await new TestApp().LoggedInAsync();
        // Utilisée tous les 20 jours, la session glisse (30 jours) : sans limite absolue, elle durerait toujours.
        for (var day = 20; day <= 80; day += 20)
        {
            app.Clock.Advance(TimeSpan.FromDays(20));
            Assert.Equal(HttpStatusCode.OK, (await app.Http.GetAsync("/api/boms")).StatusCode);
        }
        app.Clock.Advance(TimeSpan.FromDays(20)); // jour 100
        Assert.Equal(HttpStatusCode.Unauthorized, (await app.Http.GetAsync("/api/boms")).StatusCode);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("auto", false)]
    [InlineData("always", true)]
    public async Task Le_cookie_est_Secure_selon_le_reglage(string? setting, bool secure)
    {
        using var app = new TestApp(cookieSecure: setting);
        var res = await app.Http.PostAsJsonAsync("/api/auth/setup", new { password = TestApp.Password });
        res.EnsureSuccessStatusCode();
        var cookie = res.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("partscout="));
        Assert.Equal(secure, cookie.Contains("secure", StringComparison.OrdinalIgnoreCase));
    }
}
