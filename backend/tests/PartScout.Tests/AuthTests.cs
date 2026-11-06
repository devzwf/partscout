using System.Net;
using System.Net.Http.Json;

namespace PartScout.Tests;

public class AuthTests
{
    [Theory]
    [InlineData("/api/settings")]
    [InlineData("/api/suppliers")]
    [InlineData("/api/boms")]
    [InlineData("/api/history")]
    [InlineData("/api/rates")]
    public async Task Sans_connexion_l_api_repond_401(string path)
    {
        using var app = new TestApp();
        Assert.Equal(HttpStatusCode.Unauthorized, (await app.Http.GetAsync(path)).StatusCode);
    }

    [Fact]
    public async Task La_recherche_exige_la_connexion()
    {
        using var app = new TestApp();
        var res = await app.Http.PostAsJsonAsync("/api/search", new { query = "LM358" });
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task Le_compte_ne_se_cree_qu_une_fois()
    {
        using var app = new TestApp();
        Assert.Equal(HttpStatusCode.BadRequest, (await app.Http.PostAsJsonAsync("/api/auth/setup", new { password = "court" })).StatusCode);
        await app.LoggedInAsync();
        Assert.Equal(HttpStatusCode.Conflict, (await app.Http.PostAsJsonAsync("/api/auth/setup", new { password = "un-autre-mot-de-passe" })).StatusCode);
    }

    [Fact]
    public async Task Connexion_et_deconnexion()
    {
        using var app = await new TestApp().LoggedInAsync();
        Assert.Equal(HttpStatusCode.OK, (await app.Http.GetAsync("/api/boms")).StatusCode);
        await app.Http.PostAsync("/api/auth/logout", null);
        Assert.Equal(HttpStatusCode.Unauthorized, (await app.Http.GetAsync("/api/boms")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await app.Http.PostAsJsonAsync("/api/auth/login", new { name = "admin", password = "mauvais-mot-de-passe" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await app.Http.PostAsJsonAsync("/api/auth/login", new { name = "admin", password = TestApp.Password })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await app.Http.GetAsync("/api/boms")).StatusCode);
    }

    [Fact]
    public async Task Le_jeton_d_installation_est_exige_s_il_est_defini()
    {
        using var app = new TestApp(setupToken: "jeton-secret");
        Assert.Equal(HttpStatusCode.Forbidden, (await app.Http.PostAsJsonAsync("/api/auth/setup", new { password = TestApp.Password })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await app.Http.PostAsJsonAsync("/api/auth/setup", new { password = TestApp.Password, token = "faux" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await app.Http.PostAsJsonAsync("/api/auth/setup", new { password = TestApp.Password, token = "jeton-secret" })).StatusCode);
    }

    [Fact]
    public async Task Les_en_tetes_de_securite_sont_presents()
    {
        using var app = new TestApp();
        var res = await app.Http.GetAsync("/healthz");
        Assert.Contains("default-src 'self'", res.Headers.GetValues("Content-Security-Policy").Single());
        Assert.Equal("no-referrer", res.Headers.GetValues("Referrer-Policy").Single());
    }
}
