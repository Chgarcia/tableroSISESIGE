using Microsoft.Data.Sqlite;
using SigeSiseEjecutivo.Models;

namespace SigeSiseEjecutivo.Services;

public sealed class Database
{
    private readonly string _connectionString;
    public string DatabasePath { get; }

    public Database(IWebHostEnvironment env, IConfiguration cfg)
    {
        var configured = cfg["Dashboard:DatabasePath"] ?? "App_Data/sige-sise.db";
        DatabasePath = Path.IsPathRooted(configured) ? configured : Path.Combine(env.ContentRootPath, configured);
        Directory.CreateDirectory(Path.GetDirectoryName(DatabasePath)!);
        _connectionString = new SqliteConnectionStringBuilder { DataSource = DatabasePath, Mode = SqliteOpenMode.ReadWriteCreate }.ToString();
    }

    public async Task InitializeAsync()
    {
        await using var c = new SqliteConnection(_connectionString);
        await c.OpenAsync();
        var cmd = c.CreateCommand();
        cmd.CommandText = """
        PRAGMA journal_mode=WAL;
        CREATE TABLE IF NOT EXISTS TrackingRecords (
          RowId INTEGER PRIMARY KEY AUTOINCREMENT,
          System TEXT NOT NULL,
          ItemNumber INTEGER NOT NULL,
          Session TEXT NOT NULL DEFAULT '',
          Areas TEXT NOT NULL DEFAULT '',
          Organ TEXT NOT NULL DEFAULT '',
          Contact TEXT NOT NULL DEFAULT '',
          Circuit TEXT NOT NULL DEFAULT '',
          Topic TEXT NOT NULL DEFAULT '',
          Type TEXT NOT NULL DEFAULT '',
          Request TEXT NOT NULL DEFAULT '',
          Agreements TEXT NOT NULL DEFAULT '',
          Followup TEXT NOT NULL DEFAULT '',
          Status TEXT NOT NULL DEFAULT ''
        );
        CREATE INDEX IF NOT EXISTS IX_Tracking_System ON TrackingRecords(System);
        CREATE INDEX IF NOT EXISTS IX_Tracking_Session ON TrackingRecords(Session);
        CREATE TABLE IF NOT EXISTS Imports (
          ImportId INTEGER PRIMARY KEY AUTOINCREMENT,
          ImportedAtUtc TEXT NOT NULL,
          FileName TEXT NOT NULL,
          ImportedBy TEXT NOT NULL,
          SiseCount INTEGER NOT NULL,
          SigeCount INTEGER NOT NULL
        );
        """;
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<IReadOnlyList<TrackingRecord>> GetRecordsAsync(string system)
    {
        var result = new List<TrackingRecord>();
        await using var c = new SqliteConnection(_connectionString);
        await c.OpenAsync();
        var cmd = c.CreateCommand();
        cmd.CommandText = """
        SELECT RowId,System,ItemNumber,Session,Areas,Organ,Contact,Circuit,Topic,Type,Request,Agreements,Followup,Status
        FROM TrackingRecords WHERE System=$system ORDER BY ItemNumber, RowId;
        """;
        cmd.Parameters.AddWithValue("$system", system);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            result.Add(new TrackingRecord(r.GetInt64(0),r.GetString(1),r.GetInt32(2),r.GetString(3),r.GetString(4),r.GetString(5),r.GetString(6),r.GetString(7),r.GetString(8),r.GetString(9),r.GetString(10),r.GetString(11),r.GetString(12),r.GetString(13)));
        return result;
    }

    public async Task<Dictionary<string,int>> GetCountsAsync()
    {
        var d = new Dictionary<string,int>(StringComparer.OrdinalIgnoreCase) { ["SISE"] = 0, ["SIGE"] = 0 };
        await using var c = new SqliteConnection(_connectionString); await c.OpenAsync();
        var cmd = c.CreateCommand(); cmd.CommandText = "SELECT System, COUNT(*) FROM TrackingRecords GROUP BY System";
        await using var r = await cmd.ExecuteReaderAsync(); while (await r.ReadAsync()) d[r.GetString(0)] = r.GetInt32(1);
        return d;
    }

    public async Task<(string? At,string? FileName,string? User)> GetLastImportAsync()
    {
        await using var c = new SqliteConnection(_connectionString); await c.OpenAsync();
        var cmd = c.CreateCommand(); cmd.CommandText = "SELECT ImportedAtUtc,FileName,ImportedBy FROM Imports ORDER BY ImportId DESC LIMIT 1";
        await using var r = await cmd.ExecuteReaderAsync();
        return await r.ReadAsync() ? (r.GetString(0),r.GetString(1),r.GetString(2)) : (null,null,null);
    }

    public async Task ReplaceAllAsync(IEnumerable<ImportRow> rows, string fileName, string importedBy)
    {
        var list = rows.ToList();
        await using var c = new SqliteConnection(_connectionString); await c.OpenAsync();
        await using var tx = await c.BeginTransactionAsync();
        try
        {
            var clear = c.CreateCommand(); clear.Transaction = (SqliteTransaction)tx; clear.CommandText = "DELETE FROM TrackingRecords"; await clear.ExecuteNonQueryAsync();
            foreach (var x in list)
            {
                var cmd = c.CreateCommand(); cmd.Transaction = (SqliteTransaction)tx;
                cmd.CommandText = """
                INSERT INTO TrackingRecords(System,ItemNumber,Session,Areas,Organ,Contact,Circuit,Topic,Type,Request,Agreements,Followup,Status)
                VALUES($sys,$num,$ses,$areas,$organ,$contact,$cto,$topic,$type,$request,$agreements,$followup,$status)
                """;
                cmd.Parameters.AddWithValue("$sys",x.System); cmd.Parameters.AddWithValue("$num",x.ItemNumber); cmd.Parameters.AddWithValue("$ses",x.Session);
                cmd.Parameters.AddWithValue("$areas",x.Areas); cmd.Parameters.AddWithValue("$organ",x.Organ); cmd.Parameters.AddWithValue("$contact",x.Contact); cmd.Parameters.AddWithValue("$cto",x.Circuit);
                cmd.Parameters.AddWithValue("$topic",x.Topic); cmd.Parameters.AddWithValue("$type",x.Type); cmd.Parameters.AddWithValue("$request",x.Request); cmd.Parameters.AddWithValue("$agreements",x.Agreements);
                cmd.Parameters.AddWithValue("$followup",x.Followup); cmd.Parameters.AddWithValue("$status",x.Status); await cmd.ExecuteNonQueryAsync();
            }
            var log = c.CreateCommand(); log.Transaction = (SqliteTransaction)tx;
            log.CommandText = "INSERT INTO Imports(ImportedAtUtc,FileName,ImportedBy,SiseCount,SigeCount) VALUES($at,$file,$user,$sise,$sige)";
            log.Parameters.AddWithValue("$at",DateTimeOffset.UtcNow.ToString("O")); log.Parameters.AddWithValue("$file",fileName); log.Parameters.AddWithValue("$user",importedBy);
            log.Parameters.AddWithValue("$sise",list.Count(x=>x.System=="SISE")); log.Parameters.AddWithValue("$sige",list.Count(x=>x.System=="SIGE")); await log.ExecuteNonQueryAsync();
            await tx.CommitAsync();
        }
        catch { await tx.RollbackAsync(); throw; }
    }
}
