import { useMemo, useState } from "react";
import {
  api, ApiError, fmtDuration,
  type ComparisonRow, type ItineraryDetail, type Trip,
} from "./api";

const MODE_ICON: Record<string, string> = {
  Walking: "🚶", Taxi: "🚕", PrivateTransfer: "🚐", UrbanTransit: "🚌",
  IntercityBus: "🚌", Train: "🚆", Ferry: "⛴", DomesticFlight: "✈",
  InternationalFlight: "✈", AirportAccess: "🚐", HotelStay: "🏨",
  Wait: "⏳", Activity: "🎫", Checkpoint: "🛂",
};

const PROFILE_META: Record<string, { icon: string; blurb: string }> = {
  Fastest: { icon: "⚡", blurb: "Minimum door-to-gate time" },
  Cheapest: { icon: "💰", blurb: "Minimum total cost" },
  Balanced: { icon: "⚖", blurb: "Weighted trade-off" },
};

const CANONICAL = {
  origin: "Beykoz, Istanbul",
  destination: "Machu Picchu",
  departureDate: "2026-09-18",
};

export default function App() {
  const [origin, setOrigin] = useState("");
  const [destination, setDestination] = useState("");
  const [date, setDate] = useState(CANONICAL.departureDate);
  const [travellers, setTravellers] = useState(1);
  const [currency, setCurrency] = useState("USD");
  const [avoidOvernight, setAvoidOvernight] = useState(false);
  const [reducedWalking, setReducedWalking] = useState(false);

  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [trip, setTrip] = useState<Trip | null>(null);
  const [detail, setDetail] = useState<ItineraryDetail | null>(null);
  const [comparison, setComparison] = useState<ComparisonRow[] | null>(null);

  async function run(o: string, d: string, dt: string) {
    setLoading(true);
    setError(null);
    setTrip(null);
    setDetail(null);
    setComparison(null);
    try {
      const t = await api.search({
        origin: o, destination: d, departureDate: dt,
        travellers, currency,
        avoidOvernightLayovers: avoidOvernight, reducedWalking,
      });
      setTrip(t);
      const balancedId = t.profiles.Balanced ?? t.itineraries[0]?.id;
      if (balancedId) setDetail(await api.itinerary(t.id, balancedId));
      setComparison(await api.comparison(t.id));
    } catch (e) {
      setError(e instanceof ApiError ? e.code : "network_error");
    } finally {
      setLoading(false);
    }
  }

  function canonical() {
    setOrigin(CANONICAL.origin);
    setDestination(CANONICAL.destination);
    setDate(CANONICAL.departureDate);
    void run(CANONICAL.origin, CANONICAL.destination, CANONICAL.departureDate);
  }

  async function select(itineraryId: string) {
    if (!trip) return;
    setDetail(await api.itinerary(trip.id, itineraryId));
  }

  // itinerary id → profiles that picked it (one card can carry several chips).
  const picksByItinerary = useMemo(() => {
    const map = new Map<string, string[]>();
    if (trip)
      for (const [profile, id] of Object.entries(trip.profiles))
        map.set(id, [...(map.get(id) ?? []), profile]);
    return map;
  }, [trip]);

  return (
    <div className="shell">
      <header>
        <div>
          <h1>JourneyOS <span className="thin">Showcase</span></h1>
          <p className="tagline">Door-to-door multimodal trip planning — not a flight search.</p>
        </div>
        <span className="demo-badge" title="Every price, schedule and entry rule in this demo comes from deterministic mock providers.">
          DEMO DATA · deterministic mocks
        </span>
      </header>

      <section className="card form">
        <button className="canonical" onClick={canonical} disabled={loading}>
          ▶ Canonical demo: Beykoz → Machu Picchu
        </button>
        <form onSubmit={(e) => { e.preventDefault(); void run(origin, destination, date); }}>
          <div className="grid">
            <label>Origin
              <input value={origin} onChange={(e) => setOrigin(e.target.value)} placeholder="Beykoz, Istanbul" required />
            </label>
            <label>Destination
              <input value={destination} onChange={(e) => setDestination(e.target.value)} placeholder="Machu Picchu" required />
            </label>
            <label>Departure
              <input type="date" value={date} onChange={(e) => setDate(e.target.value)} required />
            </label>
            <label>Travellers
              <input type="number" min={1} max={9} value={travellers}
                onChange={(e) => setTravellers(Math.max(1, Math.min(9, +e.target.value || 1)))} />
            </label>
            <label>Currency
              <select value={currency} onChange={(e) => setCurrency(e.target.value)}>
                {["USD", "EUR", "TRY", "PEN", "GBP"].map((c) => <option key={c}>{c}</option>)}
              </select>
            </label>
          </div>
          <div className="prefs">
            <label className="check">
              <input type="checkbox" checked={avoidOvernight} onChange={(e) => setAvoidOvernight(e.target.checked)} />
              Avoid overnight layovers
            </label>
            <label className="check">
              <input type="checkbox" checked={reducedWalking} onChange={(e) => setReducedWalking(e.target.checked)} />
              Reduced walking
            </label>
            <button type="submit" disabled={loading}>Search</button>
          </div>
        </form>
      </section>

      {loading && <div className="card state"><span className="spinner" /> Composing door-to-door candidates…</div>}
      {error && <div className="card state error">Search failed: <code>{error}</code></div>}
      {!trip && !loading && !error && (
        <div className="card state muted">
          Run the canonical demo above. The engine composes taxi + flights + train/bus +
          hotel nights + the citadel entry into three genuinely different alternatives.
        </div>
      )}

      {trip && trip.relaxedPreferences.length > 0 && (
        <div className="card state relaxed">
          <strong>These results do not fully match what you asked for.</strong>
          <ul>
            {trip.relaxedPreferences.map((r) => (
              <li key={r.preference}><code>{r.preference}</code> — {r.reason}</li>
            ))}
          </ul>
        </div>
      )}

      {trip && (
        <>
          <section className="cards">
            {trip.itineraries.map((i) => {
              const profiles = picksByItinerary.get(i.id) ?? [];
              const active = detail?.summary.id === i.id;
              return (
                <button key={i.id} className={`card profile ${active ? "active" : ""}`} onClick={() => void select(i.id)}>
                  <div className="chips">
                    {profiles.map((p) => (
                      <span key={p} className={`chip ${p.toLowerCase()}`}>
                        {PROFILE_META[p]?.icon} {p}
                      </span>
                    ))}
                  </div>
                  <div className="price">{i.totalPrice.toFixed(0)} <small>{i.currency}</small></div>
                  <div className="meta">
                    <span>{fmtDuration(i.totalDurationMinutes)}</span>
                    <span>{i.transferCount} transfers</span>
                    <span>risk {(i.riskScore * 100).toFixed(0)}%</span>
                  </div>
                  <div className="meta faint">
                    <span>{i.carbonKgEstimate} kg CO₂e</span>
                    <span>{i.warningCount} warnings</span>
                  </div>
                  <div className="label">{i.label}</div>
                </button>
              );
            })}
          </section>

          {detail && (
            <section className="card detail">
              <h2>
                {picksByItinerary.get(detail.summary.id)?.join(" + ") ?? "Itinerary"}
                <span className="thin"> — {detail.summary.label}</span>
              </h2>
              <p className="journey-span">
                {detail.timeline.departureLocal} → {detail.timeline.arrivalLocal}
                <strong> · arrives +{detail.timeline.arrivalDayOffset} day{detail.timeline.arrivalDayOffset > 1 ? "s" : ""}</strong>
              </p>

              {detail.summary.score && (
                <div className="score">
                  <table>
                    <thead>
                      <tr><th>Component</th><th>Normalized</th><th>Weight</th><th>Weighted</th></tr>
                    </thead>
                    <tbody>
                      {detail.summary.score.components.map((c) => (
                        <tr key={c.name}>
                          <td>{c.name}</td>
                          <td>{c.normalized.toFixed(2)}</td>
                          <td>{c.weight.toFixed(2)}</td>
                          <td>{c.weighted.toFixed(3)}</td>
                        </tr>
                      ))}
                      <tr className="total">
                        <td colSpan={3}>Total (lower is better within this candidate set)</td>
                        <td>{detail.summary.score.total.toFixed(3)}</td>
                      </tr>
                    </tbody>
                  </table>
                  <p className="explanation">“{detail.summary.score.explanation}”</p>
                </div>
              )}

              <h3>Timeline</h3>
              <ol className="timeline">
                {detail.timeline.entries.map((e, idx) => (
                  <li key={idx} className={e.kind}>
                    <span className="icon">{e.mode ? MODE_ICON[e.mode] ?? "•" : "•"}</span>
                    <div>
                      <div className="title">{e.title}</div>
                      <div className="times">
                        {e.startLocal} → {e.endLocal} · {fmtDuration(e.durationMinutes)}
                        {e.crossesTimeZone && <span className="badge">🕒 timezone change</span>}
                        {e.dateChanges && <span className="badge">📅 date changes</span>}
                      </div>
                    </div>
                  </li>
                ))}
              </ol>

              <h3>Warnings <span className="thin">(all demo data)</span></h3>
              <ul className="warnings">
                {detail.warnings.map((w, idx) => (
                  <li key={idx} className={w.severity.toLowerCase()}>
                    <strong>{w.title}</strong>
                    <span>{w.detail}</span>
                    {w.isDemoData && <em className="demo-chip">demo</em>}
                  </li>
                ))}
              </ul>

              <h3>Required documents</h3>
              <ul className="docs">
                {detail.requiredDocuments.map((d, idx) => <li key={idx}>{d}</li>)}
              </ul>
            </section>
          )}

          {comparison && (
            <section className="card">
              <h3>Side-by-side comparison</h3>
              <table className="comparison">
                <thead>
                  <tr>
                    <th />
                    {Object.keys(trip.profiles).map((p) => (
                      <th key={p}>{PROFILE_META[p]?.icon} {p}<div className="thin">{PROFILE_META[p]?.blurb}</div></th>
                    ))}
                  </tr>
                </thead>
                <tbody>
                  {comparison.map((row) => (
                    <tr key={row.metric}>
                      <td>{row.metric}</td>
                      {Object.keys(trip.profiles).map((p) => <td key={p}>{row.byProfile[p]}</td>)}
                    </tr>
                  ))}
                </tbody>
              </table>
            </section>
          )}
        </>
      )}

      <footer>
        Technical demo — deterministic mock providers behind the same adapter seams a live
        system uses. No real prices, schedules or entry rules. <a href="/openapi/v1.json">OpenAPI spec</a>
      </footer>
    </div>
  );
}
