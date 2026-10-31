# Knowledge scope and curation

This is a general-purpose Fractal Audio and MIDI toolkit. Its knowledge should help someone inspect, understand, compare or safely operate a supported device using any suitable application or AI client.

## Admission criteria

An entry must answer a reusable domain question or describe a reusable operation. It must identify its prerequisites, model/firmware scope, source, procedure, expected output and verification limits. A finding does not need to apply to every Fractal model; it needs to be usable by everyone within its stated scope.

| Historical material | Treatment |
|---|---|
| MIDI/SysEx framing, addressing, checksums and saved format | Retain with device/firmware and evidence qualification |
| Scene MIDI insertion and message tables | Retain as a generalized editing procedure; preserve default-scene and unrelated-content checks |
| Missing or delayed replies | Retain correlation, bounded recovery and measurement principles; exclude application bug chronology |
| Amp roster and amplifier attribution research | Retain qualified mappings, sources and unresolved joins |
| Saved-data comparison and scene rearrangement | Retain the evidence requirements and ambiguity rules; exclude an application's tagging policies |
| Generic MIDI forwarding | Retain complete-message, session, buffering and ownership requirements; exclude project-specific patches and release instructions |
| Application UI, profile dialogs, deployment and version choices | Exclude |
| Chat transcripts, request-by-request history and development handovers | Keep outside the distributable repository |

Source provenance can name the application or repository where evidence was collected. That attribution is distinct from making its behavior, settings or bug fixes part of the toolkit's public task contract.

Public procedures take the target preset, scenes, block instances, channels, placement, ports and files as caller inputs. They must not adopt a contributor's setup as defaults. Remove personal paths, private filenames, preset numbers and session troubleshooting history from published evidence; retain neutral artifact hashes and aggregate verification outcomes. Model, firmware, protocol facts and tested limitations remain necessary evidence, rather than setup configuration. Synthetic fixtures may exercise arbitrary addresses and values without making them recommended operating settings.

## Evidence and updates

Manufacturer documentation, open implementations, recorded hardware observations, inference and unverified designs are separate evidence categories. Preserve a negative observation when it constrains the general operation; describe the implication without exporting the original troubleshooting conversation.

Retain original dates, firmware and source hashes. New observations can supersede a claim, but do not expand compatibility by analogy. A test of an application, a captured transaction and an offline toolkit test establish different things.

Distributable knowledge contains domain procedures, technical provenance and qualified evidence. Chat catalogues, application documentation copies and unrelated development workflows remain outside this knowledge base.

## Distribution checks

Run `python scripts/validate_distribution.py --source` to inspect public build inputs. Packaging also scans the completed bundle, including assemblies and debug symbols, for private artifact types, chat records and local user-profile paths. Compiled source paths use a portable virtual root. `python scripts/validate_distribution.py PACKAGE.mcpb` checks the actual archive independently.

These checks supplement review of domain scope; a pattern scan cannot decide whether every paragraph is reusable. Public source attribution and licence notices remain necessary. Qualified model/firmware observations and aggregate test results remain evidence, while contributor settings and actions remain private. See [distribution validation](distribution-validation.json).
