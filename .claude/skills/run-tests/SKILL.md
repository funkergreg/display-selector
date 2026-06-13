---
name: run-tests
description: Run the Display Selector test suite. Use when asked to run tests, run unit tests, run integration tests, or check that tests pass. Runs the headless unit tests by default; runs the real-API integration tests only on explicit request.
---

# run-tests

Run the project's xUnit tests and summarize results concisely.

## Steps
1. **Default — unit tests (headless, fast, CI-safe):**
   `dotnet test --filter "Category!=Integration"`
2. **Integration tests** (only when explicitly asked — they hit real Windows audio/display APIs, non-destructively, and need a live desktop session):
   `dotnet test --filter "Category=Integration"`
3. **Summarize:** report passed/failed/skipped counts. For any failure, show the test name + the relevant assertion/exception lines (not the whole log). If the build itself failed, surface the compiler errors.

## What is NOT covered here
- **Tier-3 active / physical checks** (did the tone actually play on the soundbar? did the displays actually switch?) cannot be asserted by a machine. Those run **human-in-the-loop** from the app's in-tray **Diagnostics** menu (Run audio test / Run display test). Mention this if the user expects automated verification of physical output.

## Notes
- Never `git commit`/`push`.
- If no test project exists yet (before M0 scaffolding), say so rather than reporting a false pass.
