using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;
using BenchmarkDotNet.Toolchains.CsProj;

namespace Benchmark.NetStandard;

internal static class Program
{
    const string BaselineDllEnvironmentVariable = "ZLINQ_BASELINE_DLL";

    public static int Main(string[] args)
    {
        Console.WriteLine($"Host process ID: {Environment.ProcessId}");

        var baselineDll = Environment.GetEnvironmentVariable(BaselineDllEnvironmentVariable);
        if (string.IsNullOrEmpty(baselineDll) || !File.Exists(baselineDll))
        {
            Console.Error.WriteLine($"Set {BaselineDllEnvironmentVariable} to the netstandard2.1 ZLinq.dll built from the baseline source.");
            return 1;
        }

        BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args, new NetStandardComparisonConfig(Path.GetFullPath(baselineDll)));
        return 0;
    }
}

/// <summary>
/// Compares the netstandard2.1 build of the baseline source (a saved ZLinq.dll) with the netstandard2.1 build of the current source.
/// Build the baseline DLL with the same SDK as the current source; otherwise the difference may come from the compiler rather than the source.
/// </summary>
internal sealed class NetStandardComparisonConfig : ManualConfig
{
    public NetStandardComparisonConfig(string baselineDll)
    {
        var baseJob = Job.Default.WithToolchain(CsProjCoreToolchain.NetCoreApp80);

        AddJob(baseJob
            .WithCustomBuildConfiguration("ZLinqBaseline")
            .WithArguments([new MsBuildArgument($"/p:ZLinqBaselineDll=\"{baselineDll}\"")])
            .WithId("Baseline")
            .AsBaseline());

        AddJob(baseJob.WithId("Current"));

        AddDiagnoser(MemoryDiagnoser.Default);

        AddColumnProvider(DefaultConfig.Instance.GetColumnProviders().ToArray());
        AddColumn(StatisticalTestColumn.Create("5%"));
        AddLogger(DefaultConfig.Instance.GetLoggers().ToArray());
        AddExporter(DefaultConfig.Instance.GetExporters().ToArray());
        AddAnalyser(DefaultConfig.Instance.GetAnalysers().ToArray());
        AddValidator(DefaultConfig.Instance.GetValidators().ToArray());

        HideColumns(Column.BuildConfiguration, Column.Arguments);
        AddLogicalGroupRules(BenchmarkLogicalGroupRule.ByMethod, BenchmarkLogicalGroupRule.ByParams);
    }
}
