namespace ZLinq.Tests.Linq;

// On netstandard (tested through net48), JoinToString over List<string> copies the elements into a pooled array
// to use the fast path for strings.
public class NetStandardListFastPathTest
{
    public static TheoryData<int> Lengths => new() { 0, 1, 2, 100, 1024 };

    /// <summary>
    /// Ensures that JoinToString over List&lt;string&gt; produces the same result as string.Join,
    /// including null elements and every separator length (empty, single char, multiple chars).
    /// </summary>
    [Theory]
    [MemberData(nameof(Lengths))]
    public void JoinToString_ListOfString_MatchesStringJoin(int length)
    {
        var list = Enumerable.Range(0, length).Select(x => x % 7 == 3 ? null : x.ToString()).ToList();

        list.AsValueEnumerable().JoinToString("").ShouldBe(string.Join("", list));
        list.AsValueEnumerable().JoinToString(',').ShouldBe(string.Join(",", list));
        list.AsValueEnumerable().JoinToString(", ").ShouldBe(string.Join(", ", list));
    }
}
