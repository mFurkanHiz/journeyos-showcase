# Security policy

## Scope

This is a portfolio demonstration repository. It ships **no secrets, no credentials and
no production configuration**, and it makes **no outbound network calls** — every
provider is an in-process deterministic mock. There is no deployed instance to attack.

## What this repo deliberately does not contain

- API keys, tokens, connection strings or `.env` files (`.env.example` documents the
  empty configuration surface).
- Deployment hosts, IP addresses, cloud account identifiers or third-party client ids.
- Any personal data. All demo travellers, hotels and schedules are invented.

## Reporting

If you spot something that looks like a leaked credential or a security-relevant bug,
please open a GitHub issue or use the repository's private vulnerability reporting.
Since the code has no runtime dependencies on external services, the realistic surface
is limited to dependency vulnerabilities, which Dependabot tracks.

## A note on the demo data

Entry, visa and health warnings in this project are **demonstration rules**, not travel
advice, and are flagged `IsDemoData` in the API and labelled in the UI. Do not use them
for real travel decisions.
