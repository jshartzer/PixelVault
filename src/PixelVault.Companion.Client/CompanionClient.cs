using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using PixelVaultNative;

namespace PixelVault.Companion.Client;

public sealed class CompanionRequestException(HttpStatusCode status,string message):Exception(message)
{
    public HttpStatusCode Status {get;}=status;
}
public sealed class CompanionTrackerDocument
{
    public AchievementTrackerDefinition Definition {get;init;}=new();
    public List<AchievementTrackerState> States {get;init;}=[];
    public Dictionary<string,TrackerFieldVersions> Versions {get;init;}=[];
    public HashSet<string> ProtectedItems {get;init;}=[];
    public bool ReadOnly {get;init;}
    public bool Sample {get;init;}
}
public sealed record CompanionSaveResult(TrackerMutation Result,CompanionTrackerDocument Tracker);
public sealed record CompanionTrackerSummary(string Id,string Provider,string GameId,string GameTitle,string Title,TrackerTotals Totals)
{
    public override string ToString()=>$"{GameTitle} — {Title}";
}
public sealed record CompanionHistory(long Id,string Kind,string Label,long CreatedUtc,long UndoneUtc);
public sealed record CompanionWorkspace(string Id,string Mode,bool ReadOnly,bool Sample,Dictionary<string,string> Accounts);
public sealed class CompanionWorkspaceChangedException():InvalidOperationException("The server is using a different workspace. Close this connection before opening the new workspace; unsaved edits belong to the previous one.");

/// <summary>Uses a caller-owned HttpClient/cookie handler. Credentials stay in the sign-in request only.</summary>
public sealed class CompanionClient(HttpClient http)
{
    private static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web);
    private string? csrf;
    private string? workspace;

    public async Task<CompanionWorkspace> WorkspaceAsync(CancellationToken cancellation=default)
    {
        var current=await Send<CompanionWorkspace>("workspace",null,cancellation);
        if(workspace is not null && workspace!=current.Id)
            throw new CompanionWorkspaceChangedException();
        workspace=current.Id;
        return current;
    }
    public Task<List<CompanionTrackerSummary>> TrackersAsync(CancellationToken cancellation=default)=>Send<List<CompanionTrackerSummary>>("trackers",null,cancellation);
    public Task<List<CompanionHistory>> HistoryAsync(string tracker,CancellationToken cancellation=default)=>
        Send<List<CompanionHistory>>("trackers/"+Uri.EscapeDataString(tracker)+"/history",null,cancellation);

    public async Task SignInAsync(string name,string password,CancellationToken cancellation=default)
    {
        csrf=(await Send<JsonElement>("antiforgery",null,cancellation)).GetProperty("token").GetString();
        await Send<JsonElement>("login",new{name,password},cancellation);
        csrf=(await Send<JsonElement>("antiforgery",null,cancellation)).GetProperty("token").GetString();
    }
    public async Task SignOutAsync(CancellationToken cancellation=default)
    {
        await Send<JsonElement>("logout",new{},cancellation);csrf=null;
    }
    public Task<CompanionTrackerDocument> TrackerAsync(string tracker,CancellationToken cancellation=default)=>
        Send<CompanionTrackerDocument>("trackers/"+Uri.EscapeDataString(tracker),null,cancellation);
    public Task<CompanionSaveResult> EditAsync(string tracker,TrackerEdit command,CancellationToken cancellation=default)=>
        Send<CompanionSaveResult>("trackers/"+Uri.EscapeDataString(tracker)+"/items",command,cancellation);
    public Task<CompanionSaveResult> UndoAsync(string tracker,long history,string requestId,CancellationToken cancellation=default)=>
        Send<CompanionSaveResult>($"trackers/{Uri.EscapeDataString(tracker)}/history/{history}/undo",new{requestId},cancellation);

    private async Task<T> Send<T>(string path,object? body,CancellationToken cancellation)
    {
        if(http.BaseAddress is null || http.BaseAddress.Scheme is not ("https" or "http") || http.BaseAddress.AbsolutePath!="/"
            || http.BaseAddress.UserInfo.Length>0 || http.BaseAddress.Query.Length>0 || http.BaseAddress.Fragment.Length>0)
            throw new InvalidOperationException("Configure the Companion server root as HttpClient.BaseAddress.");
        using var request=new HttpRequestMessage(body is null?HttpMethod.Get:HttpMethod.Post,"api/v1/"+path);
        if(workspace is not null && path is not ("login" or "logout" or "antiforgery" or "workspace"))
            request.Headers.TryAddWithoutValidation("X-Companion-Workspace",workspace);
        if(body is not null){request.Content=JsonContent.Create(body,options:Json);request.Headers.TryAddWithoutValidation("X-CSRF-TOKEN",csrf);}
        using var response=await http.SendAsync(request,cancellation);
        if(!response.IsSuccessStatusCode)
        {
            string message="The Companion request failed.";
            try{var error=await response.Content.ReadFromJsonAsync<JsonElement>(Json,cancellation);message=error.GetProperty("error").GetString()??message;}
            catch(JsonException){}catch(KeyNotFoundException){}
            throw new CompanionRequestException(response.StatusCode,message);
        }
        return await response.Content.ReadFromJsonAsync<T>(Json,cancellation)??throw new InvalidDataException("Companion returned an empty response.");
    }
}
