using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace PixelVaultNative;

public partial class TrackerRepository
{
    public static IReadOnlyList<string> DefaultTrackerGroupOrder => new[]{"achievement","manual","century"};
    public static string TrackerGroup(AchievementTrackerDefinition d) => string.IsNullOrWhiteSpace(d.AchievementId)?"manual"
        :d.AchievementId.StartsWith("century.",StringComparison.Ordinal)?"century":"achievement";
    public static string TrackerGroupTitle(string id) => id switch
    {"achievement"=>"Achievement-linked trackers","manual"=>"Manual trackers","century"=>"Centurian Achievements",_=>throw new InvalidDataException("Unknown tracker group.")};
    static List<string> ReadGroupOrder(SqliteConnection c,SqliteTransaction tx=null)
    {
        using var q=c.CreateCommand();q.Transaction=tx;
        q.CommandText="SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='tracker_group_order'";
        if(Convert.ToInt32(q.ExecuteScalar())==0)return DefaultTrackerGroupOrder.ToList();
        q.CommandText="SELECT value FROM tracker_group_order WHERE id=1";
        if(q.ExecuteScalar() is not string json)return DefaultTrackerGroupOrder.ToList();
        var order=JsonSerializer.Deserialize<List<string>>(json);
        if(order==null||order.Count!=3||order.Distinct().Count()!=3||order.Any(id=>!DefaultTrackerGroupOrder.Contains(id)))throw new InvalidDataException("Invalid tracker group order.");
        return order;
    }
    public IReadOnlyList<string> TrackerGroupOrder(){using var c=Open();return ReadGroupOrder(c);}
    public IReadOnlyList<string> MoveTrackerGroup(string group,int direction)
    {
        if(!DefaultTrackerGroupOrder.Contains(group)||direction is not (-1 or 1))throw new InvalidDataException("Choose a tracker group and move up or down.");
        using var c=Open();using var tx=c.BeginTransaction();var order=ReadGroupOrder(c,tx);
        int index=order.IndexOf(group),next=index+direction;
        if(next>=0&&next<order.Count)
        {
            (order[index],order[next])=(order[next],order[index]);using var q=c.CreateCommand();q.Transaction=tx;
            q.CommandText="INSERT INTO tracker_group_order(id,value) VALUES(1,$v) ON CONFLICT(id) DO UPDATE SET value=$v";
            q.Parameters.AddWithValue("$v",JsonSerializer.Serialize(order));q.ExecuteNonQuery();
        }
        tx.Commit();return order;
    }
}
