# Garbati.ScheduleApi

GARBATI's standing-orders / recurring-payments service. It owns no money and no authorization — a
scheduled run is submitted to `Garbati.OrchestratorApi` exactly like any other channel's request,
going through the same authorization and posting path an interactive transfer does. ScheduleApi's own
job is narrower: remember what's supposed to recur, decide when it's due, and record what happened
when it ran.

**Status: design only, not yet built.** The full design — entities, endpoints, the scheduling
mechanism, and every trade-off behind it — is in [docs/DESIGN.md](docs/DESIGN.md). Start there.

v1 targets GARBATI's full Fineract-equivalent standing-instruction feature set (owner's explicit
choice), honestly adapted to what GARBATI actually has today: everything that would need a loan or
financing product is modeled in the data shape for future parity, but rejected clearly at creation
time until GARBATI's own financing module exists (gated on a qualified Sharia scholar's sign-off,
`garbati-platform` D-28 — unrelated to this service's own scope, just a precondition it doesn't try to
get ahead of).

See [`garbati-platform/ARCHITECTURE.md`](https://github.com/AbubakarImam/garbati-platform/blob/main/ARCHITECTURE.md)
for how this service fits the rest of the platform.
