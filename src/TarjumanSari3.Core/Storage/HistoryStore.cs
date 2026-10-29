using Microsoft.Data.Sqlite;

namespace TarjumanSari3.Core.Storage;

public sealed record HistoryItem(long Id, string Source, string Translation, string From, string To, DateTime Time, bool Favorite, string Engine, string Kind);

/// <summary>Local translation history and favourites (never leaves the machine).</summary>
public sealed class HistoryStore : IDisposable
{
    private readonly SqliteConnection _db;
    private readonly object _lock = new();

    public HistoryStore(string dbPath)
    {
        _db = new SqliteConnection($"Data Source={dbPath}");
        _db.Open();
        Exec("CREATE TABLE IF NOT EXISTS history(id INTEGER PRIMARY KEY AUTOINCREMENT, src TEXT, tr TEXT, f TEXT, t TEXT, ts INTEGER, fav INTEGER DEFAULT 0)");
        try { Exec("ALTER TABLE history ADD COLUMN kind TEXT DEFAULT \u0027text\u0027"); } catch (SqliteException) { /* column already exists */ }
        try { Exec("ALTER TABLE history ADD COLUMN eng TEXT DEFAULT \u0027\u0027"); } catch (SqliteException) { /* column already exists */ }
    }

    public void Add(string src, string tr, string from, string to, string engine = "", string kind = "text")
    {
        lock (_lock)
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = "INSERT INTO history(src,tr,f,t,ts,eng,kind) VALUES($s,$r,$f,$t,$ts,$e,$k)";
            cmd.Parameters.AddWithValue("$s", src);
            cmd.Parameters.AddWithValue("$r", tr);
            cmd.Parameters.AddWithValue("$f", from);
            cmd.Parameters.AddWithValue("$t", to);
            cmd.Parameters.AddWithValue("$ts", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            cmd.Parameters.AddWithValue("$e", engine);
            cmd.Parameters.AddWithValue("$k", kind);
            cmd.ExecuteNonQuery();
        }
    }

    public List<HistoryItem> Search(string? query, bool favoritesOnly, int limit = 200)
    {
        lock (_lock)
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = "SELECT id,src,tr,f,t,ts,fav,COALESCE(eng,\u0027\u0027),COALESCE(kind,\u0027text\u0027) FROM history WHERE (src LIKE $q OR tr LIKE $q)"
                              + (favoritesOnly ? " AND fav=1" : "") + " ORDER BY id DESC LIMIT $n";
            cmd.Parameters.AddWithValue("$q", "%" + (query ?? "") + "%");
            cmd.Parameters.AddWithValue("$n", limit);
            var list = new List<HistoryItem>();
            using var r = cmd.ExecuteReader();
            while (r.Read())
                list.Add(new(r.GetInt64(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4),
                    DateTimeOffset.FromUnixTimeSeconds(r.GetInt64(5)).LocalDateTime, r.GetInt32(6) == 1, r.GetString(7), r.GetString(8)));
            return list;
        }
    }

    public void SetFavorite(long id, bool fav) => Exec($"UPDATE history SET fav={(fav ? 1 : 0)} WHERE id={id}");
    public void Delete(long id) => Exec($"DELETE FROM history WHERE id={id}");
    public void Clear(bool keepFavorites = true) => Exec(keepFavorites ? "DELETE FROM history WHERE fav=0" : "DELETE FROM history");

    private void Exec(string sql)
    {
        lock (_lock)
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = sql;
            cmd.ExecuteNonQuery();
        }
    }

    public void Dispose() => _db.Dispose();
}


