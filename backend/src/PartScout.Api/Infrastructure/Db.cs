using Microsoft.Data.Sqlite;

namespace PartScout.Api.Infrastructure;

public sealed record UserRow(long Id, string Name, string PasswordHash, bool IsAdmin, string Lang, string Stamp, string CreatedUtc);

/// <summary>Accès SQLite minimal : les comptes, puis pour chacun ses réglages, son coffre, ses BOM, son historique et ses compteurs d'appels.</summary>
public sealed class Db
{
    private const int SchemaVersion = 2;
    private readonly string _connectionString;

    public Db(string path, SecretVault vault)
    {
        _connectionString = new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString();
        using var c = Open();
        Exec(c, "PRAGMA journal_mode=WAL");
        using var version = Command(c, "PRAGMA user_version");
        if ((long)version.ExecuteScalar()! < SchemaVersion) Migrate(c, path, vault);
    }

    /// <summary>
    /// Du compte unique aux utilisateurs. Tout passe dans une transaction : un échec laisse la base telle quelle,
    /// et une copie est faite avant, à côté de la base. L'ancien compte devient l'administrateur, nommé admin.
    /// </summary>
    private static void Migrate(SqliteConnection c, string path, SecretVault vault)
    {
        var hash = LegacyPasswordHash(c);
        if (hash != null)
        {
            var copy = $"{path}.before-users-{DateTime.UtcNow:yyyyMMddHHmmss}";
            Exec(c, "VACUUM INTO $f", ("$f", copy));
        }

        using var tx = c.BeginTransaction();
        Exec(c, """
            CREATE TABLE IF NOT EXISTS kv(key TEXT PRIMARY KEY, value TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS secrets(name TEXT PRIMARY KEY, blob BLOB NOT NULL);
            CREATE TABLE IF NOT EXISTS boms(id INTEGER PRIMARY KEY AUTOINCREMENT, name TEXT NOT NULL, doc TEXT NOT NULL, updated_utc TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS history(query TEXT PRIMARY KEY COLLATE NOCASE, at_utc TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS api_calls(day TEXT NOT NULL, supplier TEXT NOT NULL, calls INTEGER NOT NULL, PRIMARY KEY(day, supplier));

            CREATE TABLE users(id INTEGER PRIMARY KEY AUTOINCREMENT, name TEXT NOT NULL UNIQUE COLLATE NOCASE, password_hash TEXT NOT NULL,
                               is_admin INTEGER NOT NULL, lang TEXT NOT NULL DEFAULT '', stamp TEXT NOT NULL, created_utc TEXT NOT NULL);
            CREATE TABLE user_kv(user_id INTEGER NOT NULL, key TEXT NOT NULL, value TEXT NOT NULL, PRIMARY KEY(user_id, key));

            ALTER TABLE secrets RENAME TO old_secrets;
            CREATE TABLE secrets(user_id INTEGER NOT NULL, name TEXT NOT NULL, blob BLOB NOT NULL, PRIMARY KEY(user_id, name));
            ALTER TABLE history RENAME TO old_history;
            CREATE TABLE history(user_id INTEGER NOT NULL, query TEXT NOT NULL COLLATE NOCASE, at_utc TEXT NOT NULL, PRIMARY KEY(user_id, query));
            ALTER TABLE api_calls RENAME TO old_api_calls;
            CREATE TABLE api_calls(user_id INTEGER NOT NULL, day TEXT NOT NULL, supplier TEXT NOT NULL, calls INTEGER NOT NULL, PRIMARY KEY(user_id, day, supplier));
            ALTER TABLE boms ADD COLUMN user_id INTEGER NOT NULL DEFAULT 1;
            CREATE INDEX boms_user ON boms(user_id);
            """);

        if (hash != null)
        {
            const long owner = 1;
            Exec(c, "INSERT INTO users(id,name,password_hash,is_admin,stamp,created_utc) VALUES($i,'admin',$h,1,$s,$t)",
                ("$i", owner), ("$h", hash), ("$s", NewStamp()), ("$t", DateTime.UtcNow.ToString("O")));
            Exec(c, "INSERT INTO user_kv(user_id,key,value) SELECT $i, key, value FROM kv WHERE key='settings'", ("$i", owner));
            Exec(c, "INSERT INTO history(user_id,query,at_utc) SELECT $i, query, at_utc FROM old_history", ("$i", owner));
            Exec(c, "INSERT INTO api_calls(user_id,day,supplier,calls) SELECT $i, day, supplier, calls FROM old_api_calls", ("$i", owner));

            // Une clé devient liée à son propriétaire. Illisible (clé maîtresse changée) : elle le reste, à ressaisir comme avant.
            var old = new List<(string Name, byte[] Blob)>();
            using (var cmd = Command(c, "SELECT name, blob FROM old_secrets"))
            using (var r = cmd.ExecuteReader())
                while (r.Read()) old.Add((r.GetString(0), (byte[])r[1]));
            foreach (var (name, blob) in old)
            {
                var plain = vault.Unprotect(name, blob);
                var moved = plain == null ? blob : vault.Protect(SecretVault.Scoped(owner, name), plain);
                Exec(c, "INSERT INTO secrets(user_id,name,blob) VALUES($i,$n,$b)", ("$i", owner), ("$n", name), ("$b", moved));
            }
        }

        Exec(c, """
            DROP TABLE old_secrets;
            DROP TABLE old_history;
            DROP TABLE old_api_calls;
            DELETE FROM kv WHERE key IN ('password_hash','settings');
            """);
        Exec(c, $"PRAGMA user_version={SchemaVersion}");
        tx.Commit();
    }

    private static string? LegacyPasswordHash(SqliteConnection c)
    {
        using var exists = Command(c, "SELECT 1 FROM sqlite_master WHERE type='table' AND name='kv'");
        if (exists.ExecuteScalar() == null) return null;
        using var cmd = Command(c, "SELECT value FROM kv WHERE key='password_hash'");
        return cmd.ExecuteScalar() as string;
    }

    public static string NewStamp() => Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(16));

    private SqliteConnection Open()
    {
        var c = new SqliteConnection(_connectionString);
        c.Open();
        return c;
    }

    private static void Exec(SqliteConnection c, string sql, params (string, object?)[] args)
    {
        using var cmd = Command(c, sql, args);
        cmd.ExecuteNonQuery();
    }

    private static SqliteCommand Command(SqliteConnection c, string sql, params (string Name, object? Value)[] args)
    {
        var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (name, value) in args) cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return cmd;
    }

    // ---- Données communes (taux de change) ----

    public string? GetValue(string key)
    {
        using var c = Open();
        using var cmd = Command(c, "SELECT value FROM kv WHERE key=$k", ("$k", key));
        return cmd.ExecuteScalar() as string;
    }

    public void SetValue(string key, string value)
    {
        using var c = Open();
        Exec(c, "INSERT INTO kv(key,value) VALUES($k,$v) ON CONFLICT(key) DO UPDATE SET value=$v", ("$k", key), ("$v", value));
    }

    // ---- Comptes ----

    private const string UserColumns = "id,name,password_hash,is_admin,lang,stamp,created_utc";

    private static UserRow ReadUser(SqliteDataReader r) =>
        new(r.GetInt64(0), r.GetString(1), r.GetString(2), r.GetInt64(3) != 0, r.GetString(4), r.GetString(5), r.GetString(6));

    public bool HasUsers()
    {
        using var c = Open();
        using var cmd = Command(c, "SELECT 1 FROM users LIMIT 1");
        return cmd.ExecuteScalar() != null;
    }

    public UserRow? GetUser(long id)
    {
        using var c = Open();
        using var cmd = Command(c, $"SELECT {UserColumns} FROM users WHERE id=$i", ("$i", id));
        using var r = cmd.ExecuteReader();
        return r.Read() ? ReadUser(r) : null;
    }

    public UserRow? GetUserByName(string name)
    {
        using var c = Open();
        using var cmd = Command(c, $"SELECT {UserColumns} FROM users WHERE name=$n", ("$n", name));
        using var r = cmd.ExecuteReader();
        return r.Read() ? ReadUser(r) : null;
    }

    public List<UserRow> ListUsers()
    {
        using var c = Open();
        using var cmd = Command(c, $"SELECT {UserColumns} FROM users ORDER BY name");
        using var r = cmd.ExecuteReader();
        var list = new List<UserRow>();
        while (r.Read()) list.Add(ReadUser(r));
        return list;
    }

    /// <summary>Le premier compte, administrateur. Une seule requête : deux créations simultanées ne font qu'un gagnant.</summary>
    public long? CreateFirstUser(string name, string hash)
    {
        using var c = Open();
        using var cmd = Command(c, """
            INSERT INTO users(name,password_hash,is_admin,stamp,created_utc)
            SELECT $n,$h,1,$s,$t WHERE NOT EXISTS(SELECT 1 FROM users) RETURNING id
            """, ("$n", name), ("$h", hash), ("$s", NewStamp()), ("$t", DateTime.UtcNow.ToString("O")));
        return cmd.ExecuteScalar() as long?;
    }

    /// <summary>Renvoie null si le nom est déjà pris, quelle qu'en soit la casse.</summary>
    public long? CreateUser(string name, string hash, bool isAdmin, string lang = "")
    {
        using var c = Open();
        using var cmd = Command(c, """
            INSERT OR IGNORE INTO users(name,password_hash,is_admin,lang,stamp,created_utc) VALUES($n,$h,$a,$l,$s,$t) RETURNING id
            """, ("$n", name), ("$h", hash), ("$a", isAdmin ? 1 : 0), ("$l", lang), ("$s", NewStamp()), ("$t", DateTime.UtcNow.ToString("O")));
        return cmd.ExecuteScalar() as long?;
    }

    /// <summary>Change le mot de passe et le tampon : les sessions ouvertes avec l'ancien tombent. Renvoie le nouveau tampon.</summary>
    public string? SetPassword(long id, string hash)
    {
        var stamp = NewStamp();
        using var c = Open();
        using var cmd = Command(c, "UPDATE users SET password_hash=$h, stamp=$s WHERE id=$i", ("$h", hash), ("$s", stamp), ("$i", id));
        return cmd.ExecuteNonQuery() == 1 ? stamp : null;
    }

    public void SetLanguage(long id, string lang)
    {
        using var c = Open();
        Exec(c, "UPDATE users SET lang=$l WHERE id=$i", ("$l", lang), ("$i", id));
    }

    /// <summary>Retire le compte et tout ce qui lui appartient.</summary>
    public bool DeleteUser(long id)
    {
        using var c = Open();
        using var tx = c.BeginTransaction();
        foreach (var table in new[] { "user_kv", "secrets", "boms", "history", "api_calls" })
            Exec(c, $"DELETE FROM {table} WHERE user_id=$i", ("$i", id));
        using var cmd = Command(c, "DELETE FROM users WHERE id=$i", ("$i", id));
        var done = cmd.ExecuteNonQuery() == 1;
        tx.Commit();
        return done;
    }

    // ---- Données d'un utilisateur ----

    public string? GetValue(long user, string key)
    {
        using var c = Open();
        using var cmd = Command(c, "SELECT value FROM user_kv WHERE user_id=$u AND key=$k", ("$u", user), ("$k", key));
        return cmd.ExecuteScalar() as string;
    }

    public void SetValue(long user, string key, string value)
    {
        using var c = Open();
        Exec(c, "INSERT INTO user_kv(user_id,key,value) VALUES($u,$k,$v) ON CONFLICT(user_id,key) DO UPDATE SET value=$v", ("$u", user), ("$k", key), ("$v", value));
    }

    public Dictionary<string, byte[]> AllSecrets(long user)
    {
        using var c = Open();
        using var cmd = Command(c, "SELECT name, blob FROM secrets WHERE user_id=$u", ("$u", user));
        using var r = cmd.ExecuteReader();
        var all = new Dictionary<string, byte[]>();
        while (r.Read()) all[r.GetString(0)] = (byte[])r[1];
        return all;
    }

    public void SetSecret(long user, string name, byte[]? blob)
    {
        using var c = Open();
        if (blob == null) Exec(c, "DELETE FROM secrets WHERE user_id=$u AND name=$n", ("$u", user), ("$n", name));
        else Exec(c, "INSERT INTO secrets(user_id,name,blob) VALUES($u,$n,$b) ON CONFLICT(user_id,name) DO UPDATE SET blob=$b", ("$u", user), ("$n", name), ("$b", blob));
    }

    public void AddHistory(long user, string query)
    {
        using var c = Open();
        Exec(c, "INSERT INTO history(user_id,query,at_utc) VALUES($u,$q,$t) ON CONFLICT(user_id,query) DO UPDATE SET at_utc=$t",
            ("$u", user), ("$q", query), ("$t", DateTime.UtcNow.ToString("O")));
        Exec(c, "DELETE FROM history WHERE user_id=$u AND query NOT IN (SELECT query FROM history WHERE user_id=$u ORDER BY at_utc DESC LIMIT 100)", ("$u", user));
    }

    public List<string> GetHistory(long user, int limit)
    {
        using var c = Open();
        using var cmd = Command(c, "SELECT query FROM history WHERE user_id=$u ORDER BY at_utc DESC LIMIT $l", ("$u", user), ("$l", limit));
        using var r = cmd.ExecuteReader();
        var list = new List<string>();
        while (r.Read()) list.Add(r.GetString(0));
        return list;
    }

    public void ClearHistory(long user)
    {
        using var c = Open();
        Exec(c, "DELETE FROM history WHERE user_id=$u", ("$u", user));
    }

    public void CountCalls(long user, string supplier, int calls)
    {
        using var c = Open();
        Exec(c, "INSERT INTO api_calls(user_id,day,supplier,calls) VALUES($u,$d,$s,$n) ON CONFLICT(user_id,day,supplier) DO UPDATE SET calls=calls+$n",
            ("$u", user), ("$d", DateTime.UtcNow.ToString("yyyy-MM-dd")), ("$s", supplier), ("$n", calls));
    }

    public Dictionary<string, long> CallsToday(long user)
    {
        using var c = Open();
        using var cmd = Command(c, "SELECT supplier, calls FROM api_calls WHERE user_id=$u AND day=$d", ("$u", user), ("$d", DateTime.UtcNow.ToString("yyyy-MM-dd")));
        using var r = cmd.ExecuteReader();
        var all = new Dictionary<string, long>();
        while (r.Read()) all[r.GetString(0)] = r.GetInt64(1);
        return all;
    }

    // Chaque requête sur un BOM précise son propriétaire : le BOM d'un autre répond comme s'il n'existait pas.

    public long InsertBom(long user, string name, string doc)
    {
        using var c = Open();
        using var cmd = Command(c, "INSERT INTO boms(user_id,name,doc,updated_utc) VALUES($u,$n,$d,$t) RETURNING id",
            ("$u", user), ("$n", name), ("$d", doc), ("$t", DateTime.UtcNow.ToString("O")));
        return (long)cmd.ExecuteScalar()!;
    }

    public bool UpdateBom(long user, long id, string name, string doc)
    {
        using var c = Open();
        using var cmd = Command(c, "UPDATE boms SET name=$n, doc=$d, updated_utc=$t WHERE id=$i AND user_id=$u",
            ("$n", name), ("$d", doc), ("$t", DateTime.UtcNow.ToString("O")), ("$i", id), ("$u", user));
        return cmd.ExecuteNonQuery() == 1;
    }

    public string? GetBom(long user, long id)
    {
        using var c = Open();
        using var cmd = Command(c, "SELECT doc FROM boms WHERE id=$i AND user_id=$u", ("$i", id), ("$u", user));
        return cmd.ExecuteScalar() as string;
    }

    public bool DeleteBom(long user, long id)
    {
        using var c = Open();
        using var cmd = Command(c, "DELETE FROM boms WHERE id=$i AND user_id=$u", ("$i", id), ("$u", user));
        return cmd.ExecuteNonQuery() == 1;
    }

    public List<(long Id, string Name, string UpdatedUtc)> ListBoms(long user)
    {
        using var c = Open();
        using var cmd = Command(c, "SELECT id,name,updated_utc FROM boms WHERE user_id=$u ORDER BY updated_utc DESC", ("$u", user));
        using var r = cmd.ExecuteReader();
        var list = new List<(long, string, string)>();
        while (r.Read()) list.Add((r.GetInt64(0), r.GetString(1), r.GetString(2)));
        return list;
    }
}
