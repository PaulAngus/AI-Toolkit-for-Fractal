# Read live block parameters and interpret private changes

Use the capability `read-live-block-parameters` for an explicitly selected current block instance. Begin with [runtime contracts](../../docs/runtime.md) and [private parameter contracts and evidence](../../docs/live-parameters.md). Reuse the existing coordinated transport; setup and offline tests do not authorize hardware access.

Resolve the exact block instance for the model. Read the current block table through the CLI or MCP using caller-supplied firmware/transport context. Retain the private report and raw frames, including partial failures. Report raw values with the own-model symbolic mappings and their qualifications. A model catalogue describes editor addresses, not saved offsets, physical scales, default values or hardware compatibility.

Passive observation uses the same mapping layer without sending queries. Complete private tables, individual parameter echoes and unknown or malformed frames remain distinguishable. Notifications carry no reliable preset/channel identity or persistence proof. Use fresh bounded reads to resolve an intended task; do not replay uncertain writes or automatically refresh hardware from notification hints.

Public reusable evidence excludes personal presets, connection endpoints and capture filenames. Keep physical tests separately scoped to the actual model, firmware, block family and operation.
