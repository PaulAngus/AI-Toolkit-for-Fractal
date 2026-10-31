# Experimental editor operations and preservation

The repository's contained release 0.4.0 adds typed editor operations and an opt-in empty-preset Scene MIDI test. The standalone skill still contains offline planners only; it cannot send these commands. A write request needs a compatible runtime and existing user authorization.

FM9 12.00 hardware qualified Scene MIDI instance 190 insertion at a caller-chosen empty cell, PC/CC settings across eight scenes, channels 1/16, value/controller variations, saving, same-target reload and exact recovery of the original empty saved preset. FM3/Axe-Fx III use common builders with their own grid/parameter profiles and synthetic coverage; their physical behavior remains unverified. Full-preset restore, editing populated presets and batches remain pending.

Use pinned [setParam.ts](https://github.com/TheAndrewStaker/mcp-midi-control/blob/59047175cfc4f23e092931b54a7c54f2bffde3ea/packages/fractal-midi/src/gen3/axe-fx-iii/setParam.ts), revision `59047175cfc4f23e092931b54a7c54f2bffde3ea`. Relevant symbols: `buildSelectCell`, `buildInsertBlock`, `buildClearBlockCompanion`, `buildSetParameterFloat`, `buildStorePreset`, `buildSwitchPresetSysEx`. This is independent implementation research. The manufacturer's [MIDI specification](https://www.fractalaudio.com/downloads/misc/Axe-Fx%20III%20MIDI%20for%203rd%20Party%20Devices.pdf) documents scene/status operations and the effect-ID enumeration, not private editor/save commands.

- Function 01/action 30 selects a cell; action 32 inserts. FM3 needs selection before insertion. Position is `(column - 1) * modelRows + row - 1`.
- Clear uses action 30 followed by action 33. Selection alone is not deletion.
- Typed field set uses action 09 with the field's value encoded as IEEE float32 in five low-first septets. Use each model's own field catalogue. Scene MIDI bank starts are 0/64/128/192 in the retained catalogues; this does not generalize to unrelated parameters.
- Save/reload actions 26/27 carry the zero-based target slot in low-first 14-bit septets. Saved-read addressing is high-first; do not interchange them.
- Status includes foot-controller/preset-FC entries 200/201 in the observed empty FM9 preset. It is not a literal grid listing.
- Newly inserted blocks can expose active retained instance settings. Read actual defaults; never assume Off.
- Saving with another selected scene changed the FM9 12.00 first body word to that scene's zero-based index. Restore the original default before saving ordinary edits. Other model/firmware semantics remain unverified.
- The qualified shape-record offset is 12 bytes after the actual instance prefix; removing a block for independent comparison must retain the following prefix. Do not hide an unexplained byte difference by excluding opaque fields.

Require exact backup integrity, fresh saved base hashes before saves, live and saved complete-table checks, names/routing/default preservation and a qualified recovery path. The pilot recovered only its own inserted block in an originally empty preset. A received saved dump is not a proven upload/restore stream. A send is unverified until readback; stop after rejection/timeout and reconcile through a fresh session without automatic replay. Cancellation may leave a partial state.
