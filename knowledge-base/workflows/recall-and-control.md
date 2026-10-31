# Recall presets/scenes and control effect state

Use the [standalone controller listener](../../docs/controller-listener.md) to receive bounded note/CC input from an explicitly selected port. It retains mapped intents without sending or executing them; hardware identity, fresh target context and execution approval remain separate.

General task: translate an explicit target into the device's configured MIDI messages, then verify actual state. Use the [configured MIDI runtime](../../docs/configured-midi-routing.md) for bank/PC recall, assigned CC scene/channel/bypass controls and host-fed note/CC input mapping. These share captured-state verification and exact review with the [capture-bound load operation](../../docs/preset-load.md). Explicitly resolve retained unsaved differences before navigation; automatic recovery continues to refuse them.

## Preset and scene recall

Keep the zero-based device slot separate from a displayed number. Compute `slot = displayed_number - display_offset`, validate it against the established device capacity, then use `bank = slot // 128` and `program = slot % 128`. On the FM9, send bank-select CC 0 followed by the Program Change on the configured receive channel. Include bank select on each recall to avoid relying on an earlier bank selection. Check whether the device's custom PC mapping is enabled before assuming direct addressing.

For example, slot 129 uses bank 1 and wire PC 1. A controller displaying programs 1–128 may display that wire PC as 2; check its numbering convention. See the FM9 manual's **MIDI Reference Tables → MIDI Bank & Program Change**, linked in the [reading map](../reference-map.md).

To select scene 1–8 through an assigned Scene Select CC, use value `scene - 1`. Read the actual CC assignment from the target configuration; CC 34 is a common example, not a universal assignment. Send scene selection after the preset load has completed. The alternate SysEx scene operation and readback formats are in the [protocol reference](../../skills/fractal-fm9/references/fm9-protocol.md).

Confirm the active preset and scene from correlated replies. A successful transport send is not acknowledgement from the device. Loading a preset can discard unsaved changes and activate outgoing Scene MIDI messages; include those effects when defining a navigation task. Recording and restoring the former selection does not restore discarded edits.

Recorded FM9 12.00 observations on 12 October 2026 show that a direct recall can leave the selected scene different from the target's saved default, and a post-Save Reload can select a different scene even after a verified Save. In the retained captures, the decoded temporary buffer differed from the saved target only in the selected-scene word; the compressed stream and its length could also change. This is hardware observation, not a manufacturer rule or evidence for another model/firmware. Before one absolute scene selection, verify the complete saved result is unchanged and the loaded body has no other difference, then reread the complete target and selected scene. The stopped and completed trials are distinguished in [editing validation](../../docs/preset-editing-validation.json).

## Effect bypass, channels and controllers

Match a command to the intended block, the device's assigned CC and its value semantics. Distinguish explicit state setting from toggle behavior. Where the device implements toggling, forward the original press command once; a second route can toggle twice and appear to do nothing. Do not convert an unchanged controller command into a second toggle.

Block channel selection, bypass and scene selection are different operations. A block may be absent, have Scene Ignore enabled or select a different channel in another scene. Read actual state or qualify feedback as unknown. A controller's last-press LED and static label do not establish device state.

Note-triggered control requires an explicit note/channel/press-release mapping in the receiving automation. Do not assume that a Fractal device directly interprets a keyboard note as preset recall. Handle release and Note On with velocity zero according to the chosen input contract, so one press does not unintentionally execute twice.

Use [controller integration](controllers.md) for version-qualified labels and feedback, and [MIDI routing diagnostics](midi-reliability.md) for port/route checks. [Absolute live scene/channel/bypass controls](../../docs/live-tools.md) support direct SysEx or explicit CC assignment through CLI/helper and MCP, using exact context and readback. Note mapping accepts events from a coordinated host, finite offline input or the bounded standalone listener. Input observation never grants itself execution authorization.
