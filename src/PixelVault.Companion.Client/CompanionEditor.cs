using PixelVaultNative;

namespace PixelVault.Companion.Client;

/// <summary>One view's drafts retain their own field versions across refreshes and independent saves.</summary>
public sealed class CompanionEditor(CompanionClient client,string tracker)
{
    private readonly Dictionary<(string Item,string Field),TrackerEdit> drafts=[];
    private PendingCommand? pending;
    private sealed record PendingCommand(TrackerEdit? Edit,long? History,string RequestId);
    public CompanionTrackerDocument Document {get;private set;}=new();
    public bool HasDrafts=>drafts.Count>0;
    public bool HasUncertainSave=>pending is not null;
    public TrackerEdit? UnconfirmedEdit=>pending?.Edit;
    public (string Item,string Field)? Conflict {get;private set;}
    public bool ConflictReady {get;private set;}
    public TrackerEdit? Draft(string item,string field)=>drafts.GetValueOrDefault((item,field));

    public async Task RefreshAsync(CancellationToken cancellation=default)
    {
        Document=await client.TrackerAsync(tracker,cancellation);ConflictReady=Conflict is not null;
    }

    public void Note(string item,string text)=>Set(item,"note",null,text);
    public void Progress(string item,int? current)=>Set(item,"progress",current,null);
    private void Set(string item,string field,int? current,string? note)
    {
        RequireSettled();
        if(Document.ReadOnly)throw new InvalidOperationException("This workspace is read-only.");
        var state=Document.States.Single(s=>s.Item.Id==item&&!s.Item.Archived);
        var previous=Draft(item,field);
        if(previous is null && (field=="note" ? (state.Note??"")==note : current.HasValue && state.Current==current))return;
        if(previous is not null && previous.Current==current && previous.Note==note)return;
        var version=previous?.ExpectedVersion??Version(item,field);
        drafts[(item,field)]=new(item,field=="progress"&&!current.HasValue?"source":field,current,note!,version,Guid.NewGuid().ToString());
    }
    private string Version(string item,string field)=>field=="note"?Document.Versions[item].Note:Document.Versions[item].Progress;
    private void RequireSettled()
    {
        if(HasUncertainSave)throw new InvalidOperationException("Retry the unconfirmed save before changing another value.");
    }
    public void UseSaved(string item,string field)
    {
        RequireSettled();drafts.Remove((item,field));if(Conflict==(item,field))Conflict=null;
    }
    public void RebaseDraft(string item,string field)
    {
        RequireSettled();var draft=Draft(item,field)??throw new InvalidOperationException("There is no draft to save.");
        if(Conflict!=(item,field)||!ConflictReady)throw new InvalidOperationException("Reload and review the conflict before replacing the saved value.");
        drafts[(item,field)]=draft with{ExpectedVersion=Version(item,field),RequestId=Guid.NewGuid().ToString()};Conflict=null;
    }
    public Task<CompanionSaveResult> SaveAsync(string item,string field,CancellationToken cancellation=default)
    {
        RequireSettled();var draft=Draft(item,field)??throw new InvalidOperationException("There is no changed value to save.");
        pending=new(draft,null,draft.RequestId);return SendPending(cancellation);
    }
    public Task<CompanionSaveResult> UndoAsync(long history,CancellationToken cancellation=default)
    {
        RequireSettled();pending=new(null,history,Guid.NewGuid().ToString());return SendPending(cancellation);
    }
    public Task<CompanionSaveResult> RetryAsync(CancellationToken cancellation=default)=>SendPending(cancellation);

    private async Task<CompanionSaveResult> SendPending(CancellationToken cancellation)
    {
        var command=pending??throw new InvalidOperationException("There is no unconfirmed save.");
        try
        {
            var result=command.Edit is not null?await client.EditAsync(tracker,command.Edit,cancellation)
                :await client.UndoAsync(tracker,command.History!.Value,command.RequestId,cancellation);
            Document=result.Tracker;
            if(command.Edit is not null)
            {
                var key=(command.Edit.ItemId,command.Edit.Field=="note"?"note":"progress");
                drafts.Remove(key);if(Conflict==key)Conflict=null;
            }
            pending=null;return result;
        }
        catch(CompanionRequestException e)
        {
            if((int)e.Status>=500)throw;
            pending=null;
            if(e.Status==System.Net.HttpStatusCode.Conflict && command.Edit is not null)
            {
                // Rebase is always a separate, explicit user decision. Refresh cannot change a draft's token.
                Conflict=(command.Edit.ItemId,command.Edit.Field=="note"?"note":"progress");
                ConflictReady=false;
                try{await RefreshAsync(cancellation);}catch{ }
            }
            throw;
        }
        // A missing/unreadable acknowledgement leaves the exact command available for an idempotent retry.
    }
}
