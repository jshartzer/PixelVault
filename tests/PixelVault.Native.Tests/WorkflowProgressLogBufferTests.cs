using PixelVaultNative;
using Xunit;

namespace PixelVaultNative.Tests;

public sealed class WorkflowProgressLogBufferTests
{
    [Fact]
    public void TryRender_BatchesMultipleAppendsIntoOneRenderedText()
    {
        var buffer = new WorkflowProgressLogBuffer(maxLines: 10);

        Assert.True(buffer.Append("first"));
        Assert.True(buffer.Append("second"));
        Assert.True(buffer.Append("third"));

        Assert.True(buffer.TryRender(out var text));
        Assert.Equal("first" + Environment.NewLine + "second" + Environment.NewLine + "third", text);
        Assert.False(buffer.TryRender(out _));
    }

    [Fact]
    public void Append_TrimsOldestLinesToMaxLineCount()
    {
        var buffer = new WorkflowProgressLogBuffer(maxLines: 2);

        buffer.Append("first");
        buffer.Append("second");
        buffer.Append("third");

        Assert.True(buffer.TryRender(out var text));
        Assert.Equal("second" + Environment.NewLine + "third", text);
        Assert.Equal(new[] { "second", "third" }, buffer.SnapshotLines());
    }

    [Fact]
    public void Append_IgnoresBlankLinesWithoutMarkingBufferDirty()
    {
        var buffer = new WorkflowProgressLogBuffer(maxLines: 10);

        Assert.False(buffer.Append("   "));
        Assert.False(buffer.TryRender(out _));
    }
}
