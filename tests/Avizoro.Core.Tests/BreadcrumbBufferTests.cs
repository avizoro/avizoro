using Avizoro.Core.Configuration;
using Avizoro.Core.Diagnostics;
using Avizoro.Core.Models;

namespace Avizoro.Core.Tests;

public class BreadcrumbBufferTests
{
    private static BreadcrumbBuffer NewBuffer(int capacity)
        => new(new AvizoroOptions { BreadcrumbCapacity = capacity });

    private static Breadcrumb Crumb(string message) => new() { Message = message };

    [Fact]
    public void Snapshot_ReturnsItems_OldestFirst()
    {
        var buffer = NewBuffer(10);
        buffer.Add(Crumb("first"));
        buffer.Add(Crumb("second"));
        buffer.Add(Crumb("third"));

        var snapshot = buffer.Snapshot();

        Assert.Equal(new[] { "first", "second", "third" }, snapshot.Select(c => c.Message));
    }

    [Fact]
    public void Add_BeyondCapacity_DropsOldest()
    {
        var buffer = NewBuffer(2);
        buffer.Add(Crumb("a"));
        buffer.Add(Crumb("b"));
        buffer.Add(Crumb("c"));

        var snapshot = buffer.Snapshot();

        Assert.Equal(new[] { "b", "c" }, snapshot.Select(c => c.Message));
    }

    [Fact]
    public void Capacity_IsClampedToAtLeastOne()
    {
        // Options with a nonsensical capacity must not produce a zero/negative
        // buffer that silently drops everything or throws.
        var buffer = NewBuffer(0);
        buffer.Add(Crumb("only"));

        var snapshot = buffer.Snapshot();

        Assert.Single(snapshot);
        Assert.Equal("only", snapshot[0].Message);
    }

    [Fact]
    public void Clear_EmptiesTheBuffer()
    {
        var buffer = NewBuffer(5);
        buffer.Add(Crumb("a"));
        buffer.Add(Crumb("b"));

        buffer.Clear();

        Assert.Empty(buffer.Snapshot());
    }

    [Fact]
    public void Snapshot_IsAPointInTimeCopy_NotAffectedByLaterAdds()
    {
        var buffer = NewBuffer(5);
        buffer.Add(Crumb("a"));

        var snapshot = buffer.Snapshot();
        buffer.Add(Crumb("b"));

        Assert.Single(snapshot);
    }

    [Fact]
    public async Task Add_IsThreadSafe_UnderConcurrency()
    {
        var buffer = NewBuffer(1000);

        await Task.WhenAll(Enumerable.Range(0, 10).Select(t => Task.Run(() =>
        {
            for (var i = 0; i < 100; i++)
                buffer.Add(Crumb($"{t}-{i}"));
        })));

        Assert.Equal(1000, buffer.Snapshot().Count);
    }
}
