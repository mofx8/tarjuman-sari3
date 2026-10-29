using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace QuickTranslate.Core.Storage;

public static partial class TextNormalizer
{
    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();

    public static string Normalize(string s) => Spaces().Replace(s, " ").Trim();

    /// <summary>True when there is nothing worth sending (empty, digits/punctuation only).</summary>
    public static bool IsTrivial(string s)
    {
        foreach (var ch in s) if (char.IsLetter(ch)) return false;
        return true;
    }
}

public sealed record CachedTranslation(string Text, string DetectedSource, string EngineId);

/// <summary>Memory + SQLite cache so an identical sentence is never sent twice.</summary>
public sealed class TranslationCache : IDisposable
{
    private readonly ConcurrentDictionary<string, CachedTranslation> _mem = new();
    private readonly SqliteConnection? _db;
    private readonly object _dbLock = new();

    public TranslationCache(string? dbPath)
    {
        if (dbPath == null) return;
        _db = new SqliteConnection($"Data Source={dbPath}");
        _db.Open();
        Exec("CREATE TABLE IF NOT EXISTS cache(k TEXT PRIMARY KEY, t TEXT NOT NULL, s TEXT, e TEXT, ts INTEGER)");
    }

    private static string Key(string text, string src, string tgt) => $"{src}>{tgt}|{TextNormalizer.Normalize(text)}";

    public CachedTranslation? Get(string text, string src, string tgt)
    {
        var k = Key(text, src, tgt);
        if (_mem.TryGetValue(k, out var hit)) return hit;
        if (_db == null) return null;
        lock (_dbLock)
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = "SELECT t,s,e FROM cache WHERE k=$k";
            cmd.Parameters.AddWithValue("$k", k);
            using var r = cmd.ExecuteReader();
            if (!r.Read()) return null;
            var v = new CachedTranslation(r.GetString(0), r.IsDBNull(1) ? src : r.GetString(1), r.IsDBNull(2) ? "" : r.GetString(2));
            _mem[k] = v;
            return v;
        }
    }

    public void Put(string text, string src, string tgt, CachedTranslation value)
    {
        var k = Key(text, src, tgt);
        _mem[k] = value;
        if (_db == null) return;
        lock (_dbLock)
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = "INSERT OR REPLACE INTO cache(k,t,s,e,ts) VALUES($k,$t,$s,$e,$ts)";
            cmd.Parameters.AddWithValue("$k", k);
            cmd.Parameters.AddWithValue("$t", value.Text);
            cmd.Parameters.AddWithValue("$s", value.DetectedSource);
            cmd.Parameters.AddWithValue("$e", value.EngineId);
            cmd.Parameters.AddWithValue("$ts", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            cmd.ExecuteNonQuery();
        }
    }

    public void Clear()
    {
        _mem.Clear();
        if (_db != null) lock (_dbLock) Exec("DELETE FROM cache");
    }

    private void Exec(string sql)
    {
        using var cmd = _db!.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    public void Dispose() => _db?.Dispose();
}
