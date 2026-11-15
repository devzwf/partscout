namespace PartScout.Api.Infrastructure;

/// <summary>Configuration lue dans l'environnement au démarrage.</summary>
/// <param name="CookieAlwaysSecure">
/// PARTSCOUT_COOKIE_SECURE=always : cookie de session toujours en Secure, même si la requête arrive en HTTP.
/// À mettre dès que l'app est jointe hors du réseau local, forcément derrière HTTPS. Par défaut (auto) : Secure si la requête est en HTTPS.
/// </param>
public sealed record AppOptions(string DataDir, string MasterKey, string? SetupToken, bool Demo, bool CookieAlwaysSecure = false)
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
        // La valeur d'exemple de .env.example ne doit jamais servir : n'importe qui la connaît.
        if (token?.Contains("change-me", StringComparison.OrdinalIgnoreCase) == true || key.Contains("change-me", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("PARTSCOUT_SETUP_TOKEN or PARTSCOUT_MASTER_KEY still has the example value: generate one with openssl rand -base64 32.");
        var cookieSecure = (c["PARTSCOUT_COOKIE_SECURE"] ?? "auto").Trim().ToLowerInvariant() switch
        {
            "always" or "true" or "1" => true,
            "auto" or "" => false,
            var other => throw new InvalidOperationException($"PARTSCOUT_COOKIE_SECURE: « {other} » inconnu (auto ou always)."),
        };
        return new AppOptions(dataDir, key, string.IsNullOrEmpty(token) ? null : token, demo, cookieSecure);
    }
}
