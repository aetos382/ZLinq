import os
import sys
import statistics
from collections import defaultdict

# Usage: analyze.py <result.tsv> [case substring filter]
# Per (case, N, variant): median of each round (SAMPLES samples, default 15), then the median across rounds.
# Ratios are relative to the Baseline variant.
path = sys.argv[1]
case_filter = sys.argv[2] if len(sys.argv) > 2 else None
per_sample = int(os.environ.get("SAMPLES", "15"))

samples = defaultdict(list)
variants = []
cases = []
for line in open(path, encoding="utf-8"):
    variant, backend, case, n, ns = line.rstrip("\n").split("\t")
    samples[(case, int(n), variant)].append(float(ns))
    if variant not in variants:
        variants.append(variant)
    if (case, int(n)) not in cases:
        cases.append((case, int(n)))

others = [v for v in variants if v != "Baseline"]
header = f"{'Case':30} {'N':>6} {'Baseline ns':>12}" + "".join(f" {v + ' ratio':>14} {'(range)':>11}" for v in others)
print(header)
for case, n in sorted(cases, key=lambda x: (x[1], cases.index(x))):
    if case_filter and case_filter not in case:
        continue
    rounds = {}
    for v in variants:
        s = samples[(case, n, v)]
        rounds[v] = [statistics.median(s[i:i + per_sample]) for i in range(0, len(s), per_sample)]
    base = statistics.median(rounds["Baseline"])
    row = f"{case:30} {n:>6} {base:>12.1f}"
    for v in others:
        ratios = [x / b for b, x in zip(rounds["Baseline"], rounds[v])]
        row += f" {statistics.median(rounds[v]) / base:>14.2f} {min(ratios):>5.2f}-{max(ratios):<5.2f}"
    print(row)
