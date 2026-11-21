"""Small-sample fairness simulation for the two fair-draw modes.

Mirrors SecRandom.Core DrawEngine.Filter.ApplyAverageGapProtection and
DrawEngine.WeightCalculator.CalculateDeficitWeights for a single sequential draw
(batchSize = 1). Prints one representative run, not ensemble averages.
"""

import math
import random
from collections import Counter


def uniform_pool(counts):
    avg = sum(counts) / len(counts)
    max_eligible = avg
    if max(counts) - min(counts) > 1:
        max_eligible = min(max_eligible, min(counts) + 1)
    pool = [i for i, c in enumerate(counts) if c <= max_eligible]
    if not pool:
        pool = [min(range(len(counts)), key=lambda i: counts[i])]
    return pool


def deficit_weights(counts, pool, whole_pool_drawable):
    sub = [counts[i] for i in pool]
    lead_cap = 1 if min(sub) == 0 else 2
    ceiling = min(sub) + lead_cap
    required = len(sub) if whole_pool_drawable else 1
    if sum(1 for c in sub if c < ceiling) < required:
        ceiling = max(ceiling, sorted(sub)[required - 1] + 1)
    weights = [max(ceiling - c, 0.0) for c in sub]
    total = sum(weights)
    if total <= 0:
        return [1.0 / len(pool)] * len(pool)
    return [w / total for w in weights]


def draw_once(counts, mode, rng):
    if mode == "uniform":
        pool = uniform_pool(counts)
        weights = deficit_weights(counts, pool, whole_pool_drawable=True)
    else:
        pool = list(range(len(counts)))
        weights = deficit_weights(counts, pool, whole_pool_drawable=False)
    pick = rng.choices(pool, weights=weights, k=1)[0]
    counts[pick] += 1
    return pick


def run(mode, n, draws, seed, initial=None):
    rng = random.Random(seed)
    counts = list(initial) if initial else [0] * n
    repeats_before_full = 0
    covered = {i for i, c in enumerate(counts) if c > 0}
    for _ in range(draws):
        pick = draw_once(counts, mode, rng)
        if len(covered) < n and pick in covered:
            repeats_before_full += 1
        covered.add(pick)
    return counts, repeats_before_full


def summarize(label, counts):
    dist = Counter(counts)
    print(f"  {label}: distribution {dict(sorted(dist.items()))}, "
          f"max-min={max(counts) - min(counts)}, "
          f"distinct drawn={sum(1 for c in counts if c > 0)}/{len(counts)}")
    buckets = {}
    for i, c in enumerate(counts):
        buckets.setdefault(c, []).append(i)
    for c in sorted(buckets):
        ids = buckets[c]
        print(f"    {c} draw(s): {len(ids)} people  {ids[:12]}{' ...' if len(ids) > 12 else ''}")


def main():
    for draws in (25, 75):
        print(f"\n=== 50 people, {draws} sequential draws, fresh history ===")
        for mode in ("uniform", "random"):
            counts, repeats = run(mode, 50, draws, seed=20261114)
            print(f"[{mode}] repeats before everyone drawn once: {repeats}")
            summarize("after", counts)

    initial = [3] * 10 + [0] * 40
    print("\n=== uneven starting history (10 people already at 3, 40 at 0), 1 draw ===")
    for mode in ("uniform", "random"):
        counts, _ = run(mode, 50, 1, seed=7, initial=initial)
        pool = uniform_pool(initial) if mode == "uniform" else range(50)
        weights = deficit_weights(initial, list(pool), whole_pool_drawable=(mode == "uniform"))
        top = sorted(((w, i) for w, i in zip(weights, pool)), reverse=True)[:4]
        print(f"[{mode}] candidate count={len(list(pool))}, "
              f"top weights={[(round(w, 3), i) for w, i in top]}")

    print("\n=== uneven starting history, 20 sequential draws ===")
    for mode in ("uniform", "random"):
        counts, _ = run(mode, 50, 20, seed=11, initial=initial)
        summarize(f"[{mode}] after", counts)


if __name__ == "__main__":
    main()