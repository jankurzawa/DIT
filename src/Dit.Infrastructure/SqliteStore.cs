using System.Text.Json;
using Dit.Core;
using Microsoft.Data.Sqlite;

namespace Dit.Infrastructure;

public sealed class SqliteStore : IStore, IDisposable
{
    private readonly SqliteConnection connection;
    private readonly object gate=new();
    private SqliteTransaction? transaction;
    public void Atomic(Action action)
    {
        lock(gate)
        {
            if(transaction is not null){action();return;}
            using var pending=connection.BeginTransaction();transaction=pending;
            try{action();pending.Commit();}catch{pending.Rollback();throw;}finally{transaction=null;}
        }
    }
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true, Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };
    public SqliteStore(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        connection=new SqliteConnection(new SqliteConnectionStringBuilder { DataSource=path, Mode=SqliteOpenMode.ReadWriteCreate, Pooling=false }.ToString());
        connection.Open();
        using var cmd=connection.CreateCommand();
        cmd.CommandText="PRAGMA journal_mode=WAL; PRAGMA synchronous=FULL; PRAGMA busy_timeout=5000; CREATE TABLE IF NOT EXISTS documents(kind TEXT NOT NULL,id TEXT NOT NULL,json TEXT NOT NULL,PRIMARY KEY(kind,id)); PRAGMA user_version=1;";
        cmd.ExecuteNonQuery();
    }
    public void Put<T>(string kind,string id,T value)
    {
        lock(gate)
        {
            using var cmd=connection.CreateCommand();
            cmd.Transaction=transaction;
            cmd.CommandText="INSERT INTO documents(kind,id,json) VALUES($k,$i,$j) ON CONFLICT(kind,id) DO UPDATE SET json=excluded.json";
            cmd.Parameters.AddWithValue("$k",kind);cmd.Parameters.AddWithValue("$i",id);cmd.Parameters.AddWithValue("$j",JsonSerializer.Serialize(value,Json));cmd.ExecuteNonQuery();
        }
    }
    public T? Get<T>(string kind,string id)
    {
        lock(gate)
        {
            using var cmd=connection.CreateCommand();cmd.Transaction=transaction;cmd.CommandText="SELECT json FROM documents WHERE kind=$k AND id=$i";
            cmd.Parameters.AddWithValue("$k",kind);cmd.Parameters.AddWithValue("$i",id);
            return cmd.ExecuteScalar() is string json ? JsonSerializer.Deserialize<T>(json,Json):default;
        }
    }
    public T[] All<T>(string kind)
    {
        lock(gate)
        {
            using var cmd=connection.CreateCommand();cmd.Transaction=transaction;cmd.CommandText="SELECT json FROM documents WHERE kind=$k ORDER BY rowid";cmd.Parameters.AddWithValue("$k",kind);
            using var reader=cmd.ExecuteReader();List<T> values=[];
            while(reader.Read()) values.Add(JsonSerializer.Deserialize<T>(reader.GetString(0),Json)!);
            return values.ToArray();
        }
    }
    public void Backup(string destination)
    {
        lock(gate)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            using var target=new SqliteConnection(new SqliteConnectionStringBuilder {DataSource=destination,Pooling=false}.ToString());target.Open();connection.BackupDatabase(target);
        }
    }
    public void Dispose()=>connection.Dispose();
}
