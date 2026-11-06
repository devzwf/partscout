using Microsoft.Data.Sqlite;

namespace PartScout.Api.Services;

/// <summary>
/// Écrit une copie cohérente de la base dans /data/backups : copier un fichier SQLite en cours d'écriture peut donner une
/// sauvegarde inutilisable. Une copie par jour (UTC), rafraîchie chaque heure, puis gardée 14 jours. La sauvegarde de
/// l'hôte n'a qu'à emporter ce dossier. Les clés d'API y restent chiffrées.
/// </summary>
public sealed class BackupService : BackgroundService
{
    public const int KeepDays = 14;
    private readonly string _database;
    private readonly string _folder;
    private readonly ILogger<BackupService> _log;

    public BackupService(Infrastructure.AppOptions options, ILogger<BackupService> log)
    {
        _database = Path.Combine(options.DataDir, "partscout.db");
        _folder = Path.Combine(options.DataDir, "backups");
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(1));
        do
        {
            try
            {
                WriteSnapshot(_database, _folder, DateTime.UtcNow);
            }
            catch (Exception ex) when (ex is SqliteException or IOException or UnauthorizedAccessException)
            {
                // L'application continue de servir, mais ce message ne doit pas passer inaperçu.
                _log.LogError(ex, "Copie de sauvegarde impossible");
            }
        }
        while (await WaitAsync(timer, stop));
    }

    private static async Task<bool> WaitAsync(PeriodicTimer timer, CancellationToken stop)
    {
        try { return await timer.WaitForNextTickAsync(stop); }
        catch (OperationCanceledException) { return false; }
    }

    /// <summary>Le fichier du jour n'est remplacé qu'une fois la nouvelle copie complète.</summary>
    public static string WriteSnapshot(string database, string folder, DateTime nowUtc)
    {
        Directory.CreateDirectory(folder);
        var target = Path.Combine(folder, $"partscout-{nowUtc:yyyy-MM-dd}.db");
        var temp = target + ".tmp";
        File.Delete(temp);
        using (var c = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = database, Pooling = false }.ToString()))
        {
            c.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "VACUUM INTO $path";
            cmd.Parameters.AddWithValue("$path", temp);
            cmd.ExecuteNonQuery();
        }
        File.Move(temp, target, overwrite: true);
        Prune(folder);
        return target;
    }

    /// <summary>Les noms contiennent la date : l'ordre alphabétique est l'ordre chronologique.</summary>
    private static void Prune(string folder)
    {
        foreach (var old in Directory.GetFiles(folder, "partscout-*.db").OrderDescending(StringComparer.Ordinal).Skip(KeepDays))
            File.Delete(old);
    }
}
