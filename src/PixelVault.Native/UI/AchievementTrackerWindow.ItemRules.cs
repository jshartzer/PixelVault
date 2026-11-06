using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace PixelVaultNative;

sealed partial class AchievementTrackerWindow
{
    readonly CheckBox itemMissable = new() { Content = "Missable", Margin = new Thickness(0, 5, 0, 5) };
    readonly CheckBox itemRequired = new() { Content = "Required for completion", Margin = new Thickness(0, 5, 0, 5) };
    readonly Button saveItemRules = Button("Save item settings", "Save missable and completion requirements for this item");

    void BuildItemRules(Panel detail)
    {
        detail.Children.Add(Text("Item settings", 16, true));
        itemMissable.Foreground = itemRequired.Foreground = Foreground;
        AutomationProperties.SetName(itemMissable, "Missable tracker item");
        AutomationProperties.SetName(itemRequired, "Required for completion");
        detail.Children.Add(itemMissable); detail.Children.Add(itemRequired);
        detail.Children.Add(saveItemRules);
        saveItemRules.Click += async (_, _) => await (PendingEdit = SaveItemRulesAsync());
    }

    void ShowItemRules(AchievementTrackerState item, bool enabled)
    {
        itemMissable.IsChecked = item?.Item.Missable == true;
        itemRequired.IsChecked = item?.Item.ExcludedFromTotal != true;
        itemMissable.IsEnabled = itemRequired.IsEnabled = saveItemRules.IsEnabled = enabled;
    }

    bool ItemRulesDraftPending() => selectedItem != null &&
        (itemMissable.IsChecked != selectedItem.Item.Missable || itemRequired.IsChecked == selectedItem.Item.ExcludedFromTotal);

    async Task SaveItemRulesAsync()
    {
        if (selected == null || selectedItem == null) return;
        var tracker = selected; var item = selectedItem.Item; var note = notes.Text; var count = current.Text;
        var edit = new TrackerItemRuleEdit(item.Id, itemMissable.IsChecked == true, itemRequired.IsChecked == true,
            TrackerRepository.ItemRulesVersion(tracker, item.Id));
        saveItemRules.IsEnabled = false;
        try
        {
            await Task.Run(() => store.SetItemRules(tracker.Id, edit));
            if (selected?.Id != tracker.Id) return;
            await ReloadAsync();
            if (selectedItem?.Item.Id == item.Id) { notes.Text = note; current.Text = count; }
            status.Text = "Item settings saved. Centurian progress updates after the next game check.";
        }
        catch (Exception ex) { Error(ex.Message); }
        finally { saveItemRules.IsEnabled = selectedItem != null; }
    }
}
