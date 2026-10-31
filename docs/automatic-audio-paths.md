# Automatic forward audio paths

Release 0.20.0 adds deterministic routing between explicit occupied grid waypoints for FM9, FM3 and both Axe-Fx III capacities. It produces the same general edit bundle used by existing single-preset and batch executors, including complete verification, exact-result undo and reconciliation. Supported operations remain enabled; hardware qualification is separate.

## Request and policy

Start with an integrity-checked saved capture and its model/capacity/firmware context. Supply a schema-1 request containing `model`, `capacity`, `firmware`, zero-based `slot`, `base_sha256`, `persistence` (`save` or `edit-buffer`), `waypoints`, `shunt_id`, optional `sharing_policy` and optional `feedback_policy`.

`waypoints` contains 2–14 `{ "row": ..., "column": ... }` points in strictly increasing columns within the selected model's grid. Each point must already contain a known audio block or shunt. Select intermediate effects explicitly when the signal should pass through them. The planner does not choose a sound, create an audio block, move an existing block or route audio through Scene MIDI. New empty cells receive the caller's explicit saved shunt identity, 1024–1029; no device default is inferred.

Between waypoints, the planner uses empty cells and existing shunts. It minimizes new shunts first, new cables second, row movement third, then chooses the lexicographically lowest row sequence for a deterministic tie break. Grid edges advance one column at a time. This is a structural route, not an audibility guarantee: bypass modes, Return levels, global I/O configuration, external cables and DSP resources retain their existing evidence limits.

Default `sharing_policy: isolated` avoids intermediate shunts with incoming or outgoing cables outside the selected path. Existing exact path segments remain reusable, so repeating the same intent is a no-op. Explicit `allow-existing` permits shared intermediate shunts and preserves all their cables; review resulting fan-in/fan-out carefully. Explicit waypoints always retain their existing branches. Empty cells carrying dangling routing or unknown bits are not adopted, and destination routing words with unknown bits are not overwritten. Unknown data elsewhere is preserved rather than disabling supported edits.

The generated operations pass through the existing complete-image planner, including its Send/Return feedback checks. Default `feedback_policy: preserve` rejects a proposed new feedback-loop edge; explicit `allow` permits the reviewed loop without changing Return levels. If the selected structural path fails final validation, its conflict is reported and no executable bundle is written. The search does not optimize tone, scene bypass behavior, special-link placement or an alternative global feedback topology.

## CLI and MCP

`plan-audio-path --request PATH_REQUEST_JSON --capture SAVED_CAPTURE --destination NEW_DIRECTORY` runs offline. It returns the chosen path, added shunt/cable counts and the full ordinary edit plan with before/after routing and hashes. A successful bundle contains the usual plan/request/original/result files plus `audio-path.json` retaining the request and chosen coordinates.

MCP `fractal_plan_audio_path(backup_artifact, slot, persistence, waypoints, shunt_id, sharing_policy?, feedback_policy?)` returns a plan artifact. Pass that artifact to `fractal_prepare_preset_edit`, or include it in `fractal_prepare_edit_batch`. Existing trusted-host exact review, execution and undo rules apply. No ports open during planning or preparation.

The algorithm uses existing model-specific grid definitions and source-qualified destination-mask encoding; it introduces no new protocol or effect defaults. [Validation](path-listener-validation.json) covers independent expected whole images, repeat requests, obstacles, selected intermediate effects, shared-route policy, invalid inputs, standard Save/Reload and real CLI/MCP preparation. No new hardware evidence is claimed.
