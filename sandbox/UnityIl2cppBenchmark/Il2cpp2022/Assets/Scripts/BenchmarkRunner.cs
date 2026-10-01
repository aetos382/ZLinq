using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using ZLinq;

// Runs the same cases as sandbox/Benchmark.NetStandard in a player build and writes
// one TSV line per sample: variant, backend, case, N, ns/op.
public static class BenchmarkRunner
{
    static readonly int[] Sizes = { 16, 32, 64, 128, 1024, 65536 };

    const double WarmupSeconds = 0.2;
    const double SampleSeconds = 0.05;

    public static int Sink;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Run()
    {
        if (Application.isEditor)
        {
            return;
        }

        var output = GetArgument("-output") ?? Path.Combine(Application.persistentDataPath, "result.tsv");
        var samples = int.Parse(GetArgument("-samples") ?? "15", CultureInfo.InvariantCulture);
        var filter = GetArgument("-filter");
        var variant = File.ReadAllText(Path.Combine(Application.streamingAssetsPath, "variant.txt")).Trim();
        var backend = Application.platform + "/" + GetBackend();

        var sb = new StringBuilder();
        try
        {
            foreach (var n in Sizes)
            {
                foreach (var (name, action) in CreateCases(n))
                {
                    if (filter != null && name.IndexOf(filter, StringComparison.Ordinal) < 0)
                    {
                        continue;
                    }

                    foreach (var nsPerOp in Measure(action, samples))
                    {
                        sb.Append(variant).Append('\t').Append(backend).Append('\t').Append(name).Append('\t')
                          .Append(n.ToString(CultureInfo.InvariantCulture)).Append('\t')
                          .Append(nsPerOp.ToString("R", CultureInfo.InvariantCulture)).Append('\n');
                    }
                }
            }

            File.AppendAllText(output, sb.ToString());
        }
        catch (Exception ex)
        {
            File.AppendAllText(output + ".error.txt", ex.ToString());
        }

        Application.Quit();
    }

    static string GetBackend()
    {
#if ENABLE_IL2CPP
        return "IL2CPP";
#else
        return "Mono";
#endif
    }

    static string GetArgument(string name)
    {
        var args = Environment.GetCommandLineArgs();
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == name)
            {
                return args[i + 1];
            }
        }
        return null;
    }

    static List<double> Measure(Func<int> action, int samples)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        // Warm up and calibrate the number of operations per sample.
        var sw = Stopwatch.StartNew();
        long warmupOps = 0;
        while (sw.Elapsed.TotalSeconds < WarmupSeconds)
        {
            Sink += action();
            warmupOps++;
        }
        var opsPerSample = Math.Max(1, (long)(warmupOps * (SampleSeconds / sw.Elapsed.TotalSeconds)));

        var results = new List<double>(samples);
        for (var s = 0; s < samples; s++)
        {
            var start = Stopwatch.GetTimestamp();
            for (long i = 0; i < opsPerSample; i++)
            {
                Sink += action();
            }
            var elapsed = Stopwatch.GetTimestamp() - start;
            results.Add(elapsed * (1e9 / Stopwatch.Frequency) / opsPerSample);
        }
        return results;
    }

    static IEnumerable<(string, Func<int>)> CreateCases(int n)
    {
        var intArray = Enumerable.Range(0, n).ToArray();
        var intList = new List<int>(intArray);
        var stringList = intArray.Select(x => x.ToString(CultureInfo.InvariantCulture)).ToList();
        var destinationList = new List<int>(n);

        // FromList<T>: TryGetSpan / TryCopyTo
        yield return ("FromList_Sum", () => intList.AsValueEnumerable().Sum());
        yield return ("FromList_ToArray", () => intList.AsValueEnumerable().ToArray().Length);
        yield return ("FromList_ToList", () => intList.AsValueEnumerable().ToList().Count);
        yield return ("FromList_CountWithPredicate", () => intList.AsValueEnumerable().Count(x => (x & 1) == 0));
        yield return ("FromList_JoinToString_Int", () => intList.AsValueEnumerable().JoinToString(',').Length);
        yield return ("FromList_JoinToString_String", () => stringList.AsValueEnumerable().JoinToString(',').Length);

        // ListWhere<T>
        yield return ("ListWhere_Iterate", () =>
        {
            var sum = 0;
            foreach (var x in intList.AsValueEnumerable().Where(x => (x & 1) == 0))
            {
                sum += x;
            }
            return sum;
        });
        yield return ("ListWhere_Count", () => intList.AsValueEnumerable().Where(x => (x & 1) == 0).Count());
        yield return ("ListWhere_ToArray", () => intList.AsValueEnumerable().Where(x => (x & 1) == 0).ToArray().Length);
        yield return ("ListWhereSelect_ToArray", () => intList.AsValueEnumerable().Where(x => (x & 1) == 0).Select(x => x * 2).ToArray().Length);

        // FromList<T>.TryCopyTo
        yield return ("FromList_SkipTake_ToArray", () => intList.AsValueEnumerable().Skip(1).Take(n - 2).ToArray().Length);
        yield return ("FromList_ElementAt", () => intList.AsValueEnumerable().ElementAt(n / 2));

        // ListSelect<TSource, TResult>
        yield return ("ListSelect_Iterate", () =>
        {
            var sum = 0;
            foreach (var x in intList.AsValueEnumerable().Select(x => x * 2))
            {
                sum += x;
            }
            return sum;
        });
        yield return ("ListSelect_ToArray", () => intList.AsValueEnumerable().Select(x => x * 2).ToArray().Length);
        yield return ("ListSelect_ToList", () => intList.AsValueEnumerable().Select(x => x * 2).ToList().Count);
        yield return ("ListSelect_JoinToString", () => intList.AsValueEnumerable().Select(x => x * 2).JoinToString(',').Length);

        // Writing into List<T>
        yield return ("FromArray_ToList", () => intArray.AsValueEnumerable().ToList().Count);
        yield return ("FromArray_Select_ToList", () => intArray.AsValueEnumerable().Select(x => x * 2).ToList().Count);
        yield return ("FromArray_CopyToList", () =>
        {
            intArray.AsValueEnumerable().CopyTo(destinationList);
            return destinationList.Count;
        });
    }
}
