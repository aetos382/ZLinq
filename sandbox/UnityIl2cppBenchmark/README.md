# Unity IL2CPP benchmark

Measures the netstandard build of ZLinq in Unity IL2CPP players, to compare a change against `main`.
It runs the same cases as `sandbox/Benchmark.NetStandard`, whose CoreCLR numbers do not represent IL2CPP.

- `Il2cpp2022/`: Unity 2022.3.62f2 project
- `Il2cpp6000/`: Unity 6000.6.3f1 project
- `scripts/`: build, run and analysis scripts (Git Bash on Windows, Python 3)

Both projects have the same `Assets/`: `Scripts/BenchmarkRunner.cs` runs every case in the player and appends one TSV line per sample (variant, backend, case, N, ns/op), and `Editor/PlayerBuilder.cs` builds a StandaloneWindows64 player with the IL2CPP Release C++ configuration.

## Usage

1. Copy the project for your Unity version to a short path outside the repository (IL2CPP builds may fail on long paths), e.g. `D:\ZLinqBench\Il2cpp2022`.
2. Build the netstandard2.1 `ZLinq.dll` of each variant, e.g. `main` (from a separate worktree) and your branch:

   ```sh
   dotnet build src/ZLinq -c Release -f netstandard2.1
   # -> src/ZLinq/bin/Release/netstandard2.1/ZLinq.dll
   ```

   Build the variant of your branch from a clean worktree of the commit you want to measure, not from a working tree with uncommitted changes.

3. Build a player per variant. The variant name `Baseline` is the reference of the analysis.

   ```sh
   U="/c/Program Files/Unity/Hub/Editor/2022.3.62f2/Editor/Unity.exe"
   scripts/build-variant.sh "$U" /d/ZLinqBench/Il2cpp2022 /d/ZLinqBench/Builds2022/Baseline Baseline /path/to/main/ZLinq.dll
   scripts/build-variant.sh "$U" /d/ZLinqBench/Il2cpp2022 /d/ZLinqBench/Builds2022/Current Current /path/to/branch/ZLinq.dll
   ```

   `System.Runtime.CompilerServices.Unsafe.dll` 6.1.2 is copied from the NuGet package cache; set `UNSAFE_DLL` to use another file.

4. Run the players alternately. Run nothing else on the machine while measuring.

   ```sh
   scripts/run-rounds.sh /d/ZLinqBench/Builds2022 3 result-2022.tsv Baseline Current
   ```

5. Print the ratio of each variant against `Baseline` (median of each round, then the median across rounds, with the range of the per-round ratios):

   ```sh
   python scripts/analyze.py result-2022.tsv [case filter]
   ```

`SAMPLES` (default 15) sets the number of samples per case and round for both `run-rounds.sh` and `analyze.py`.

Results at N = 16 are dominated by noise, and the code generation of IL2CPP varies by 10-30% between builds of the same source, so compare several rounds and treat small differences with care.

## Variants measured for #260

The numbers in the pull request for #260 (netstandard builds no longer rely on the private field layout of `List<T>`) compare these commits:

- `Baseline`: `main` at `ce6fcff`, which reaches the backing array of `List<T>` through its private field layout.
- `Naive`: `f95e7ac` (tag `bench/naive-list-access`), which uses only the public `List<T>` API and reads the elements one by one through the indexer, without chunked reads or bulk copies.
- `Final`: `aac6d87`, the head of the pull request.
