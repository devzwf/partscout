using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using PartScout.Api.Services;

namespace PartScout.Tests;

public class BackupTests
{
    [Fact]
    public async Task La_copie_contient_les_donnees_meme_pendant_que_l_application_tourne()
    {
        using var app = await new TestApp().LoggedInAsync();
        await app.Http.PostAsJsonAsync("/api/boms", new { name = "À sauver", notes = "", lines = Array.Empty<object>() });

        var path = BackupService.WriteSnapshot(Path.Combine(app.DataDir, "partscout.db"), Path.Combine(app.DataDir, "backups"), DateTime.UtcNow);

        using var c = new SqliteConnection($"Data Source={path};Mode=ReadOnly;Pooling=False");
        c.Open();
        using var check = c.CreateCommand();
        check.CommandText = "PRAGMA integrity_check";
        Assert.Equal("ok", check.ExecuteScalar());
        using var names = c.CreateCommand();
        names.CommandText = "SELECT name FROM boms";
        Assert.Equal("À sauver", names.ExecuteScalar());
    }

    [Fact]
    public async Task Une_seule_copie_par_jour_et_quatorze_jours_gardes()
    {
        using var app = await new TestApp().LoggedInAsync();
        var db = Path.Combine(app.DataDir, "partscout.db");
        var folder = Path.Combine(app.DataDir, "test-rotation"); // « backups » reçoit aussi la vraie copie du jour
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        for (var i = 0; i < 20; i++)
        {
            BackupService.WriteSnapshot(db, folder, start.AddDays(i));
            BackupService.WriteSnapshot(db, folder, start.AddDays(i).AddHours(3));
        }
        var files = Directory.GetFiles(folder).Select(Path.GetFileName).Order().ToList();
        Assert.Equal(BackupService.KeepDays, files.Count);
        Assert.Equal("partscout-2026-01-07.db", files[0]);
        Assert.Equal("partscout-2026-01-20.db", files[^1]);
    }
}
