# Identify blocks in a preset grid

Use the contained [block catalogue](../catalog/block-identities.json) to translate numeric grid IDs into names and instance numbers. It covers 162 enum identities across 41 families, with separate model evidence and dated instance limits. No network, AI account or live device connection is needed for lookup.

The exporter adds `name` and `kind` to each grid cell while preserving its numeric `effect_id`, position and routing. Empty cells have a null name, shunts are labelled separately, and unknown or model-unqualified IDs retain a null name. The snapshot's `device.block_catalogue` carries source revisions and scope. Existing snapshots are immutable: decode the retained captures into a new export to obtain names; do not edit old hash-bound exports.

In MCP, call `fractal_query_block` with the model and either `query` (name or abbreviation) or `effect_id`. Firmware is optional recorded context, not a lookup into a firmware-qualified catalogue. Examples below are synthetic:

```json
{"model":"FM3","query":"Reverb"}
{"model":"FM9","effect_id":66}
{"model":"Axe-Fx III","query":"VOL"}
```

The provider-independent CLI offers the same resolver:

```text
dotnet runtime/fractal.dll blocks --model FM3 --query Reverb
dotnet runtime/fractal.dll blocks --model "Axe-Fx III" --effect-id 66
```

Results include `identity_source_ids`, `identification_status` and `availability`. Reverb IDs 66 and 67 mean Reverb 1 and Reverb 2. The June 2023 Blocks Guide lists two instances on Axe-Fx III/FM9 and one on FM3. Reserved enum values can have names while not being listed as available. Treat `documented_available` as a dated documentation fact, never a current device probe or authorization to insert a block. Input USB is distinct from Input 4: FM9's four documented Input blocks are Input 1, 2, 3 and USB.

The manufacturer's numeric appendix applies to Axe-Fx III. The independently pinned Gen-3 implementation supplies common numeric mappings for FM9/FM3. Where a family lacks that independent mapping, FM9/FM3 results have `identification_status: unqualified-for-model`, null `name` and a clearly qualified `candidate_name`. Dynamic Distortion is a documented family whose numeric IDs remain unmapped; searching for it returns that limitation. Do not guess or infer IDs from parameter-record column counts.

An effect **instance ID** identifies a placed block, such as Amp 1. It is distinct from an amp model ID or a Reverb algorithm/type ID. Naming a block does not establish its parameters, insertion defaults, DSP resources, exact-firmware availability or successful hardware editing. Use the separate [insertion workflow](block-insertion.md) and model/firmware-qualified evidence for edits.

Source locators, retrieval date and SHA-256 are retained in the catalogue: manufacturer MIDI revision 1.4 Appendix 1; Blocks Guide June 2023 Product Blocks Comparison, printed p3/PDF p4 and Input/Output pp4–5; pinned Gen-3 `EFFECT_BASES`/`effectName`. These are factual mappings and counts; manufacturer prose and full manuals are not bundled. The catalogue JSON is embedded in the contained core assembly, so CLI/MCP/export share one maintained resolver and no runtime fetch is needed.
