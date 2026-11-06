using PixelVault.Companion;
using PixelVault.Companion.Local;

namespace PixelVault.Companion.Export;

// The exporter and direct-local API now share the same portable canonical reader.
public static class AchievementSnapshotExporter
{
    public static Dictionary<string,string> ConnectedAccounts(string root,Dictionary<string,string> trackerAccounts)=>LocalAchievementReader.ConnectedAccounts(root,trackerAccounts);
    public static AchievementGroups Groups(string category,string dlc,int order)=>LocalAchievementReader.Groups(category,dlc,order);
    public static IReadOnlyList<AchievementGameSync> Read(string root,string workspace,Dictionary<string,string> accounts)=>LocalAchievementReader.Read(root,workspace,accounts);
}
