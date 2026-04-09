using PixelVaultNative;
using Xunit;

namespace PixelVaultNative.Tests;

public sealed class LibraryRehomeRulesTests
{
    [Fact]
    public void PhotoIndexGameIdChangedForRehome_FirstAssignmentFromEmpty()
    {
        Assert.True(LibraryRehomeRules.PhotoIndexGameIdChangedForRehome(string.Empty, "G00001"));
    }

    [Fact]
    public void PhotoIndexGameIdChangedForRehome_Unchanged()
    {
        Assert.False(LibraryRehomeRules.PhotoIndexGameIdChangedForRehome("G00001", "G00001"));
    }

    [Fact]
    public void PhotoIndexGameIdChangedForRehome_Reassignment()
    {
        Assert.True(LibraryRehomeRules.PhotoIndexGameIdChangedForRehome("G00001", "G00002"));
    }
}
