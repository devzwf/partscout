using System.Security.Cryptography;
using System.Text;

namespace PartScout.Api.Infrastructure;

/// <summary>Chiffre les clés d'API en AES-256-GCM. La clé maîtresse vient de l'environnement, jamais du volume de données.</summary>
public sealed class SecretVault
{
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private readonly byte[] _key;

    public SecretVault(string masterKey)
    {
        if (masterKey.Length < 32)
            throw new InvalidOperationException("The master key must be at least 32 characters (openssl rand -base64 32).");
        _key = HKDF.DeriveKey(HashAlgorithmName.SHA256, Encoding.UTF8.GetBytes(masterKey), 32, info: Encoding.UTF8.GetBytes("partscout-secrets-v1"));
    }

    /// <summary>Nom authentifié d'un secret : il lie le blob à son propriétaire et à son usage.</summary>
    public static string Scoped(long user, string name) => $"{user}:{name}";

    /// <summary>Le nom du secret entre dans l'authentification : un blob recopié sur un autre nom ou un autre compte ne se déchiffre pas.</summary>
    public byte[] Protect(string name, string plain)
    {
        var data = Encoding.UTF8.GetBytes(plain);
        var blob = new byte[NonceSize + TagSize + data.Length];
        var nonce = blob.AsSpan(0, NonceSize);
        RandomNumberGenerator.Fill(nonce);
        using var aes = new AesGcm(_key, TagSize);
        aes.Encrypt(nonce, data, blob.AsSpan(NonceSize + TagSize), blob.AsSpan(NonceSize, TagSize), Encoding.UTF8.GetBytes(name));
        return blob;
    }

    /// <summary>Renvoie null si le blob est illisible (clé maîtresse changée) : il suffit de ressaisir la clé d'API.</summary>
    public string? Unprotect(string name, byte[] blob)
    {
        if (blob.Length < NonceSize + TagSize) return null;
        var plain = new byte[blob.Length - NonceSize - TagSize];
        try
        {
            using var aes = new AesGcm(_key, TagSize);
            aes.Decrypt(blob.AsSpan(0, NonceSize), blob.AsSpan(NonceSize + TagSize), blob.AsSpan(NonceSize, TagSize), plain, Encoding.UTF8.GetBytes(name));
            return Encoding.UTF8.GetString(plain);
        }
        catch (CryptographicException)
        {
            return null;
        }
    }
}
