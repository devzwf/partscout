namespace PartScout.Api.Infrastructure;

/// <summary>Configuration lue dans l'environnement au démarrage.</summary>
public sealed record AppOptions(string DataDir, string MasterKey, string? SetupToken, bool Demo)
{
    public static AppOptions FromEnvironment(IConfiguration c)
    {
        var demo = c["PARTSCOUT_DEMO"] is "1" or "true";
        var dataDir = c["PARTSCOUT_DATA"] ?? "/data";
        var key = c["PARTSCOUT_MASTER_KEY"];
        var keyFile = c["PARTSCOUT_MASTER_KEY_FILE"];
        if (string.IsNullOrEmpty(key) && !string.IsNullOrEmpty(keyFile)) key = File.ReadAllText(keyFile).Trim();
        if (string.IsNullOrEmpty(key))
        {
            // La démo ne contient aucune vraie clé : une clé jetable suffit.
            if (!demo) throw new InvalidOperationException("PARTSCOUT_MASTER_KEY manquante (openssl rand -base64 32).");
            key = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        }
        var token = c["PARTSCOUT_SETUP_TOKEN"];
        return new AppOptions(dataDir, key, string.IsNullOrEmpty(token) ? null : token, demo);
    }
}
