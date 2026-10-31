# Discover amp models and their real amplifier families

General task: connect firmware-qualified numeric model IDs to exact Fractal names, real amplifier families and qualified variants.

Validate three separate joins:

1. Device family/firmware → available numeric IDs and exact Fractal names.
2. Exact Fractal model/variant → real manufacturer and amplifier family.
3. Variant → channel, circuit, modification or year/revision, with uncertainty retained.

Knowing the real amplifier behind a name does not prove its numeric ID. Preset scanning finds models already used, not all models available. IDs must not silently cross device or firmware scopes.

## Recorded FM9 roster method

For **FM9 firmware 12.00**, the observed read-only editor roster query uses function `0x01`, sub-action `0x1F`, effect 58, parameter 10. Its ordinal uses low-first seven-bit fields. Validate checksum and effect/parameter. Responses omit the ordinal; serialize queries and stop on timeout to avoid assigning a late name to the wrong ID.

Recorded full-frame template: `F0 00 01 74 12 01 1F 00 3A 00 0A 00 [entryLo] [entryHi] 00 00 00 00 00 00 00 [checksum] F7`. Offsets count `F0` as byte 0. The ordinal is at bytes 12/13; the reply's name uses an MSB-first septet stream starting at byte 21. Recalculate checksum, validate descriptor/effect/parameter and decode the recorded format. A current Amp-type query is a different operation. The [contained live roster reader](../../docs/live-tools.md) now sends the bounded query, using each model’s own type address and retaining reply hashes. Other models/firmware remain physically unqualified; no count or end marker is inferred.

The recorded roster contains **336 ID/name pairs**. A later bounded FM9 12.00 live read of those same requested ordinals validated every reply and matched all retained ID/name pairs exactly. A separate query of the next ordinal stopped on an unknown/empty-name encoding, which is not a verified firmware termination signal. The [portable roster](../catalog/fm9-12-amp-roster.json) retains the original pairs and capture hash, excluding raw replies and personal device names. It includes Class-A 30W Bright = 233, Friedman BE 2010 = 287 and HBE 2010 = 288. Those names are distinct from real-amplifier attributions.

## Research attribution

Prefer Fractal's documentation for its own model basis. Qualify independent corroboration; two websites repeating one upstream Wiki do not constitute two independent sources. Preserve conflicting evidence and keep family identification separate from exact revision claims. FAS virtual originals need no single physical equivalent.

Group related variants under the real family while retaining exact Fractal names, aliases and raw IDs. Deduplicate a preset containing multiple variants. Separate **contained in a saved channel**, **selected in a scene** and **engaged in a scene**; conditional Scene Ignore state can qualify the last interpretation.

Attach source, date, model/firmware and confidence to each mapping. A firmware-qualified catalogue can include unused entries; complete compatible scans establish usage counts. Unknown firmware or IDs should remain visible and explicitly unresolved, without being labelled using another family's table.

## Local lookup and previously researched questions

The [reviewed identities](../catalog/fm9-12-amp-identities.json) cover all 336 recorded names in 162 family/original groups. They retain manufacturer, exact variant, physical/virtual/schematic relationship, qualifications, source locators and variant-specific Wiki links. Family confidence does not verify every year, circuit or modification. Input hashes establish provenance, not a new hardware test.

```powershell
python scripts/query_amp.py --query "block crunch"
python scripts/query_amp.py --id 263 --json
python scripts/query_amp.py --query "Fender Bassman"
```

The first lookup identifies PVH 6160 Block Crunch as Peavey 5150 Block Letter, distinguishing family authority from the Wiki's channel detail. The second returns Porta-Bass's established B-15 family and qualified B-15R inference, with its useful archived-table reference. The tool rejects other model/firmware scopes and returns no matches for unknown names rather than inventing an attribution.

To find unused amps, subtract the contained numeric IDs in a **complete, compatible saved inventory** from that firmware's available roster. A sample, names-only scan or partial inventory cannot establish absence. Scene-selected or engaged usage answers a different question. Use the contained [inventory scanner and amp-usage analysis](../../docs/preset-inventory.md), including CLI and MCP access. An FM9 12.00 trial joined the live validated 336-ID range with a complete decoded saved inventory and returned qualified absence within that supplied range. The roster input explicitly binds model and firmware; incomplete or undecodable coverage produces unknown absence.

Reading sources, historical access and shared lineage are in the [source register](../catalog/sources.md). Use the [reading map](../reference-map.md) before another broad documentation search.
