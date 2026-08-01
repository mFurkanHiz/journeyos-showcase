// Typed client for the showcase API. In dev, Vite proxies /api → localhost:5100;
// set VITE_API_BASE only when serving the built bundle from elsewhere.
const BASE = import.meta.env.VITE_API_BASE ?? "";

export interface ScoreComponent { name: string; normalized: number; weight: number; weighted: number }
export interface Score { profile: string; components: ScoreComponent[]; total: number; explanation: string }
export interface ItinerarySummary {
  id: string; label: string; totalPrice: number; currency: string;
  totalDurationMinutes: number; transferCount: number; walkingMinutes: number;
  waitingMinutes: number; overnightWaits: number; riskScore: number;
  comfortScore: number; carbonKgEstimate: number; warningCount: number;
  score: Score | null;
}
/** A preference the engine could not honour for this route. A non-empty list means
 *  the results knowingly violate part of the search, so the UI has to say so. */
export interface RelaxedPreference { preference: string; reason: string }
export interface Trip {
  id: string; origin: string; destination: string; departureDate: string;
  travellers: number; currency: string;
  profiles: Record<string, string>;
  itineraries: ItinerarySummary[];
  relaxedPreferences: RelaxedPreference[];
}
export interface Segment {
  mode: string; from: string; fromName: string; to: string; toName: string;
  departUtc: string; arriveUtc: string; durationMinutes: number;
  price?: number | null; currency?: string | null; carrier?: string | null;
  note?: string | null; isSeparateTicket: boolean; crossesBorder: boolean;
}
export interface TimelineEntry {
  kind: string; title: string; mode?: string | null;
  startUtc: string; endUtc: string; startLocal: string; endLocal: string;
  durationMinutes: number; crossesTimeZone: boolean; dateChanges: boolean; note?: string | null;
}
export interface Timeline {
  entries: TimelineEntry[]; arrivalDayOffset: number;
  departureLocal: string; arrivalLocal: string;
}
export interface Warning { code: string; severity: string; title: string; detail: string; isDemoData: boolean }
export interface ItineraryDetail {
  summary: ItinerarySummary; segments: Segment[]; timeline: Timeline;
  warnings: Warning[]; requiredDocuments: string[];
}
export interface ComparisonRow { metric: string; byProfile: Record<string, string> }

export interface SearchRequest {
  origin: string; destination: string; departureDate: string;
  travellers: number; currency: string;
  maxTransfers?: number | null; avoidOvernightLayovers?: boolean; reducedWalking?: boolean;
}

export class ApiError extends Error {
  constructor(public status: number, public code: string) {
    super(`API ${status}: ${code}`);
  }
}

async function req<T>(path: string, init?: RequestInit): Promise<T> {
  const r = await fetch(`${BASE}${path}`, {
    headers: { "Content-Type": "application/json" },
    ...init,
  });
  if (!r.ok) {
    let code = `http_${r.status}`;
    try {
      const body = (await r.json()) as { error?: string };
      if (body.error) code = body.error;
    } catch { /* non-JSON error body */ }
    throw new ApiError(r.status, code);
  }
  return r.json() as Promise<T>;
}

export const api = {
  search: (body: SearchRequest) =>
    req<Trip>("/api/trips/search", { method: "POST", body: JSON.stringify(body) }),
  itinerary: (tripId: string, itineraryId: string) =>
    req<ItineraryDetail>(`/api/trips/${tripId}/itineraries/${itineraryId}`),
  comparison: (tripId: string) =>
    req<ComparisonRow[]>(`/api/trips/${tripId}/comparison`),
};

export const fmtDuration = (min: number) => `${Math.floor(min / 60)}h ${String(min % 60).padStart(2, "0")}m`;
