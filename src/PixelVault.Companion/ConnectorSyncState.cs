using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace PixelVault.Companion;

public sealed class ConnectorSyncState
{
    readonly string path;readonly TrackerWorkspace workspace;
    public ConnectorSyncState(CompanionOptions options,TrackerWorkspace workspace)
    {
        path=Path.Combine(options.DataRoot,"companion.sqlite");this.workspace=workspace;
        using var c=Open();using var cmd=c.CreateCommand();cmd.CommandText="CREATE TABLE IF NOT EXISTS companion_connector_activity(workspace TEXT NOT NULL,name TEXT NOT NULL,value TEXT NOT NULL,updated INTEGER NOT NULL,PRIMARY KEY(workspace,name))";cmd.ExecuteNonQuery();
    }
    SqliteConnection Open(){var c=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=path,Pooling=false}.ToString());c.Open();return c;}
    public void Put(string name,string value="")
    {
        using var c=Open();using var cmd=c.CreateCommand();cmd.CommandText="INSERT INTO companion_connector_activity(workspace,name,value,updated) VALUES($w,$n,$v,$t) ON CONFLICT(workspace,name) DO UPDATE SET value=$v,updated=$t";
        cmd.Parameters.AddWithValue("$w",workspace.Identity);cmd.Parameters.AddWithValue("$n",name);cmd.Parameters.AddWithValue("$v",value);cmd.Parameters.AddWithValue("$t",DateTime.UtcNow.Ticks);cmd.ExecuteNonQuery();
    }
    public (string Value,long Updated) Get(string name)
    {
        using var c=Open();using var cmd=c.CreateCommand();cmd.CommandText="SELECT value,updated FROM companion_connector_activity WHERE workspace=$w AND name=$n";cmd.Parameters.AddWithValue("$w",workspace.Identity);cmd.Parameters.AddWithValue("$n",name);
        using var row=cmd.ExecuteReader();return row.Read()?(row.GetString(0),row.GetInt64(1)):("",0);
    }
    public ConnectorSyncSettings Settings()
    {
        var value=Get("settings").Value;return value.Length==0?new(false,false,workspace.Identity):JsonSerializer.Deserialize<ConnectorSyncSettings>(value)!;
    }
    public ConnectorSyncSettings EnableLibraryAndAutomatic()
    {
        if(workspace.ReadOnly)throw new InvalidDataException("A real workspace is required.");
        var settings=new ConnectorSyncSettings(true,true,workspace.Identity);Put("settings",JsonSerializer.Serialize(settings));return settings;
    }
    public ConnectorSyncSettings SetAutomatic(bool enabled)
    {
        var settings=Settings() with{AutomaticEnabled=enabled};Put("settings",JsonSerializer.Serialize(settings));return settings;
    }
}
