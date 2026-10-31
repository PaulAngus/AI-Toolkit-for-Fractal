# AI Toolkit for Fractal project guidance

## Test the actual deliverable

- General functional, native and acceptance tests must exercise the normal release build/package that users install, with its actual dependencies, publishing options, startup and installation path. Record the tested version and package/binary hashes.
- Reuse the same unchanged release candidate across acceptance scenarios. Do not create temporary application builds, alternate dependency versions, substitute application hosts, test-only product switches or patched binaries merely to make general tests run or pass.
- Test runners and automation drivers may be built separately, but must drive the unchanged normal application; component tests that host application code are not evidence that the shipped application works.
- If the installed application, helper or dependency is outdated or mismatched, report the discrepancy before proceeding with acceptance. Correct it through the normal source, version, build, package and installation process, then test that actual deliverable. Do not work around it with a parallel application variant.
- A temporary or instrumented application build is permitted only to investigate a specific observed problem. State the problem and the build differences explicitly, keep its evidence labelled diagnostic, and retest the normal release package after the fix.
- Keep unit, component, simulated and diagnostic results separate from release acceptance. They do not qualify the shipped application, native platform or hardware workflow.

## Purpose and boundaries

- Keep device knowledge and automation independent of AI providers. Provider packaging belongs in adapters; protocol and task behavior belong in shared, deterministic code.
- Target FM9, Axe-Fx III and FM3 from the outset. Keep task planning, CLI/MCP contracts and transaction handling common; isolate actual model/firmware differences in device definitions and adapters. Do not impose FM9-only gates or reuse FM9 constants/catalogues across models. Follow `docs/device-support.md`; historical FM9 evidence is a validation scope, not the toolkit's architectural boundary.
- Keep AI Toolkit for Fractal and PresetMaestro self-contained repositories. Each must contain the Fractal protocol source, build inputs and test fixtures needed for its supported functionality. Do not require another checkout, sibling-directory references, a separately running application or a remote feed to obtain the Fractal protocol code.
- Preserve one maintained protocol implementation through explicit versioned source releases. Each consumer carries its own complete release snapshot, with licence, notices, source revision and hashes. Update consumers independently; do not create divergent protocol rewrites or fetch missing protocol source during normal builds.
- Repository setup, documentation, offline planning, and tests do not authorize connecting to or modifying a live device. Match device operations to the user's requested device, presets, scenes, messages, and persistence behavior. Existing explicit session authorization should be honored without redundant questions.

## Knowledge and evidence

- Begin with `knowledge-base/README.md` and the relevant task recipe; use `skills/fractal-fm9/SKILL.md` for common normalized Scene MIDI planning and scoped FM9 evidence. For contained reads/export, begin with `docs/runtime.md`. Search `knowledge-base/catalog/capabilities.json` for a generalized task before rediscovering a method.
- Check `docs/capability-audit.md` for implementation gaps. Use `knowledge-base/reference-map.md` for retained document sections and code symbols before a broad documentation search. For FM9 12.00 amp questions, run `scripts/query_amp.py` and retain its attribution qualifications.
- Apply `docs/knowledge-scope.md`: include reusable Fractal/MIDI procedures, qualified device observations and transferable domain constraints. Exclude application-specific bugs, UI choices, release history and per-chat request records; generalize useful findings before admitting them.
- Preserve technical source attribution without importing an application's behavior into the toolkit contract. Source hashes establish which bytes informed the knowledge, not new hardware validation.
- Distinguish manufacturer documentation, open implementation, recorded hardware observation, inference, and unknown behavior. Record model, firmware scope, source, date, and whether a claim comes from a transcript or retained bytes.
- Preserve negative results, partial runs, and limitations. Tests of synthetic data do not establish hardware compatibility. A command's absence of an error reply does not establish persistence.
- Qualify historical research with newer evidence: some old notes predate the firmware query and later hardware testing.
- Keep transcripts, tokens, personal device names, full user preset dumps, and backup collections out of distributable artifacts. Add non-personal or explicitly approved fixtures with provenance when needed.
- Keep public workflows and validation records generic. Do not carry a user's preset numbers, block/channel choices, MIDI port indices, local paths, private filenames or troubleshooting chronology into reusable documentation or defaults. Use neutral artifact identifiers for evidence hashes. Retain model/firmware and factual test limits to avoid implying untested compatibility; examples and fixtures must be clearly synthetic or caller-supplied.

## MIDI implementation

- Coordinate one owner of a device's MIDI ports. An integration hosted by another application must use its existing coordinated transport; a standalone runtime owns its own connection.
- Match replies by model, function, length, checksum, and available address fields. Serialize requests. Scene-name replies omit preset identity; verify stable preset state around live snapshots.
- Bound timeouts and recovery. Use readback to resolve uncertain writes instead of resending them blindly. The FM9 10 ms query spacing is an application policy, not a manufacturer requirement.
- Keep zero-based device preset slots distinct from display offsets. Use scenes 1–8 and MIDI channels 1–16 at the task boundary; document conversions in transport adapters.
- Preserve existing MIDI messages, routing, names, other parameters, and the saved default scene. A Scene MIDI block belongs to the preset; its message settings vary by scene.
- Use the shared offline block-insertion operation for chosen grid placement. A historical task's preferred cell is not a toolkit default. Resolve exact block instance IDs from model/firmware evidence; grid-only planning does not establish availability, DSP resources or new-block defaults.
- Require integrity-checked backups and base-state comparison for persistent edits. Saving and restoring hardware require verified target-specific operations; a received dump is not automatically a verified restore stream.

## Verification

- Run `python -m unittest discover -s tests -v` after planner changes. Cover conflicts, repeat runs, invalid input, and preservation of unrelated settings.
- Run `python scripts/validate_knowledge.py` after knowledge changes to verify capability records, evidence references, roster bounds, domain scope and portable links.
- Hardware tests are explicitly separate from offline checks and normal CI. Report their real scope, including incomplete writes and restoration failures.
- No UI is currently implemented. If a UI is introduced, establish and document the relevant platform design guidance and inspect rendered results.

- After runtime changes, run `python scripts/check_cli_pipeline.py`; this exercises .NET checks and offline CLI export/decode/plan across all model definitions. Deliberately update/check `docs/protocol-release.json` through `scripts/protocol_release.py` after reviewed source changes. Do not fetch another checkout during builds.

## Local MCP adapter

- Use `docs/mcp.md` for stdio/client contracts and exact local review approval. Do not add provider dependencies or expose arbitrary shell/SysEx execution. Honor existing exact user authorization through the trusted host; model-controlled calls cannot authorize themselves.
- Run `python scripts/check_mcp_pipeline.py` after adapter changes. It relocates a complete source/runtime bundle and uses real stdio/helper binaries without MIDI. The official SDK interoperability script is optional testing, never a normal build/runtime dependency.

## Keep incidental failures separate from the active change

- User instruction, 22 October 2026: discovering an unrelated test failure does not authorise fixing it, changing MIDI behaviour, upgrading a dependency or expanding the current task. Preserve the active change's purpose and pinned dependencies. Continue independent in-scope verification; report any assertion that cannot be exercised as blocked/incomplete.
- For core application/UI verification, use an immutable platform plan with `workstream: application-change`. Other dependency changes require a separate `dependency-change` plan. Shared library fixes and MIDI dependency adoption require a separate explicitly selected `shared-midi-change` plan and their own regression/affected-consumer verification. Never silently broaden a UI change into that workstream. Normal disconnected application behaviour may support UI verification; do not add a test-only startup mode, suppress normal sync artificially or substitute an application host.
- Record incidental failures in the shared platform's findings register (`GET/POST /api/findings` or dashboard Separate test findings). Retain the originating plan/job, actual package/library pins, platform/backend/route, reproduction and evidence. Use the existing finding ID for recurrence on another project/platform. Collected failed/incomplete/interrupted non-diagnostic runs enter the register unclassified; triage the cause without automatically repairing it.
- Distinguish candidate regressions, demonstrated pre-existing defects, environment/deployment mismatches, test defects and inconclusive findings. A pre-existing assessment needs unchanged-baseline evidence; suspicion stays unclassified/inconclusive. A defect caused by the current change remains in scope. If an existing defect blocks a selected assertion, classify its relation as blocks-change, not unrelated, and retain the incomplete/failed result.
- A finding is neither a waiver nor permission to schedule diagnosis, change pins, approve a baseline or release an unresolved device reservation. Keep focused change status and full qualification separate. Register permanent regressions centrally when a separately scoped fix is undertaken; test unchanged normal releases through paired dispatch and preserve restoration requirements.
- An intentionally pinned older library is not a deployment mismatch merely because a newer version exists. Compare the installed payload with this plan's intended normal release. Report genuine mismatches and keep dependent acceptance incomplete; correcting them must not silently upgrade the dependency or repair product MIDI code during a UI task. Use a separately scoped normal publication/install change where needed.
- Follow the platform's `docs/incidental-test-findings.md` (in the sibling `crossplatform-testing` repository for consumers) for recording, triage and separate-workstream handling. AI Toolkit remains optional and QuickLinks remains excluded from all testing.

- When explicitly selected, release functional/integration tests must invoke the unchanged normal distributed CLI/MCP processes in real-world conditions. Pinned file inputs qualify only file-processing assertions; fake device replies, source-hosted product code and test-only product modes cannot qualify live operation. Storage scenarios require full verified backup/restoration at the normal path. Follow `../crossplatform-testing/docs/release-functional-audit-2026-10-22.md`. Toolkit remains outside general core-project testing.
