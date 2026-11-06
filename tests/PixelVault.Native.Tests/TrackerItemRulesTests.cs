using System.IO;
using PixelVaultNative;
using Xunit;

namespace PixelVaultNative.Tests;

public sealed class TrackerItemRulesTests
{
    [Fact] public void RulesPersistAcrossClientsApplyToWholeRecordsAndRejectStaleAndEmptyGoals()
    {
        var root = Path.Combine(Path.GetTempPath(), "pv-item-rules-" + Guid.NewGuid().ToString("N"));
        try
        {
            var repo = new TrackerRepository(root, pooling: false);
            var definition = new AchievementTrackerDefinition { Id = "custom.rules", Provider = "steam", GameId = "42", GameTitle = "Another game", Title = "Tasks",
                Milestones = new() { "One", "Two" }, Items = TrackerMilestones.Expand(new[] { new AchievementTrackerItem { Id = "a", Name = "First", Category = "Tasks" },
                    new AchievementTrackerItem { Id = "b", Name = "Second", Category = "Tasks" } }, new[] { "One", "Two" }) };
            repo.AddDefinition(definition); var item = definition.Items.First();
            repo.SetManualProgress(definition.Id, "account", item.Id, 1); repo.SaveNote(definition.Id, "account", item.Id, "Keep this note");
            var edit = new TrackerItemRuleEdit(item.Id, true, false, TrackerRepository.ItemRulesVersion(definition, item.Id));
            repo.SetItemRules(definition.Id, edit);
            var reloaded = new TrackerRepository(root, pooling: false, initializeSchema: false).Definitions().Single(d => d.Id == definition.Id);
            Assert.All(reloaded.Items.Where(i => i.GroupId == item.GroupId), i => { Assert.True(i.Missable); Assert.True(i.ExcludedFromTotal); });
            Assert.Throws<InvalidOperationException>(() => repo.SetItemRules(definition.Id, edit));
            var last = reloaded.Items.First(i => i.GroupId != item.GroupId);
            Assert.Throws<InvalidDataException>(() => repo.SetItemRules(definition.Id, new(last.Id, false, false, TrackerRepository.ItemRulesVersion(reloaded, last.Id))));
            var saved = repo.Load(definition.Id, "account").Single(s => s.Item.Id == item.Id);
            Assert.Equal(1, saved.ManualCurrent); Assert.Equal("Keep this note", saved.Note);
            Assert.Equal(2, TrackerTotals.Calculate(reloaded, repo.Load(definition.Id, "account")).Total);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
