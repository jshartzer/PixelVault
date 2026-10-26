using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using PixelVault.Companion.Client;

namespace PixelVaultNative
{
    /// <summary>Explicit prepared-workspace connection. This surface never opens a local tracker repository.</summary>
    sealed class CompanionWindow : Window
    {
        readonly string settingsRoot;
        readonly TextBox address=new(){MinWidth=260},user=new(){MinWidth=125},search=new(),count=new(){MaxLength=7};
        readonly PasswordBox password=new(){MinWidth=135,MaxLength=128,Padding=new Thickness(8)};
        readonly CheckBox allowHttp=new(){Content="Allow HTTP on my home network"},completion=new(){Content="Completed"};
        readonly ComboBox trackers=new(){Width=440,MinWidth=240};
        readonly WrapPanel loginPanel=new(){Margin=new Thickness(0,12,0,0)};
        readonly TextBlock feedback=Text("Enter your Companion server address and sign in.",14),workspace=Text("Prepared workspace preview",14),summary=Text("Trackers",22,true),detail=Text("Select an item",20,true),source=Text("",14),conflictText=Text("",14);
        readonly TextBox note=new(){AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,MinHeight=100,MaxHeight=160,MaxLength=20000,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
        readonly ListBox items=new(){HorizontalContentAlignment=HorizontalAlignment.Stretch},history=new(){MaxHeight=160};
        readonly StackPanel editorPanel=new(),conflictPanel=new(),historyPanel=new();
        readonly Button signIn,signOut,reauth,historyButton,reload,saveNote,saveProgress,useSource,retry,useSaved,saveMine,undo,discard;
        readonly Dictionary<string,CompanionEditor> editors=new(StringComparer.Ordinal);
        readonly Dictionary<(string Tracker,string Item),string> rawCounts=new();
        HttpClient http;CompanionClient client;CompanionWorkspace connectedWorkspace;CompanionEditor editor;
        bool busy,connected,rendering,closed,reauthenticating;
        internal Task PendingRequest {get;private set;}=Task.CompletedTask;
        sealed record ItemRow(AchievementTrackerState State)
        {
            public override string ToString()=>State.Item.Name+(string.IsNullOrEmpty(State.Item.Milestone)?"":" · "+State.Item.Milestone)+"   · "+(State.IsKnown?$"{State.Current} / {State.Item.Target}":"Not checked");
        }
        sealed record HistoryRow(CompanionHistory Entry)
        {
            public override string ToString()=>new DateTime(Entry.CreatedUtc,DateTimeKind.Utc).ToLocalTime().ToString("g")+" · "+Entry.Label+(Entry.UndoneUtc>0?" · Undone":"");
        }
        AchievementTrackerState Selected=>(items.SelectedItem as ItemRow)?.State;
        bool Unsaved=>rawCounts.Count>0||editors.Values.Any(e=>e.HasDrafts||e.HasUncertainSave);
        bool Uncertain=>editors.Values.Any(e=>e.HasUncertainSave);
        internal CompanionWindow(Window owner,string settingsRoot)
        {
            this.settingsRoot=settingsRoot;if(owner?.IsVisible==true)Owner=owner;
            Title="PixelVault Companion — desktop connection";Width=1120;Height=860;MinWidth=800;MinHeight=660;
            WindowStartupLocation=Owner is null?WindowStartupLocation.CenterScreen:WindowStartupLocation.CenterOwner;
            Background=SystemParameters.HighContrast?SystemColors.WindowBrush:AchievementTrackerWindow.Color("#11181D");
            TrackerFormStyle.Apply(this);
            if(!SystemParameters.HighContrast){password.Background=AchievementTrackerWindow.Color("#17252D");password.Foreground=AchievementTrackerWindow.Color("#D9E6ED");password.BorderBrush=AchievementTrackerWindow.Color("#415561");}
            password.MinHeight=38;search.MinHeight=38;conflictPanel.Visibility=Visibility.Collapsed;
            var root=new DockPanel{Margin=new Thickness(22)};Content=root;
            var header=new StackPanel();DockPanel.SetDock(header,Dock.Top);root.Children.Add(header);
            header.Children.Add(Text("PixelVault Companion",28,true));
            header.Children.Add(Text("Connect to the workspace shared with your browser. This preview uses a separate copy of your desktop trackers.",14));
            var login=loginPanel;header.Children.Add(login);
            login.Children.Add(Field("Server address",address));login.Children.Add(Field("Name",user));login.Children.Add(Field("Password",password));
            signIn=Button("Sign in",ConnectAsync);login.Children.Add(signIn);signOut=Button("Disconnect",DisconnectAsync);
            allowHttp.Margin=new Thickness(0,6,0,8);header.Children.Add(allowHttp);header.Children.Add(workspace);
            var commands=new WrapPanel{Margin=new Thickness(0,12,0,8)};header.Children.Add(commands);
            commands.Children.Add(trackers);reload=Button("Reload",ReloadAsync);commands.Children.Add(reload);
            historyButton=Button("History",HistoryAsync);commands.Children.Add(historyButton);retry=Button("Retry unconfirmed save",RetryAsync);commands.Children.Add(retry);
            commands.Children.Add(signOut);
            reauth=Button("Sign in again",()=>{reauthenticating=true;UpdateControls();return Task.CompletedTask;});commands.Children.Add(reauth);
            header.Children.Add(summary);search.Margin=new Thickness(0,10,0,10);search.ToolTip="Search tracker items and notes";header.Children.Add(search);
            var footer=new DockPanel{Margin=new Thickness(0,10,0,0)};DockPanel.SetDock(footer,Dock.Bottom);root.Children.Add(footer);
            var close=AchievementTrackerWindow.Button("Close","Close Companion");close.Click+=(_,__)=>Close();DockPanel.SetDock(close,Dock.Right);footer.Children.Add(close);footer.Children.Add(feedback);
            AutomationProperties.SetLiveSetting(feedback,AutomationLiveSetting.Polite);
            var body=new Grid();body.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});body.ColumnDefinitions.Add(new(){Width=new GridLength(330)});root.Children.Add(body);
            VirtualizingPanel.SetIsVirtualizing(items,true);VirtualizingPanel.SetVirtualizationMode(items,VirtualizationMode.Recycling);ScrollViewer.SetHorizontalScrollBarVisibility(items,ScrollBarVisibility.Disabled);body.Children.Add(items);
            editorPanel.Margin=new Thickness(18,0,0,0);var scroll=new ScrollViewer{Content=editorPanel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};Grid.SetColumn(scroll,1);body.Children.Add(scroll);
            editorPanel.Children.Add(detail);editorPanel.Children.Add(source);editorPanel.Children.Add(completion);editorPanel.Children.Add(Field("Manual count",count));
            saveProgress=Button("Save progress",SaveProgressAsync);editorPanel.Children.Add(saveProgress);useSource=Button("Use source value",UseSourceAsync);editorPanel.Children.Add(useSource);
            editorPanel.Children.Add(Field("Note",note));saveNote=Button("Save note",()=>SaveAsync("note"));editorPanel.Children.Add(saveNote);
            discard=Button("Discard this item's edits",DiscardAsync);editorPanel.Children.Add(discard);
            conflictPanel.Margin=new Thickness(0,14,0,10);conflictPanel.Children.Add(Text("This value changed",17,true));conflictPanel.Children.Add(conflictText);
            useSaved=Button("Use saved value",UseSavedAsync);saveMine=Button("Save my edit",SaveMineAsync);conflictPanel.Children.Add(useSaved);conflictPanel.Children.Add(saveMine);editorPanel.Children.Add(conflictPanel);
            historyPanel.Margin=new Thickness(0,14,0,0);historyPanel.Children.Add(Text("History",18,true));historyPanel.Children.Add(history);undo=Button("Undo selected change",UndoAsync);historyPanel.Children.Add(undo);editorPanel.Children.Add(historyPanel);historyPanel.Visibility=Visibility.Collapsed;
            foreach(var box in new[]{address,user,search,count,note})AchievementTrackerWindow.DressEditor(box);
            AutomationProperties.SetName(address,"Companion server address");AutomationProperties.SetName(user,"Companion account name");AutomationProperties.SetName(password,"Companion password");
            AutomationProperties.SetName(trackers,"Companion tracker");AutomationProperties.SetName(items,"Companion tracker items");AutomationProperties.SetName(count,"Companion manual count");AutomationProperties.SetName(note,"Companion note");AutomationProperties.SetName(search,"Search Companion items");AutomationProperties.SetName(history,"Companion history");
            trackers.SelectionChanged+=async(_,__)=>{if(!rendering)await(PendingRequest=RunAsync(SelectTrackerAsync));};
            items.SelectionChanged+=(_,__)=>{if(!rendering)RenderDetail();};search.TextChanged+=(_,__)=>RenderItems();history.SelectionChanged+=(_,__)=>UpdateControls();
            note.TextChanged+=(_,__)=>{if(!rendering&&Selected is not null&&editor is not null){editor.Note(Selected.Item.Id,note.Text);feedback.Text="Unsaved note.";UpdateControls();}};
            count.TextChanged+=(_,__)=>{if(!rendering&&Selected is not null&&editor is not null){rawCounts[(editor.Document.Definition.Id,Selected.Item.Id)]=count.Text;if(int.TryParse(count.Text,out var value)&&value>=0&&value<=1_000_000){editor.Progress(Selected.Item.Id,value);feedback.Text="Unsaved progress.";}else feedback.Text="Enter a whole number from 0 to 1,000,000.";UpdateControls();}};
            completion.Click+=(_,__)=>{if(Selected is not null){editor.Progress(Selected.Item.Id,completion.IsChecked==true?Selected.Item.Target:0);rawCounts.Remove((editor.Document.Definition.Id,Selected.Item.Id));RenderDetail();feedback.Text="Unsaved progress.";}};
            password.KeyDown+=async(_,e)=>{if(e.Key==System.Windows.Input.Key.Enter){e.Handled=true;await(PendingRequest=RunAsync(ConnectAsync));}};
            Closing+=(_,e)=>
            {
                if(busy){e.Cancel=true;feedback.Text="Wait for the current request to finish.";return;}
                if(Unsaved&&MessageBox.Show(this,Uncertain?"A save has not been confirmed. Close and discard these local drafts? The server may already have accepted the save.":"Close and discard unsaved edits?","PixelVault Companion",MessageBoxButton.YesNo,MessageBoxImage.Question)!=MessageBoxResult.Yes)e.Cancel=true;
            };
            Closed+=(_,__)=>{closed=true;http?.Dispose();password.Clear();};
            try{var saved=CompanionDesktopSettings.Load(settingsRoot);address.Text=saved.Server;allowHttp.IsChecked=saved.AllowHttp;}catch(Exception e){feedback.Text=e.Message;}
            UpdateControls();
        }
        static TextBlock Text(string value,double size,bool bold=false)=>AchievementTrackerWindow.Text(value,size,bold);
        static StackPanel Field(string label,FrameworkElement input)
        {
            if(input is TextBox textBox)textBox.MinHeight=Math.Max(38,textBox.MinHeight);
            var panel=new StackPanel{Margin=new Thickness(0,0,10,8)};panel.Children.Add(Text(label,13));panel.Children.Add(input);return panel;
        }
        Button Button(string label,Func<Task> action)
        {
            var button=AchievementTrackerWindow.Button(label,label);button.Margin=new Thickness(0,4,8,4);button.MinHeight=36;button.Click+=async(_,__)=>await(PendingRequest=RunAsync(action));return button;
        }
        async Task RunAsync(Func<Task> action)
        {
            if(busy||closed)return;busy=true;UpdateControls();
            try{await action();}
            catch(Exception e)
            {
                if(e is HttpRequestException or OperationCanceledException or CompanionWorkspaceChangedException || e is CompanionRequestException request&&((int)request.Status>=500||request.Status==HttpStatusCode.Unauthorized))connected=false;
                if(editor?.Conflict is not null&&!editor.ConflictReady)connected=false;
                feedback.Text=(Uncertain?"Save unconfirmed. Reconnect and retry the same save. ":!connected&&client is not null?"Disconnected · showing previously loaded values. ":"")+e.Message;
                RenderDetail();
            }
            finally{busy=false;if(!closed)UpdateControls();}
        }
        async Task ConnectAsync()
        {
            var server=CompanionConnection.Server(address.Text,allowHttp.IsChecked==true);
            if(http is not null&&http.BaseAddress!=server)
            {
                if(Unsaved)throw new InvalidOperationException("Close this connection before changing servers; it still has unsaved edits.");
                http.Dispose();http=null;client=null;editors.Clear();editor=null;
            }
            http??=CompanionConnection.CreateHttpClient(server);client??=new CompanionClient(http);
            connected=false;var secret=password.Password;password.Clear();await client.SignInAsync(user.Text,secret);
            connectedWorkspace=await client.WorkspaceAsync();connected=true;reauthenticating=false;
            var choices=await client.TrackersAsync();var selected=(trackers.SelectedItem as CompanionTrackerSummary)?.Id;
            rendering=true;trackers.ItemsSource=choices;trackers.SelectedItem=choices.FirstOrDefault(t=>t.Id==selected)??choices.FirstOrDefault();rendering=false;
            await SelectTrackerAsync();
            workspace.Text=(connectedWorkspace.Sample?"Read-only sample":connectedWorkspace.Mode=="isolated-copy"?"Connected · prepared copy":"Connected")+" · "+string.Join(" · ",connectedWorkspace.Accounts.Select(a=>a.Key+": "+a.Value));
            feedback.Text="Connected to "+server.Authority+". Saves go to this Companion workspace.";
            try{new CompanionDesktopSettings(server.AbsoluteUri,allowHttp.IsChecked==true).Save(settingsRoot);}catch(Exception e){feedback.Text+=" Could not remember the address: "+e.Message;}
        }
        async Task DisconnectAsync()
        {
            if(Unsaved)throw new InvalidOperationException("Save or discard your edits before disconnecting.");
            try{if(client is not null)await client.SignOutAsync();}
            finally{http?.Dispose();http=null;client=null;connected=false;connectedWorkspace=null;editors.Clear();editor=null;trackers.ItemsSource=null;items.ItemsSource=null;history.ItemsSource=null;workspace.Text="Prepared workspace preview";feedback.Text="Disconnected.";RenderDetail();}
        }
        async Task SelectTrackerAsync()
        {
            var selected=trackers.SelectedItem as CompanionTrackerSummary;if(selected is null||client is null)return;
            if(!editors.TryGetValue(selected.Id,out editor)){editor=new(client,selected.Id);editors.Add(selected.Id,editor);}
            items.ItemsSource=null;
            historyPanel.Visibility=Visibility.Collapsed;
            await editor.RefreshAsync();RenderItems();
        }
        async Task ReloadAsync()
        {
            if(editor is null)return;await client.WorkspaceAsync();await editor.RefreshAsync();connected=true;RenderItems();feedback.Text="Reloaded from Companion. Unsaved edits are retained.";
        }
        void RenderItems()
        {
            if(editor is null)return;var selected=Selected?.Item.Id;var query=search.Text.Trim();rendering=true;
            var rows=editor.Document.States.Where(s=>!s.Item.Archived&&(query.Length==0||(s.Item.Name+" "+s.Item.Milestone+" "+s.Item.Category+" "+s.Note).Contains(query,StringComparison.CurrentCultureIgnoreCase))).Select(s=>new ItemRow(s)).ToList();
            items.ItemsSource=rows;items.SelectedItem=rows.FirstOrDefault(r=>r.State.Item.Id==selected)??rows.FirstOrDefault();rendering=false;
            var totals=TrackerTotals.Calculate(editor.Document.Definition,editor.Document.States);summary.Text=editor.Document.Definition.Title+" · "+totals.Completed+" / "+totals.Total+" goals";RenderDetail();
        }
        void RenderDetail()
        {
            rendering=true;var state=Selected;
            if(state is null||editor is null){detail.Text="Select an item";source.Text="";note.Text="";count.Text="";completion.IsChecked=false;}
            else
            {
                string id=state.Item.Id;detail.Text=state.Item.Name+(string.IsNullOrEmpty(state.Item.Milestone)?"":" · "+state.Item.Milestone);
                source.Text="Source: "+(state.ReportedCurrent.HasValue?state.ReportedCurrent+" / "+state.Item.Target:"Not checked")+(editor.Document.ProtectedItems.Contains(id)?" · Game-confirmed":"")+"\n"+state.Item.Detail;
                var progress=editor.Draft(id,"progress");note.Text=editor.Draft(id,"note")?.Note??state.Note??"";
                count.Text=rawCounts.GetValueOrDefault((editor.Document.Definition.Id,id))??(progress is null?state.Current:progress.Field=="source"?state.ReportedCurrent:progress.Current)?.ToString(CultureInfo.InvariantCulture)??"";
                completion.IsChecked=(progress is null?state.Current:progress.Field=="source"?state.ReportedCurrent:progress.Current)>=state.Item.Target;
            }
            conflictPanel.Visibility=editor?.Conflict is null?Visibility.Collapsed:Visibility.Visible;
            if(editor?.Conflict is { } conflict)
            {
                var latest=editor.Document.States.FirstOrDefault(s=>s.Item.Id==conflict.Item);var draft=editor.Draft(conflict.Item,conflict.Field);
                conflictText.Text=(latest?.Item.Name??conflict.Item)+"\nSaved: "+(conflict.Field=="note"?latest?.Note:latest?.Current?.ToString()??"Not checked")+"\nYour edit: "+(conflict.Field=="note"?draft?.Note:draft?.Field=="source"?"Use source value":draft?.Current?.ToString())+(editor.ConflictReady?"":"\nReload to review the current saved value.");
            }
            rendering=false;UpdateControls();
        }
        void UpdateControls()
        {
            loginPanel.Visibility=allowHttp.Visibility=connected&&!reauthenticating?Visibility.Collapsed:Visibility.Visible;
            workspace.Text=(connected?connectedWorkspace?.Sample==true?"Connected · read-only sample":"Connected · prepared copy":client is null?"Prepared workspace preview":"Disconnected · previously loaded values")
                +(http is null?"":" · "+http.BaseAddress.Authority)
                +(connectedWorkspace is null?"":" · "+string.Join(" · ",connectedWorkspace.Accounts.Select(a=>a.Key+": "+a.Value)));
            bool writable=!busy&&connected&&editor is not null&&!editor.Document.ReadOnly&&!Uncertain;var state=Selected;
            address.IsEnabled=!busy&&client is null;allowHttp.IsEnabled=address.IsEnabled;user.IsEnabled=password.IsEnabled=signIn.IsEnabled=!busy;
            signOut.IsEnabled=!busy&&client is not null&&!Unsaved;reload.IsEnabled=!busy&&editor is not null;retry.Visibility=Uncertain?Visibility.Visible:Visibility.Collapsed;retry.IsEnabled=!busy&&connected;
            reauth.Visibility=connected?Visibility.Visible:Visibility.Collapsed;reauth.IsEnabled=!busy;historyButton.IsEnabled=!busy&&connected&&editor is not null&&!editor.Document.Sample;
            trackers.IsEnabled=!busy&&connected&&!Uncertain;items.IsEnabled=!busy&&!Uncertain;search.IsEnabled=!busy;
            note.IsEnabled=writable&&state is not null;count.IsEnabled=writable&&state is not null;completion.Visibility=state?.Item.Target==1?Visibility.Visible:Visibility.Collapsed;
            completion.IsEnabled=writable&&state is not null&&!(state.Complete&&editor.Document.ProtectedItems.Contains(state.Item.Id));
            saveNote.IsEnabled=writable&&state is not null&&editor.Draft(state.Item.Id,"note") is not null;
            saveProgress.IsEnabled=writable&&state is not null&&editor.Draft(state.Item.Id,"progress") is not null&&int.TryParse(count.Text,out var value)&&value>=0&&value<=1_000_000;
            if(state is not null&&editor is not null&&editor.Document.ProtectedItems.Contains(state.Item.Id)&&int.TryParse(count.Text,out var protectedValue)&&protectedValue<state.Item.Target)saveProgress.IsEnabled=false;
            useSource.IsEnabled=writable&&state?.ManualCurrent is not null;
            discard.IsEnabled=!busy&&!Uncertain&&state is not null&&editor is not null&&(editor.Draft(state.Item.Id,"note") is not null||editor.Draft(state.Item.Id,"progress") is not null||rawCounts.ContainsKey((editor.Document.Definition.Id,state.Item.Id)));
            useSaved.IsEnabled=writable&&editor?.ConflictReady==true;saveMine.IsEnabled=useSaved.IsEnabled;
            undo.IsEnabled=writable&&(history.SelectedItem as HistoryRow)?.Entry.UndoneUtc==0;
            if(editor?.Conflict is {Field:"progress",Item:var item}&&editor.Document.ProtectedItems.Contains(item))
            {
                var draft=editor.Draft(item,"progress");if(draft?.Field!="source"&&draft?.Current<editor.Document.States.Single(s=>s.Item.Id==item).Item.Target)saveMine.IsEnabled=false;
            }
        }
        Task SaveProgressAsync()=>SaveAsync("progress");
        Task DiscardAsync()
        {
            var id=Selected.Item.Id;editor.UseSaved(id,"note");editor.UseSaved(id,"progress");rawCounts.Remove((editor.Document.Definition.Id,id));RenderDetail();feedback.Text="Unsaved edits for this item were discarded.";return Task.CompletedTask;
        }
        async Task SaveAsync(string field)
        {
            var id=Selected?.Item.Id??throw new InvalidOperationException("Select an item.");await editor.SaveAsync(id,field);
            if(field=="progress")rawCounts.Remove((editor.Document.Definition.Id,id));RenderItems();feedback.Text=field=="note"?"Note saved to Companion.":"Progress saved to Companion.";
        }
        async Task UseSourceAsync(){editor.Progress(Selected.Item.Id,null);await SaveAsync("progress");}
        async Task UseSavedAsync()
        {
            if(editor.Conflict is not { } conflict)return;editor.UseSaved(conflict.Item,conflict.Field);if(conflict.Field=="progress")rawCounts.Remove((editor.Document.Definition.Id,conflict.Item));RenderItems();feedback.Text="Using the saved value.";await Task.CompletedTask;
        }
        async Task SaveMineAsync()
        {
            if(editor.Conflict is not { } conflict)return;editor.RebaseDraft(conflict.Item,conflict.Field);await editor.SaveAsync(conflict.Item,conflict.Field);if(conflict.Field=="progress")rawCounts.Remove((editor.Document.Definition.Id,conflict.Item));RenderItems();feedback.Text="Your edit was saved to Companion.";
        }
        async Task RetryAsync()
        {
            var pending=editor.UnconfirmedEdit;await editor.RetryAsync();if(pending is not null&&pending.Field!="note")rawCounts.Remove((editor.Document.Definition.Id,pending.ItemId));RenderItems();feedback.Text="Save confirmed by Companion.";
        }
        async Task HistoryAsync(){if(editor is null)return;history.ItemsSource=(await client.HistoryAsync(editor.Document.Definition.Id)).Select(h=>new HistoryRow(h)).ToList();historyPanel.Visibility=Visibility.Visible;}
        async Task UndoAsync(){if((history.SelectedItem as HistoryRow)?.Entry is not { } entry)return;await editor.UndoAsync(entry.Id);RenderItems();await HistoryAsync();feedback.Text="Change undone in Companion.";}
    }
}
