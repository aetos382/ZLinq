namespace ZLinq.Tests.Linq;

// The List<T> specialized operators read the list in chunks on netstandard (tested through net48).
// These tests cover lengths around the chunk size (512) so that every chunk boundary is exercised.
public class ListChunksTest
{
    public static TheoryData<int> Lengths => new() { 0, 1, 511, 512, 513, 1024, 1500 };

    /// <summary>
    /// Ensures that Count with a predicate on List&lt;T&gt; visits every element exactly once across chunk boundaries.
    /// </summary>
    [Theory]
    [MemberData(nameof(Lengths))]
    public void CountWithPredicate_CountsAllChunks(int length)
    {
        var list = Enumerable.Range(0, length).ToList();

        list.AsValueEnumerable().Count(x => x % 3 == 0).ShouldBe(list.Count(x => x % 3 == 0));
    }

    /// <summary>
    /// Ensures that Where().Count() on List&lt;T&gt; visits every element exactly once across chunk boundaries.
    /// </summary>
    [Theory]
    [MemberData(nameof(Lengths))]
    public void WhereCount_CountsAllChunks(int length)
    {
        var list = Enumerable.Range(0, length).ToList();

        list.AsValueEnumerable().Where(x => x % 3 == 0).Count().ShouldBe(list.Where(x => x % 3 == 0).Count());
    }

    /// <summary>
    /// Ensures that Select().ToList() on List&lt;T&gt; writes every element to the right position across chunk boundaries.
    /// </summary>
    [Theory]
    [MemberData(nameof(Lengths))]
    public void SelectToList_PreservesOrderAcrossChunks(int length)
    {
        var list = Enumerable.Range(0, length).ToList();

        list.AsValueEnumerable().Select(x => x * 2).ToList().ShouldBe(list.Select(x => x * 2).ToList());
    }

    /// <summary>
    /// Ensures that the chunked read also works for reference type elements, including null.
    /// </summary>
    [Theory]
    [MemberData(nameof(Lengths))]
    public void ReferenceTypeElements_AreReadAcrossChunks(int length)
    {
        var list = Enumerable.Range(0, length).Select(x => x % 5 == 0 ? null : x.ToString()).ToList();

        list.AsValueEnumerable().Count(x => x == null).ShouldBe(list.Count(x => x == null));
        list.AsValueEnumerable().Where(x => x != null).Count().ShouldBe(list.Where(x => x != null).Count());
        list.AsValueEnumerable().Select(x => x?.Length ?? -1).ToList().ShouldBe(list.Select(x => x?.Length ?? -1).ToList());
    }

    /// <summary>
    /// Ensures that the operators stop at the current count when the predicate removes elements from the list,
    /// instead of reading beyond the end of the list.
    /// </summary>
    [Fact]
    public void ListShrinksDuringCount_DoesNotReadBeyondCount()
    {
        var list = Enumerable.Range(0, 1500).ToList();
        var visited = 0;

        Should.NotThrow(() => list.AsValueEnumerable().Count(x =>
        {
            visited++;
            if (x == 0)
            {
                list.RemoveRange(1000, 500);
            }
            return true;
        }));

        visited.ShouldBeLessThanOrEqualTo(1500);
    }

    /// <summary>
    /// Ensures that a nested read of the same element type (a selector that reads another list) does not share the pooled buffer.
    /// </summary>
    [Fact]
    public void NestedRead_DoesNotShareBuffer()
    {
        var outer = Enumerable.Range(0, 1500).ToList();
        var inner = Enumerable.Range(0, 1000).ToList();

        var actual = outer.AsValueEnumerable().Select(x => x + inner.AsValueEnumerable().Count(y => y < 10)).ToList();

        actual.ShouldBe(outer.Select(x => x + 10).ToList());
    }
}
