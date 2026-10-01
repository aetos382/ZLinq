#!/bin/sh
# Builds a benchmark player (StandaloneWindows64) for one ZLinq.dll variant.
# Usage: build-variant.sh <Unity.exe> <project dir> <player dir> <variant> <ZLinq.dll> [IL2CPP|Mono]
# The variant name is written to the player and recorded in each result line.
# System.Runtime.CompilerServices.Unsafe.dll is taken from UNSAFE_DLL, or from the NuGet package cache.
set -e
unity=$1; project=$2; player=$3; variant=$4; zlinq=$5; backend=${6:-IL2CPP}

packages=${NUGET_PACKAGES:-$HOME/.nuget/packages}
unsafe=${UNSAFE_DLL:-$packages/system.runtime.compilerservices.unsafe/6.1.2/lib/netstandard2.0/System.Runtime.CompilerServices.Unsafe.dll}

mkdir -p "$project/Assets/Plugins/ZLinq" "$project/Assets/Plugins/Unsafe" "$project/Assets/StreamingAssets" "$player"
cp "$zlinq" "$project/Assets/Plugins/ZLinq/ZLinq.dll"
cp "$unsafe" "$project/Assets/Plugins/Unsafe/"
printf '%s\n' "$variant" > "$project/Assets/StreamingAssets/variant.txt"

"$unity" -batchmode -nographics -quit -projectPath "$(cygpath -w "$project")" -executeMethod PlayerBuilder.Build \
  -outDir "$(cygpath -w "$player")" -backend "$backend" -logFile "$(cygpath -w "$player").build.log"
