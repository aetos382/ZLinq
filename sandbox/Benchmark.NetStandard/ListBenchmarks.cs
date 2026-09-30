using BenchmarkDotNet.Attributes;
using System.Runtime.Versioning;
using ZLinq;

namespace Benchmark.NetStandard;

/// <summary>
/// Benchmarks for the operators that have <see cref="List{T}"/> specialized code paths on netstandard.
/// </summary>
[MemoryDiagnoser]
public class ListBenchmarks
{
    [Params(16, 1024, 65536)]
    public int N;

    List<int> intList = default!;
    List<string> stringList = default!;
    int[] intArray = default!;
    List<int> destinationList = default!;

    [GlobalSetup]
    public void Setup()
    {
        // BenchmarkDotNet runs each benchmark case in a separate process.
        Console.WriteLine($"Benchmark process ID: {Environment.ProcessId}");

        EnsureNetStandard21Build();

        intArray = Enumerable.Range(0, N).ToArray();
        intList = new List<int>(intArray);
        stringList = intArray.Select(x => x.ToString()).ToList();
        destinationList = new List<int>(N);
    }

    // Guards against measuring the net8.0 build by mistake.
    // Both jobs are meaningless unless the netstandard2.1 build is loaded.
    static void EnsureNetStandard21Build()
    {
        var assembly = typeof(ValueEnumerable<,>).Assembly;
        var frameworkName = assembly.GetCustomAttributes(typeof(TargetFrameworkAttribute), false)
            .Cast<TargetFrameworkAttribute>()
            .Select(x => x.FrameworkName)
            .FirstOrDefault();

        if (frameworkName != ".NETStandard,Version=v2.1")
        {
            throw new InvalidOperationException($"Expected the netstandard2.1 build of ZLinq, but '{frameworkName}' was loaded from '{assembly.Location}'.");
        }
    }

    // FromList<T> (List<T> source)

    [Benchmark]
    public int FromList_Sum() => intList.AsValueEnumerable().Sum();

    [Benchmark]
    public int[] FromList_ToArray() => intList.AsValueEnumerable().ToArray();

    [Benchmark]
    public List<int> FromList_ToList() => intList.AsValueEnumerable().ToList();

    [Benchmark]
    public int FromList_CountWithPredicate() => intList.AsValueEnumerable().Count(x => (x & 1) == 0);

    [Benchmark]
    public string FromList_JoinToString_Int() => intList.AsValueEnumerable().JoinToString(',');

    [Benchmark]
    public string FromList_JoinToString_String() => stringList.AsValueEnumerable().JoinToString(',');

    // ListWhere<T>

    [Benchmark]
    public int ListWhere_Iterate()
    {
        var sum = 0;
        foreach (var x in intList.AsValueEnumerable().Where(x => (x & 1) == 0))
        {
            sum += x;
        }
        return sum;
    }

    [Benchmark]
    public int ListWhere_Count() => intList.AsValueEnumerable().Where(x => (x & 1) == 0).Count();

    [Benchmark]
    public int[] ListWhere_ToArray() => intList.AsValueEnumerable().Where(x => (x & 1) == 0).ToArray();

    // ListSelect<TSource, TResult>

    [Benchmark]
    public int ListSelect_Iterate()
    {
        var sum = 0;
        foreach (var x in intList.AsValueEnumerable().Select(x => x * 2))
        {
            sum += x;
        }
        return sum;
    }

    [Benchmark]
    public int[] ListSelect_ToArray() => intList.AsValueEnumerable().Select(x => x * 2).ToArray();

    [Benchmark]
    public List<int> ListSelect_ToList() => intList.AsValueEnumerable().Select(x => x * 2).ToList();

    [Benchmark]
    public string ListSelect_JoinToString() => intList.AsValueEnumerable().Select(x => x * 2).JoinToString(',');

    // Writing into List<T> (ListFiller, or the bulk copy fast path for array sources)

    [Benchmark]
    public List<int> FromArray_ToList() => intArray.AsValueEnumerable().ToList();

    [Benchmark]
    public List<int> FromArray_Select_ToList() => intArray.AsValueEnumerable().Select(x => x * 2).ToList();

    [Benchmark]
    public int FromArray_CopyToList()
    {
        intArray.AsValueEnumerable().CopyTo(destinationList);
        return destinationList.Count;
    }
}
