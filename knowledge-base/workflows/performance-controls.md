# Control tempo, tuner and looper

Resolve the exact model, firmware, selected preset and coordinated MIDI owner. Capture the complete addressed saved preset and current temporary buffer before any change; retain selection and block status as well. A looper operation requires a present model-qualified Looper block and a fresh six-bit state observation.

Choose an absolute intent where verification exists: integer BPM within that model's documented range, or explicit playing/reverse/half-speed state. If the queried value already matches, send no control event. Keep record, undo, once and tap as intentional single button events. Never replay an uncertain event. An explicit tuner on/off command has no documented state query in the retained MIDI specification; state confirmation and verified no-op remain unavailable.

Manufacturer owner manuals document 20–250 BPM for FM9, FM3 and Axe-Fx III and explain that manual tempo changes can update global and current unsaved preset tempo. A live tempo operation does not Save. Global tempo and recorded loop audio are outside a saved-preset backup. Preserve tempo-to-use and other settings; do not infer global restoration from preset equality.

The Axe-Fx III third-party MIDI specification documents looper query/buttons, tap, tuner Boolean and tempo query/set. Retained open implementations bind tempo queries across the three model bytes and implement FM9 tempo read/set. Other FM9/FM3 forms remain inferred adaptations, not manufacturer or toolkit physical qualification. Query replies require exact framing, model, function, checksum, length and bounded values; published checksumless pushes cannot satisfy queries.

An upstream report says a tempo query changed tempo on unspecified early Axe-Fx III firmware. Keep this negative evidence qualified: no exact firmware or retained hardware capture establishes a safe boundary. Issue tempo queries only under explicit reviewed scope, including a requested read. Ordinary read snapshots exclude them. Compare retained captures after a query and stop before a set when preservation is uncertain. Indistinguishable unsolicited replies and global-only side effects remain limitations.

Retain before/after observations and every partial failure. Loaded tempo changes currently have no qualified ownership mapping; differences require reconciliation rather than a claim that unrelated settings were preserved. Tuner pitch pushes and observed looper flags do not establish all command effects or loop audio contents. Respect the manufacturer's MIDI Out push-stream scope without assuming USB coverage.

See [runtime contracts and source links](../../docs/performance-controls.md), [validation scope](../../docs/performance-controls-validation.json) and [transaction reliability](midi-reliability.md). Synthetic coverage across all device definitions does not establish physical compatibility or authorize a live operation.
