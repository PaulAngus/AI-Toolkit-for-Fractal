# AI Toolkit for Fractal Test Lab adapter

Build on a development PC with .NET 10 and Python:

```
python packaging/build_test_lab.py --rid win-x64 --output artifacts/test-lab
python packaging/build_test_lab.py --rid osx-arm64 --output artifacts/test-lab
```

The build uses the pinned agent in `dependencies/test-lab`, and produces a portable ZIP, SHA-256, immutable file manifest and source identity. Each extracted bundle is independent of this checkout. Test machines need no SDK, separately installed runtime, Git or Python. The optional `--connection` argument includes only the public dashboard connection JSON. Use a fresh output folder for each build.

On the development Mac, extract locally and run `bash Prepare-on-Mac-DEV.command` to sign, reseal and check the bundle before distributing it. This is ad hoc signing; Developer ID/notarization remains separate. Native execution still needs validation on each target OS.

Copy the matching bundle to each machine, copy `dashboard-connection.json` into its root, open Pair-with-Dashboard, enter a dashboard code, then Start-Agent. Pairing is per application and OS user; upgrading a bundle retains it. The application ID is `fractal-ai-toolkit`. The runner accepts `run --root ROOT --suite Functional --result-directory ROOT/results/RUN --no-open` and the standard All, Native and Desktop suites.

Functional wraps the existing offline .NET tests and retains case counts and logs. Hardware-category tests are excluded even if hardware opt-in environment variables are present. Skipped or empty coverage cannot pass. Native reports incomplete: Native transport and physical Fractal device checks require the separate explicitly scoped hardware workflow. Desktop reports incomplete when explicitly requested; All omits the inapplicable desktop suite.

Python planner/MCP checks remain development/CI checks. An All run cannot pass while required native/desktop coverage is missing. Synthetic tests do not establish physical device qualification.

All runners, including direct local runs, hold a machine-wide resource lease throughout test execution. Other applications wait and their agents continue heartbeats. Upgrade Preset Sender and the dashboard to agent 0.2.0 before mixing applications. Permission errors accessing the global lease stop the run; they never bypass it.

## Audited offline Windows profile

Use `--config packaging/test-lab-offline-windows.json` with `--rid win-x64` for
the explicit `offline-windows-no-midi` profile. It compiles the source allowlist,
omits native/CoreMIDI and network MIDI session tests, and additionally disables
command connection selection at compile time. All contains only the offline
Functional stage. Native and Desktop remain unavailable, even with inherited
hardware opt-ins. No MIDI driver, service, virtual port or API installation is
needed for these data and in-memory interface checks.

The pinned app-private Test Lab SDK 0.2.1 supports an application-declared run
budget from one second to one hour. Its default remains 15 minutes; this offline
profile declares 30 minutes for slower test hosts. The budget begins after the
host lease is acquired and expiration stops the child process tree. Waiting for
another application does not consume the execution budget. Pairing, host leases
and report collection are unchanged; test PCs install no system SDK or feature.

The restore deadline checks separate two outcomes. An unanswered preflight read
must time out before any upload, poison the session and prevent replay. A second
in-memory test holds an already-sent first header until its actual deadline
cancels, then verifies exactly one header, no body/footer/save and no later sends.
This exercises transfer uncertainty without assuming preflight completes within
an arbitrarily short wall-clock interval. Run just these checks with the compiled
`Fractal.Checks --restore-deadlines-only` entry point; it opens no MIDI ports.
