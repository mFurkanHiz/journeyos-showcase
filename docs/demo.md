# Demo

## Run it

Backend (API on port 5100):

```bash
dotnet run --project backend/src/JourneyOS.Showcase.Api
```

Frontend (Vite dev server, proxies `/api` to 5100):

```bash
cd frontend
npm install
npm run dev
```

Open the printed Vite URL (default `http://localhost:5173`).

Or with Docker for the API only:

```bash
docker compose up --build   # API on http://localhost:5100
```

## Three-minute demo script

1. Click **"Canonical demo: Beykoz to Machu Picchu"**. The engine composes candidates
   and shows three profile cards.
2. Read the cards: **Fastest** minimises door-to-gate time, **Cheapest** minimises
   cost, **Balanced** trades off. One itinerary may win two profiles when it dominates
   — both chips then sit on one card (honest, not a bug).
3. The Balanced itinerary is opened below. Walk the **timeline**: taxi/shuttle to the
   airport, check-in and security checkpoints, the international flight (note the
   Istanbul -> connection -> Lima clocks, each with its own UTC offset), passport
   control, the domestic hop to Cusco, the ground approach (train via Ollantaytambo or
   bus + trek via Hidroelectrica), a **hotel night** at the gate town, then the citadel
   entry. Look for the **date-change** and **timezone-change** badges and the
   **arrives +2 days** note.
4. Read the **score breakdown** table and the one-line explanation comparing the
   choice to the fastest option.
5. Scroll to the **side-by-side comparison** table.
6. Toggle **Avoid overnight layovers** or **Reduced walking** and search again to see
   the candidate set change.

Everything is deterministic mock data — the "DEMO DATA" badge and per-warning "demo"
chips say so throughout.
