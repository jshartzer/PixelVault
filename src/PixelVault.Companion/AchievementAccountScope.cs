using System.Text.Json;
namespace PixelVault.Companion;

public static class AchievementAccountScope
{
    public static Dictionary<string,string> Read(CompanionOptions options,TrackerWorkspace workspace)
    {
        var result=workspace.Manifest?.Accounts.ToDictionary(p=>p.Key,p=>p.Value)??new();var path=Path.Combine(options.DataRoot,"achievement-accounts.json");
        if(File.Exists(path))foreach(var pair in JsonSerializer.Deserialize<Dictionary<string,string>>(File.ReadAllText(path))??new())
        {if(pair.Key is not ("steam" or "retroachievements")||string.IsNullOrWhiteSpace(pair.Value)||pair.Value.Length>120||pair.Value.Any(char.IsControl)||result.TryGetValue(pair.Key,out var current)&&current!=pair.Value)throw new InvalidDataException("The achievement account scope does not match this workspace.");result[pair.Key]=pair.Value;}
        return result;
    }
    public static void Configure(CompanionOptions options,TrackerWorkspace workspace,Dictionary<string,string> requested)
    {
        if(workspace.ReadOnly||requested.Count==0)throw new InvalidDataException("An approved achievement account is required.");var existing=Read(options,workspace);
        if(existing.Any(p=>!requested.TryGetValue(p.Key,out var value)||value!=p.Value)||requested.Any(p=>p.Key is not ("steam" or "retroachievements")||string.IsNullOrWhiteSpace(p.Value)||p.Value.Length>120||p.Value.Any(char.IsControl)))throw new InvalidDataException("Existing account scopes cannot be replaced by this setup step.");
        var path=Path.Combine(options.DataRoot,"achievement-accounts.json");var temp=path+".tmp";File.WriteAllText(temp,JsonSerializer.Serialize(requested));if(!OperatingSystem.IsWindows())File.SetUnixFileMode(temp,UnixFileMode.UserRead|UnixFileMode.UserWrite);File.Move(temp,path,true);
    }
}
