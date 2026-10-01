#!/bin/sh
# Runs the benchmark players of the given variants in turn, <rounds> times, and appends the samples to <result.tsv>.
# Usage: run-rounds.sh <builds dir> <rounds> <result.tsv> <variant>...
# Each player is expected at <builds dir>/<variant>/Bench.exe (see build-variant.sh).
# SAMPLES sets the number of samples per case and round (default 15). Player logs go to <result.tsv>.logs/.
# Run nothing else on the machine while measuring.
set -e
builds=$1; rounds=$2; result=$3; shift 3
samples=${SAMPLES:-15}

logs="$result.logs"
mkdir -p "$logs"
rm -f "$result"
result_w=$(cygpath -w "$(cd "$(dirname "$result")" && pwd)/$(basename "$result")")
logs_w=$(cygpath -w "$(cd "$logs" && pwd)")

for r in $(seq 1 "$rounds"); do
  for v in "$@"; do
    echo "$(date +%T) round $r $v"
    (cd "$builds/$v" && ./Bench.exe -batchmode -nographics -samples "$samples" -output "$result_w" -logFile "$logs_w\$v-$r.log")
  done
done
echo "$(date +%T) done"
