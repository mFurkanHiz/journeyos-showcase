# Scoring

`ScoringService` makes Fastest / Cheapest / Balanced **provably different decisions**:

1. Eight metrics per candidate: price, duration, transfers, connection risk, waiting,
   overnight waits, comfort (inverted to a cost), walking.
2. **Min-max normalization across the candidate set**: 0 = best in set, 1 = worst.
   A metric with no spread scores 0 for everyone — it cannot differentiate, so it
   must not punish.
3. **Per-profile weights** (each row sums to 1.0 — asserted by a test):

| profile | price | duration | transfers | risk | waiting | overnight | comfort | walking |
|---|---|---|---|---|---|---|---|---|
| Fastest | .05 | **.55** | .10 | .10 | .10 | .05 | .03 | .02 |
| Cheapest | **.60** | .08 | .05 | .07 | .05 | .05 | .05 | .05 |
| Balanced | .25 | .25 | .12 | .12 | .08 | .08 | .06 | .04 |

4. Lowest weighted total wins; the winner carries a full breakdown, e.g.:

```
Balanced
  price      normalized 0.31 x 0.25 = 0.078
  duration   normalized 0.24 x 0.25 = 0.060
  transfers  normalized 0.10 x 0.12 = 0.012
  ...
  total 0.19  (lower is better within this candidate set)
```

5. A comparative, human explanation is generated against the fastest option, for
   example: "2.5h longer than the fastest option, but 1 fewer transfer and no
   overnight airport wait."

Tests pin the semantics: Fastest picks minimum duration, Cheapest picks minimum
price, the Balanced winner on a hand-built candidate set matches the hand-computed
weighted result, and normalization stays within [0,1].
