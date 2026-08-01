# Warnings

`WarningEngine` combines country-pair rules (from `IRequirementWarningProvider`) with
segment-derived rules over the composed itinerary. **Every warning carries
`IsDemoData = true`** and the UI shows a "demo" chip on each — nothing here is
official travel advice.

| Code | Severity | Trigger |
|---|---|---|
| `passport_validity` | Caution | always (demo baseline) |
| `visa_pe` / `transit_es` / `transit_co` | Info | country-pair demo rules for the route |
| `health_pe` | Info | destination country health note (demo) |
| `short_connection` | Caution | transfer wait < 45 min |
| `risky_connection` | Info | transfer wait 45-59 min at an airport |
| `overnight_airport` | Caution | 5h+ airport wait across night hours |
| `self_transfer` | Caution | separate tickets across a connection |
| `baggage_recheck` | Info | baggage must be collected + re-checked |
| `airport_change` | Caution | arrive one airport, depart another in the same metro |
| `long_walk` | Caution | a walking segment of 60+ min |
| `border_crossing` | Info | a non-flight leg changes country |
| `seasonal_closure` | Caution | destination + month demo rule (Inca Trail in Feb) |
| `requirements_unavailable` | Info | the requirement provider failed (fail-soft) |

The rules are the showcase; the facts are mock. In the private system the same seam
is fed by live government advisory feeds, and the rule engine is identical.
