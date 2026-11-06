using System.Collections.Generic;
using System.Linq;

namespace PixelVaultNative;

public partial class TrackerRepository
{
    // Provider identity is immutable for built-ins. Custom identities are read
    // fresh on each call so deletion and account-scope changes take effect now.
    static readonly IReadOnlyDictionary<string,string> BuiltinProviders =
        AchievementTrackingCatalog.Builtins().ToDictionary(d=>d.Id,d=>d.Provider);

    public string ProviderForTracker(string id)
    {
        if(BuiltinProviders.TryGetValue(id,out var provider))return provider;
        using var c=Open();using var q=c.CreateCommand();
        q.CommandText="SELECT COALESCE(json_extract(definition,'$.Provider'),json_extract(definition,'$.provider')) FROM tracker_definition WHERE id=$id";
        q.Parameters.AddWithValue("$id",id);
        return q.ExecuteScalar() as string;
    }
}
