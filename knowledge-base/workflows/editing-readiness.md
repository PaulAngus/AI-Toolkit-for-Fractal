# Inspect saved editing data offline

Use a complete integrity-checked saved capture, its actual model, established capacity and recorded firmware. The [offline read commands](../../docs/editing-read-profiles.md) report model-specific candidate audio fields and grid edges, with explicit remaining gaps without connecting to a device.

1. Inspect `edit-capabilities` and `edit-catalogue` for the intended model/firmware. A catalogue entry is read evidence, not write support or authorization.
2. Run `decode-edit` on the original saved capture. Keep it private; names and complete preset bytes appear in the report.
3. Check exact instance associations and individual field statuses. A numeric ID with an unknown name remains useful evidence; a record with matching dimensions alone does not identify a block.
4. Use saved audio/state/name reads only within their provisional qualifications. Editor IDs and display hints are documentary mappings, not saved offsets or calibrated conversions. Cab mode conditions must be respected. Retain unknown raw fields; do not coerce them into defaults.
5. Inspect routing status and issues before using edges. Recognized destination bits name source rows in the preceding column. Null edges indicate unsupported layout; partial reports preserve valid edges alongside unknown bits or unresolved endpoints. Grid edges do not establish special-path connectivity, legal writes or audible behavior.

Reports bind source/profile hashes and preserve opaque body/grid/name bytes. All-model synthetic tests establish software behavior only. FM9 historical layout evidence remains scoped to its recorded firmware and operations; no new hardware compatibility is claimed. General editing and connected routing require the remaining qualified profiles and separately guarded execution.
