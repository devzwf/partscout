using System.Text.RegularExpressions;

namespace PartScout.Api.Infrastructure;

public static partial class Safe
{
    /// <summary>N'accepte que http et https ; les URL sans schéma (//hote/...) passent en https. Sinon renvoie "".</summary>
    public static string Url(string? url)
    {
        var u = (url ?? "").Trim();
        if (u.StartsWith("//")) u = "https:" + u;
        return Uri.TryCreate(u, UriKind.Absolute, out var parsed) && (parsed.Scheme == Uri.UriSchemeHttp || parsed.Scheme == Uri.UriSchemeHttps)
            ? u : "";
    }

    /// <summary>Retire les clés d'un message d'erreur : valeurs connues, et paramètres de clé dans une URL.</summary>
    public static string Scrub(string? message, IEnumerable<string> secrets)
    {
        var text = message ?? "";
        foreach (var s in secrets.Where(s => s.Length > 0)) text = text.Replace(s, "•••");
        return KeyParameter().Replace(text, "$1=•••");
    }

    [GeneratedRegex(@"((?:callInfo\.)?api_?key|client_secret|access_token)=[^&\s""']+", RegexOptions.IgnoreCase)]
    private static partial Regex KeyParameter();
}
