# Security

This repo is secret-free **by construction**, not by scrubbing:

- **No secrets anywhere.** No API keys, tokens, connection strings or credentials.
  `appsettings.json` holds only framework defaults (logging, allowed hosts).
- **No outbound network calls.** Every provider is an in-process deterministic mock.
  There is nothing to authenticate to and nothing to leak.
- **No production identifiers.** No deployment hosts, IPs, domains, e-mails, cloud
  account ids or third-party client ids. The API binds to localhost for local dev.
- **`.env.example`** documents the (empty) configuration surface. `.env` is gitignored.
- **CORS** is limited to the Vite dev/preview origins on localhost.
- **Fresh git history.** This repository was initialised from scratch; it shares no
  commit history with the private system it was extracted from.
- **Clean re-implementation.** The composition core was re-written for the showcase
  rather than copied, so no private file — and nothing embedded in one — can ride
  along.

The private JourneyOS system is referenced only in generic capability terms. No detail
that could identify or attack a live deployment appears here.
