"""MCP-facing task contracts. Encoding, planning and device behavior stay in contained implementations."""
import hashlib
import importlib.util
import shutil
import threading
import time
import base64
from pathlib import Path
from .schema import ID, HASH, MODEL, FIRMWARE, SLOTS, BOOL, array, integer, obj, string, validate
from .storage import Artifacts, atomic_json, confined, dumps, sha
from .runtime import Runtime, device_owner

READ_INTERPRETATION = {
    'preset_identity': 'An addressed saved snapshot slot is not the current selection. Report current preset/scene only from explicit current-state fields; otherwise state that they are unknown. Apply only an explicit display offset.',
    'saved_and_live': 'Grid, names, amp models and scene assignments decoded from saved bytes describe saved state. A matching live Scene MIDI table establishes equality only for that table, not other live parameters or unsaved edits.',
    'midi_traffic': 'Offline lookup/decoding/planning sends no MIDI. Live read-only operations send MIDI/SysEx queries but do not intentionally select, edit or save. Reading Scene MIDI configuration does not prove its configured PC/CC messages were transmitted or received.',
    'receiver_numbering': 'Distinguish raw PC data bytes (0–127), task MIDI channels (1–16) and receiver display numbering/routing/settings. Do not infer receiver behaviour from stored messages.',
    'selection_observation': 'Equal before/after selection means no observed lasting drift between sampled endpoints; it cannot exclude a transient change and return. Notifications or wire captures do not prove all internal changes are observable without model/firmware evidence of complete coverage.',
    'reference': 'fractal://reference/knowledge-base/workflows/device-reads.md',
}


EDIT_OPERATION = obj({
    'kind': string(enum=['set-parameter', 'set-block-type', 'copy-channel', 'copy-block-settings', 'swap-channels', 'rename-scene', 'set-scene-block-state', 'set-scene-midi',
                         'insert-block', 'remove-block', 'insert-shunt', 'remove-shunt', 'connect', 'disconnect',
                         'set-calibrated-parameter', 'set-calibrated-modifier', 'set-scene-ignore', 'set-modifier-source', 'copy-modifier', 'set-scaled-parameter', 'set-modifier-parameter', 'copy-modifier-selector']),
    'effect_id': integer(1, 65535), 'channel': string(enum=['A', 'B', 'C', 'D']), 'other_channel': string(enum=['A', 'B', 'C', 'D']), 'key': string(),
    'value': {'type': 'number', 'minimum': 0, 'maximum': 65535}, 'unit': string(),
    'scene': integer(1, 8), 'name': string(minLength=0, maxLength=31), 'bypassed': BOOL,
    'row': integer(1, 6), 'column': integer(1, 14), 'source_row': integer(1, 6), 'source_column': integer(1, 14),
    'destination_row': integer(1, 6), 'destination_column': integer(1, 14),
    'policy': string(enum=['preserve-other-fields', 'replace-shunt', 'disconnect', 'reconnect', 'replace-with-shunt', 'replace', 'preserve-scene-references', 'follow-channel-settings']),
    'message_slot': integer(1, 8), 'message_type': string(enum=['Off', 'PC', 'CC']), 'midi_channel': integer(1, 16), 'controller': integer(0, 127),
    'expected_raw': {'type': 'array', 'items': integer(0, 65535), 'minItems': 4, 'maxItems': 4},
    'expected_binding': {'type': 'array', 'items': integer(0, 65535), 'minItems': 3, 'maxItems': 3},
    'calibrated_value': {'type': 'number', 'minimum': -1000000000, 'maximum': 1000000000},
    'physical_value': {'type': 'number', 'minimum': -1000000000, 'maximum': 1000000000},
    'calibration_artifact': ID, 'enabled': BOOL, 'modifier_slot': integer(1, 128), 'source_id': integer(0, 16383), 'template_modifier_slot': integer(1, 128),
    'template_artifact': ID, 'template_slot': integer(0, 1023),
    'template_channel': string(enum=['A', 'B', 'C', 'D']),
}, ['kind'])


def tool(name, description, properties, required=None, *, read=True, local=True):
    return {'name': 'fractal_' + name, 'description': description, 'inputSchema': obj(properties, required),
            'annotations': {'readOnlyHint': read, 'destructiveHint': not read, 'idempotentHint': read, 'openWorldHint': not local}}


MIDI_CC = obj({'channel': integer(1, 16), 'controller': integer(0, 127), 'value': integer(0, 127)})
MIDI_MAPPING = obj({'mode': string(enum=['direct', 'custom']), 'channel': integer(1, 16), 'bank_controller': integer(0, 127), 'bank': integer(0, 127), 'program': integer(0, 127)})
MIDI_TARGET = obj({'kind': string(enum=['preset', 'scene', 'channel', 'bypass']), 'slot': integer(0, 1023), 'scene': integer(1, 8),
                   'effect_id': integer(1, 1000), 'channel': string(enum=['A', 'B', 'C', 'D']), 'bypassed': BOOL, 'cc': MIDI_CC, 'preset_mapping': MIDI_MAPPING}, ['kind'])
ORGANIZATION_OPERATION = obj({
    'kind': string(enum=['rename', 'copy', 'move', 'swap', 'reorder', 'import']), 'slot': integer(0, 1023),
    'source_slot': integer(0, 1023), 'destination_slot': integer(0, 1023), 'name': string(minLength=0, maxLength=31), 'overwrite': BOOL,
    'vacated_policy': string(enum=['replace-from-capture']), 'replacement_slot': integer(0, 1023),
    'assignments': array(obj({'source_slot': integer(0, 1023), 'destination_slot': integer(0, 1023)}), 32),
    'donor_artifact': ID, 'donor_file': string(), 'donor_sha256': HASH, 'donor_model': MODEL,
    'donor_capacity': integer(512, 1024), 'donor_firmware': FIRMWARE,
}, ['kind'])
SONG_SECTION = obj({'number': integer(1, 6), 'name': string(minLength=0, maxLength=10),
                    'preset_slot': integer(0, 1023), 'scene': integer(1, 8), 'default_scene': {'type': 'boolean', 'enum': [True]}}, ['number', 'name'])
SONG = obj({'number': integer(1, 128), 'name': string(minLength=0, maxLength=10),
            'sections': {**array(SONG_SECTION, 6), 'minItems': 0}})
SETLIST = obj({'number': integer(1, 4), 'name': string(minLength=0, maxLength=10),
               'songs': {**array(integer(1, 128), 32), 'minItems': 0, 'uniqueItems': False}})
TOOLS = [
    tool('plan_setlists', 'Create a toolkit-native offline song/section/setlist proposal against a retained complete or partial inventory. Songs/sections/sets are 1-based; preset slots are 0-based. Enabled sections require exactly one scene (1–8) or default_scene=true; disabled sections omit both and preset_slot. Repeated songs in sets are allowed. Reports unresolved preset/song/FC dependencies; never reads/writes system data, plays MIDI or creates editor-importable files.',
         {'inventory_artifact': ID, 'songs': {**array(SONG, 128), 'minItems': 0}, 'setlists': {**array(SETLIST, 4), 'minItems': 0},
          'active_setlist': integer(1, 4), 'fc_controller_present': BOOL}, ['inventory_artifact', 'songs', 'setlists']),
    tool('plan_preset_organization', 'Plan saved preset rename, copy, explicit replacement-based move, swap, closed permutation or same-context individual-file import. All assignments use immutable original captures. Changed copy/move/import destinations require overwrite=true. Preserves complete donor data/default scene; no external reference updates or MIDI.',
         {'backup_artifact': ID, 'operations': array(ORGANIZATION_OPERATION, 32)}),
    tool('inspect_preset_organization', 'Revalidate one retained organization bundle and show names, ordered destinations, overwritten originals, hashes and limits offline.', {'organization_artifact': ID}),
    tool('prepare_preset_organization', 'Freeze organization plus retained current saved/unsaved context into an exact ordinary batch execution review. Sources/destinations guarded; non-atomic progress retained. No MIDI during preparation.',
         {'organization_artifact': ID, 'context_artifact': ID, 'discard_changes': BOOL}, ['organization_artifact', 'context_artifact']),
    tool('prepare_organization_reconciliation', 'Prepare query-only reconciliation of retained organization destinations and source guards. Never replays uncertain uploads or Saves.', {'organization_review_artifact': ID}),
    tool('plan_preset_organization_undo', 'Validate exact current-release completed/stopped batch progress and full completed-item receipts. Plan originals only for verified changed targets whose current capture matches result or original. Uncertain/untouched targets are ineligible; later changes refused. Produces a new organization plan needing fresh context and exact review.',
         {'execution_artifact': ID, 'progress_sha256': HASH, 'backup_artifact': ID}),
    tool('import_template', 'Retain a named complete block/channel donor in the private library from an integrity-checked backup. Firmware/provenance labels remain caller-declared. Does not open MIDI or alter presets.',
         {'backup_artifact': ID, 'slot': integer(0, 1023), 'effect_id': integer(1, 1000), 'kind': string(enum=['block', 'channel']),
          'name': string(maxLength=80), 'tags': {**array(string(maxLength=32), 16), 'minItems': 0},
          'provenance': string(maxLength=512), 'channel': string(enum=['A', 'B', 'C', 'D'])},
         ['backup_artifact', 'slot', 'effect_id', 'kind', 'name', 'provenance']),
    tool('list_templates', 'Search the private template library by name/tags with bounded paging. Returns metadata and exact current entry hashes, never donor captures. No MIDI.',
         {'query': string(maxLength=80), 'tag': string(maxLength=32), 'offset': integer(0, 5000), 'limit': integer(1, 100)}, []),
    tool('inspect_template', 'Revalidate one exact private library entry and report its shape, provenance and copy footprint without returning donor captures. No MIDI.',
         {'entry_id': ID, 'entry_sha256': HASH}),
    tool('plan_template', 'Apply an exact private library entry to a retained baseline using existing copy-channel, copy-block-settings or insertion. Produces an ordinary frozen edit bundle; later execution uses normal exact operator review. No MIDI.',
         {'entry_id': ID, 'entry_sha256': HASH, 'backup_artifact': ID, 'slot': integer(0, 1023), 'persistence': string(enum=['save', 'edit-buffer']),
          'effect_id': integer(1, 1000), 'kind': string(enum=['copy-channel', 'copy-block-settings', 'insert-block']),
          'channel': string(enum=['A', 'B', 'C', 'D']), 'row': integer(1, 6), 'column': integer(1, 14), 'policy': string(enum=['replace-shunt'])},
         ['entry_id', 'entry_sha256', 'backup_artifact', 'slot', 'persistence', 'effect_id', 'kind']),
    tool('calibrate_saved_parameter', 'Validate 2–16 caller-labelled saved captures differing in one settings word. Produces exact-value block, Scene Ignore or modifier calibration evidence for matching context; no interpolation, inferred scales, MIDI or hardware qualification.',
         {'effect_id': integer(1, 1000), 'channel': string(enum=['A', 'B', 'C', 'D']), 'key': string(maxLength=80), 'unit': string(maxLength=40),
          'kind': string(enum=['parameter', 'scene-ignore', 'modifier']), 'modifier_slot': integer(1, 128),
          'expected_binding': {'type': 'array', 'items': integer(0, 65535), 'minItems': 3, 'maxItems': 3},
          'samples': {**array(obj({'backup_artifact': ID, 'slot': integer(0, 1023), 'value': {'type': 'number', 'minimum': -1000000000, 'maximum': 1000000000} }), 16), 'minItems': 2}},
         ['effect_id', 'key', 'unit', 'kind', 'samples']),
    tool('read_block_parameters', 'Read one present block instance using a bounded private table query. Maps raw words to own-model editor IDs and retains partial evidence. No physical-unit conversion, writes or saved-state claim.',
         {'effect_id': integer(1, 1000)}, local=False),
    tool('listen_device_notifications', 'Capture bounded passive device messages: tempo/tuner pushes, known state reply formats and unknown frames. No MIDI queries or writes; network session control may be sent. Reports refresh hints without claiming saved persistence or complete change coverage.',
         {'duration_seconds': integer(1, 30), 'max_events': integer(1, 4096)}, local=False),
    tool('plan_audio_path', 'Plan a deterministic forward grid path through caller-chosen occupied waypoints, adding explicit shunts and cables. Preserves existing blocks and cables; sharing and feedback policies are explicit. Produces an ordinary executable edit bundle offline.',
         {'backup_artifact': ID, 'slot': integer(0, 1023), 'persistence': string(enum=['save', 'edit-buffer']),
          'waypoints': {**array(obj({'row': integer(1, 6), 'column': integer(1, 14)}), 14), 'minItems': 2},
          'shunt_id': integer(1024, 1029), 'sharing_policy': string(enum=['isolated', 'allow-existing']), 'feedback_policy': string(enum=['preserve', 'allow'])},
         ['backup_artifact', 'slot', 'persistence', 'waypoints', 'shunt_id']),
    tool('listen_controller', 'Listen to one explicitly selected controller MIDI input for a bounded interval and retain mapped note/CC intents. Opens input only and sends no MIDI. Results require fresh capture and normal exact execution reviews.',
         {'input': integer(0, 65535), 'duration_seconds': integer(1, 30), 'max_events': integer(1, 4096),
          'rules': array(obj({'input_kind': string(enum=['note', 'cc']), 'input_channel': integer(1, 16), 'number': integer(0, 127), 'value': integer(0, 127), 'target': MIDI_TARGET}, ['input_kind', 'input_channel', 'number', 'target']), 64)}, local=False),
    tool('route_midi_events', 'Map explicit note/CC input events to configured absolute intents offline. Handles releases and repeated events. Returned targets feed capture-bound reviews; no automatic execution or MIDI ports.',
         {'model': MODEL, 'capacity': integer(512, 1024), 'firmware': FIRMWARE,
          'rules': array(obj({'input_kind': string(enum=['note', 'cc']), 'input_channel': integer(1, 16), 'number': integer(0, 127), 'value': integer(0, 127), 'target': MIDI_TARGET}, ['input_kind', 'input_channel', 'number', 'target']), 64),
          'events': {**array(obj({'kind': string(enum=['note-on', 'note-off', 'cc']), 'channel': integer(1, 16), 'number': integer(0, 127), 'value': integer(0, 127)}), 256), 'uniqueItems': False}}),
    tool('prepare_edit_batch', 'Freeze 1–32 distinct saved-edit plans with one captured initial context into an ordered execution review. Verifies each target, checkpoints progress and stops on uncertainty. No MIDI in preparation.',
         {'context_artifact': ID, 'plan_artifacts': array(ID, 32), 'discard_changes': BOOL}, ['context_artifact', 'plan_artifacts']),
    tool('prepare_batch_reconciliation', 'Prepare query-only comparison of every target in a retained batch. Never replays an uncertain upload or Save.', {'batch_artifact': ID}),
    tool('monitor_live_state', 'Take finite query-based samples of current selection and effective block status. Retains a private report; does not claim complete internal or unsolicited change coverage.',
         {'samples': integer(1, 120), 'interval_ms': integer(100, 10000)}, local=False),
    tool('read_amp_roster', 'Query an explicit amp ordinal range using the configured model and firmware. Retains raw replies and partial failures; range completion never claims a complete firmware roster.',
         {'start': integer(0, 16383), 'count': integer(1, 1024)}, local=False),
    tool('capture_live_control_context', 'Capture current selection, block status and complete saved/unsaved images for exact live-control review. Queries only; no navigation or Save.', {}, local=False),
    tool('capture_performance_context', 'Capture complete saved/unsaved current preset, selection, block status and looper flags when a Looper is present. Uses bounded queries; never queries tempo or changes tuner state.', {}, local=False),
    tool('query_performance_capabilities', 'Read model-qualified tempo/tuner/looper controls and their evidence and verification limits offline.',
         {'model': MODEL, 'capacity': integer(512, 1024), 'firmware': FIRMWARE}),
    tool('prepare_performance_control', 'Freeze one exact performance operation for normal trusted-host execution review. Tempo read/set includes explicitly reviewed queries because upstream reports query-as-set on unspecified early firmware. BPM 20–250; tap/button is one event. Tuner has no documented state query. No Save or MIDI during preparation.',
         {'context_artifact': ID, 'kind': string(enum=['tempo-read', 'tempo-set', 'tempo-tap', 'tuner-set', 'looper-button', 'looper-state']),
          'bpm': integer(20, 250), 'enabled': BOOL, 'action': string(enum=['record', 'play', 'undo', 'once', 'reverse', 'half-speed'])}, ['context_artifact', 'kind']),
    tool('prepare_live_control', 'Prepare one absolute live scene, block channel or bypass change from retained current context. Optional midi_assignment sends the explicitly configured CC instead of direct SysEx. Preserves saved content; verifies unsaved state and readback. No MIDI during preparation.',
         {'context_artifact': ID, 'kind': string(enum=['scene', 'channel', 'bypass']), 'effect_id': integer(1, 1000),
          'channel': string(enum=['A', 'B', 'C', 'D']), 'bypassed': BOOL, 'scene': integer(1, 8), 'midi_assignment': MIDI_CC}, ['context_artifact', 'kind']),
    tool('query_edit_capabilities', 'Read model-specific editing and routing support; execution availability is separate from physical qualification.',
         {'model': MODEL, 'capacity': integer(512, 1024), 'firmware': FIRMWARE}),
    tool('query_parameters', 'Read saved semantic field keys, units, ranges and model-specific audio catalogue for editing.',
         {'model': MODEL, 'capacity': integer(512, 1024), 'firmware': FIRMWARE, 'family': string()}, ['model', 'capacity', 'firmware']),
    tool('plan_preset_edit', 'Plan a compound saved-value and connected grid edit offline. Names, scene assignments, mapped/capture-calibrated block and modifier values, Scene Ignore, templates and grid edges share one reviewed result. New blocks require a same-model/firmware template artifact. No MIDI.',
         {'backup_artifact': ID, 'slot': integer(0, 1023), 'persistence': string(enum=['save', 'edit-buffer']), 'operations': array(EDIT_OPERATION, 256),
          'feedback_policy': string(enum=['preserve', 'allow'])}, ['backup_artifact', 'slot', 'persistence', 'operations']),
    tool('plan_selective_scene_midi_undo', 'Plan selective undo of completed saved Scene MIDI message edits against a caller-supplied current saved capture. Reverts exact receipt-owned raw rows and preserves later unrelated settings. Conflicting owned rows are refused. No MIDI.',
         {'execution_artifact': ID, 'item_index': integer(0, 31), 'backup_artifact': ID, 'slot': integer(0, 1023)},
         ['execution_artifact', 'backup_artifact', 'slot']),
    tool('plan_selective_recovery_undo', 'Plan selective undo of one completed older Scene MIDI recovery-store item against a caller-supplied current saved capture. Uses exact original row words, preserves unrelated later settings and refuses conflicting owned rows. The retained recovery state is read-only; normal preset-edit review and fresh execution guards apply. No MIDI.',
         {'recovery_artifact': ID, 'state_sha256': HASH, 'backup_artifact': ID, 'slot': integer(0, 1023)},
         ['recovery_artifact', 'state_sha256', 'backup_artifact', 'slot']),
    tool('prepare_preset_edit', 'Freeze and revalidate an authored preset bundle into an exact execution review. Complete image verified before one optional Save and Reload. Sends no MIDI.', {'plan_artifact': ID}),
    tool('prepare_preset_edit_undo', 'Prepare exact original-image undo from a verified saved edit receipt. Optional item_index selects a completed batch item. Refuses later saved/unsaved changes at execution; one fresh operator review is required.', {'execution_artifact': ID, 'item_index': integer(0, 31)}, ['execution_artifact']),
    tool('prepare_edit_reconciliation', 'Prepare a query-only comparison of fresh saved/loaded images with the reviewed original/result. Never repeats an upload or Save.', {'plan_artifact': ID}),
    tool('status', 'Show versions, configured access, general operation scope and links to model/firmware evidence without opening MIDI. Distinguish implemented operations from hardware qualification.', {}),
    tool('search_knowledge', 'Find reusable capabilities, procedures and retained source locators.', {'query': string(), 'limit': integer(1, 20)}, ['query']),
    tool('query_block', 'Look up grid block IDs or names/abbreviations offline for FM9, FM3 or Axe-Fx III. Returns source attribution, model-qualified identities and dated instance limits. Block identity is distinct from amp/effect type IDs, exact-firmware availability, DSP capacity and editing qualification. No MIDI or network access.',
         {'model': MODEL, 'firmware': FIRMWARE, 'query': string(), 'effect_id': integer(0, 65535)}, ['model']),
    tool('query_amp', 'Search qualified amp attribution. The contained catalogue is FM9 12.00 only; other device tasks support all models.',
         {'model': MODEL, 'firmware': FIRMWARE, 'query': string(), 'model_id': integer(0, 335)}, ['model', 'firmware']),
    tool('read_artifact', 'Read a bounded page of a caller-private text artifact. Private files never appear in public resources.',
         {'artifact_id': ID, 'file': string(), 'offset': integer(0, 16000000), 'length': integer(1, 100000)}, ['artifact_id', 'file']),
    tool('list_artifacts', 'Find private retained artifacts after reconnecting. Returns IDs/file names, never preset bytes.',
         {'offset': integer(0, 1000000), 'limit': integer(1, 50)}, []),
    tool('inspect_attempt', 'Poll an accepted execution job; optionally wait up to 20 seconds. Returns verified result or exact recovery identifiers after interruption. Never repeat a consumed approval.', {'review_id': ID, 'wait_seconds': integer(0, 20)}, ['review_id']),
    tool('cancel_attempt', 'Request stopping an active execution job only when the user asks to stop. Stopping hardware work is uncertain until readback; never retries or undoes. Server shutdown also stops owned jobs.', {'review_id': ID}, read=False),
    tool('list_ports', 'Enumerate local MIDI ports only when the operator enables live reads.', {}, local=False),
    tool('inspect_device', 'Read configured device identity/current selection and live scene names; never navigate.', {}, local=False),
    tool('capture_presets', 'Query explicit zero-based saved slots with full backup manifests; those slots need not be selected. Live MIDI queries, no navigation or edits. Saved grid/amp/scene data is not live edit-buffer state; use explicit current-state fields for selection.', {'slots': SLOTS}, local=False),
    tool('capture_inventory', 'Start a resumable saved inventory. Specify slots or all=true; every capture is checkpointed. Sends queries only, never navigates or edits.',
         {'slots': SLOTS, 'all': BOOL}, [], local=False),
    tool('resume_inventory', 'Resume a partial inventory after rechecking every retained saved image. Never overwrites changed originals; no automatic retry.',
         {'inventory_artifact': ID}, local=False),
    tool('inspect_inventory', 'Page retained complete or partial inventory offline, including missing slots and attempts.',
         {'inventory_artifact': ID, 'offset': integer(0, 1024), 'limit': integer(1, 25)}, ['inventory_artifact']),
    tool('export_inventory', 'Export a completed inventory into a new verified backup artifact, offline.', {'inventory_artifact': ID}),
    tool('migrate_inventory', 'Validate and copy a schema-1 inventory from a supported prior release into a new current-release artifact; preserve timestamps and original data, no fresh-read claim.', {'inventory_artifact': ID}),
    tool('compare_inventories', 'Compare retained saved contents offline; report exact equality, name changes and unique or ambiguous content correspondences. Never claims physical identity or redirects targets.',
         {'before_artifact': ID, 'after_artifact': ID}),
    tool('inventory_amp_usage', 'Find saved amp channels and their scenes, including bypass state. Absence requires complete capacity and decoding plus a model/firmware-qualified roster.',
         {'inventory_artifact': ID, 'amp_type': integer(0, 65535), 'roster_artifact': ID, 'roster_file': string(),
          'offset': integer(0, 8192), 'limit': integer(1, 100)}, ['inventory_artifact']),
    tool('read_scene_midi', 'Query the current saved preset and compare its independent live Scene MIDI table; never select a scene. Equality applies only to that table, not live amp/grid state. MIDI queries do not prove configured outgoing PC/CC delivery.', {}, local=False),
    tool('capture_load_context', 'Read and retain current saved/loaded and target bytes for a later explicit load review. Optional midi_mapping binds configured bank/PC recall; sends queries only, no Load.',
         {'target_slot': integer(0, 1023), 'discard_changes': BOOL, 'midi_mapping': MIDI_MAPPING}, ['target_slot', 'discard_changes'], local=False),
    tool('check_backup', 'Check complete single-preset integrity offline. An opaque header does not qualify a restore destination.',
         {'artifact_id': ID, 'file': string(), 'sha256': HASH, 'model': MODEL, 'capacity': integer(512, 1024)}),
    tool('decode_captures', 'Decode addressed captures into a normalized snapshot and immutable backup export, offline.',
         {'artifact_id': ID, 'files': array(string()), 'model': MODEL, 'capacity': integer(512, 1024), 'firmware': FIRMWARE}),
    tool('plan_scene_midi', 'Plan PC/CC messages across explicit slots/scenes offline. If insertion is needed, execute a separate general block-insertion review, then capture fresh backups and replan the messages.',
         {'snapshot_artifact': ID, 'slots': SLOTS, 'scenes': array(integer(1, 8), 8), 'message_slot': integer(1, 8),
          'type': string(enum=['PC', 'CC']), 'channel': integer(1, 16), 'value': integer(0, 127), 'controller': integer(0, 127),
          'block_row': integer(1, 6), 'block_column': integer(1, 14), 'placement_policy': string(enum=['first-isolated-empty'])},
         ['snapshot_artifact', 'slots', 'scenes', 'message_slot', 'type', 'channel', 'value']),
    tool('plan_block_insertion', 'Plan a caller-resolved block instance at explicit coordinates or with the explicit first-isolated-empty policy. Reuses existing instances; preserves routing. Requires model/firmware instance evidence.',
         {'snapshot_artifact': ID, 'slots': SLOTS, 'effect_id': integer(1, 65535), 'row': integer(1, 6), 'column': integer(1, 14),
          'placement_policy': string(enum=['first-isolated-empty'])}, ['snapshot_artifact', 'slots', 'effect_id']),
    tool('plan_block_removal', 'Plan removal of an exact isolated block instance of any family from explicit presets, offline. Connected or uncertain routing is refused; absent instances need no edit.',
         {'snapshot_artifact': ID, 'slots': SLOTS, 'effect_id': integer(1, 1000)}),
    tool('prepare_scene_midi', 'Validate/freeze an existing-block plan and backup bytes into an exact review; sends no MIDI.',
         {'plan_artifact': ID, 'backup_artifact': ID}),
    tool('prepare_insertion', 'Validate/freeze chosen-cell insertion and original backups into an exact review; sends no MIDI.',
         {'plan_artifact': ID, 'backup_artifact': ID}),
    tool('prepare_removal', 'Validate/freeze an isolated block-removal plan and complete backups into an exact review; sends no MIDI. Preserves every other block, routing, names, Scene MIDI and saved scene.',
         {'plan_artifact': ID, 'backup_artifact': ID}),
    tool('prepare_insertion_undo', 'Prepare removal of blocks added by a verified insertion execution. Binds the exact inserted baseline and original backup; requires byte-identical original contents before Save. Later changes are refused. Sends no MIDI and requires a fresh exact operator approval.',
         {'execution_artifact': ID}),
    tool('prepare_load', 'Validate/freeze retained load context into a review, including explicit discard choice; sends no MIDI.', {'context_artifact': ID}),
    tool('inspect_recovery', 'Inspect a retained current recovery head offline. No device state is inferred from this import.',
         {'recovery_artifact': ID, 'state_sha256': HASH}),
    tool('migrate_recovery', 'Validate and copy a compatible prior-release Scene MIDI recovery record into a new current-release artifact. Settled and never-attempted pending items are supported; interrupted items are refused. No MIDI or fresh device-state claim.',
         {'recovery_artifact': ID, 'state_sha256': HASH}),
    tool('prepare_recovery', 'Validate/freeze an explicit resume or whole-backup undo against the retained exact recovery head; no MIDI.',
         {'recovery_artifact': ID, 'state_sha256': HASH, 'mode': string(enum=['resume', 'undo'])}),
    tool('execute_review', 'Start one exact operator-approved execution job and return promptly. Poll fractal_inspect_attempt with wait_seconds 20 until verified-complete or stopped-uncertain; acceptance is not completion. Server shutdown stops owned jobs. The model cannot approve itself. Never retries or rolls back.',
         {'review_id': ID, 'review_sha256': HASH}, read=False, local=False),
]
TOOL_MAP = {t['name']: t for t in TOOLS}
TOOL_MAP['fractal_plan_setlists']['annotations'].update(readOnlyHint=False, destructiveHint=False, idempotentHint=False)
for definition in TOOLS:
    if definition['name'] in {'fractal_plan_preset_organization', 'fractal_prepare_preset_organization', 'fractal_prepare_organization_reconciliation', 'fractal_plan_preset_organization_undo', 'fractal_capture_performance_context', 'fractal_prepare_performance_control', 'fractal_calibrate_saved_parameter', 'fractal_read_block_parameters', 'fractal_monitor_live_state', 'fractal_read_amp_roster', 'fractal_capture_live_control_context', 'fractal_prepare_live_control', 'fractal_capture_presets', 'fractal_read_scene_midi', 'fractal_capture_load_context', 'fractal_decode_captures',
                              'fractal_plan_scene_midi', 'fractal_plan_block_insertion', 'fractal_prepare_scene_midi', 'fractal_prepare_insertion',
                              'fractal_prepare_load', 'fractal_prepare_recovery', 'fractal_plan_block_removal', 'fractal_prepare_removal', 'fractal_prepare_insertion_undo',
                              'fractal_plan_preset_edit', 'fractal_plan_selective_scene_midi_undo', 'fractal_plan_selective_recovery_undo', 'fractal_prepare_preset_edit', 'fractal_prepare_preset_edit_undo', 'fractal_prepare_edit_reconciliation',
                              'fractal_capture_inventory', 'fractal_resume_inventory', 'fractal_export_inventory', 'fractal_migrate_inventory', 'fractal_migrate_recovery',
                              'fractal_prepare_edit_batch', 'fractal_prepare_batch_reconciliation', 'fractal_route_midi_events', 'fractal_plan_audio_path', 'fractal_listen_controller', 'fractal_listen_device_notifications', 'fractal_import_template', 'fractal_plan_template'}:
        definition['annotations'].update(readOnlyHint=False, destructiveHint=False, idempotentHint=False)


class Toolkit:
    def __init__(self, repository, config, runtime=None):
        self.repository = Path(repository).resolve()
        self.config = config
        self.artifacts = Artifacts(config['data_root'])
        self.runtime = runtime or Runtime(config)
        self.execution_jobs = {}
        self.execution_lock = threading.Lock()
        self.version = self._json_public('docs/protocol-release.json')['release']
        self.resources = {}
        for parent in ('docs', 'knowledge-base', 'skills/fractal-fm9/references'):
            for file in sorted((self.repository / parent).rglob('*')):
                if file.suffix in ('.md', '.json') and file.is_file() and file.stat().st_size <= 256000:
                    relative = file.relative_to(self.repository).as_posix()
                    self.resources['fractal://reference/' + relative] = relative

    def _json_public(self, relative):
        from .storage import loads
        return loads(confined(self.repository, relative).read_text(encoding='utf-8'))

    def _module(self, relative):
        spec = importlib.util.spec_from_file_location('fractal_contained_' + Path(relative).stem, self.repository / relative)
        module = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(module)
        return module

    def list_resources(self):
        return [{'uri': uri, 'name': relative, 'mimeType': 'application/json' if relative.endswith('.json') else 'text/markdown'}
                for uri, relative in self.resources.items()]

    def read_resource(self, uri):
        if uri not in self.resources:
            raise ValueError('Unknown public reference URI.')
        relative = self.resources[uri]
        file = confined(self.repository, relative)
        if file.stat().st_size > 256000:
            raise ValueError('Reference exceeds its bound.')
        return {'contents': [{'uri': uri, 'mimeType': 'application/json' if relative.endswith('.json') else 'text/markdown', 'text': file.read_text(encoding='utf-8')}]}

    def device(self):
        if 'device' not in self.config:
            raise ValueError('An exact operator device configuration is needed for execution reviews. Offline planning needs none.')
        return self.config['device']

    def live(self):
        if not self.config['allow_live_reads']:
            raise ValueError('Live device access is disabled by the operator configuration.')
        return self.device()

    def device_args(self, ports=False):
        d = self.device()
        args = ['--model', d['model'], '--capacity', d['capacity']]
        if ports:
            args += self.connection_args()
        return args

    def connection_args(self, input_only=False):
        d = self.device()
        if 'network' in d:
            return ['--network', d['network']]
        return ['--input', d['input']] + ([] if input_only else ['--output', d['output']])

    def export(self, identifier):
        # Every planner input is the original complete export, not a model-created normalized snapshot.
        folder = self.artifacts.directory(identifier)
        manifest = self.artifacts.json(identifier, 'export/manifest.json')
        snapshot_path = self.artifacts.file(identifier, 'export/' + manifest['snapshot_file'])
        if sha(snapshot_path) != manifest['snapshot_sha256']:
            raise ValueError('Snapshot manifest hash differs.')
        for entry in manifest['captures']:
            path = self.artifacts.file(identifier, 'export/' + entry['file'])
            if sha(path) != entry['sysex_sha256']:
                raise ValueError('Capture manifest hash differs.')
        return folder / 'export', self.artifacts.json(identifier, 'export/' + manifest['snapshot_file'])

    def match_context(self, snapshot):
        d = self.device()
        actual = snapshot['device']
        if (actual['model'], actual['firmware'], actual['preset_capacity']) != (d['model'], d['firmware'], d['capacity']):
            raise ValueError('Backup context differs from the configured model/capacity/firmware.')

    def call(self, name, arguments, cancel=None):
        if name not in TOOL_MAP:
            raise KeyError('Unknown tool.')
        validate(TOOL_MAP[name]['inputSchema'], arguments)
        cancel = cancel or threading.Event()
        if cancel.is_set():
            from .runtime import Cancelled
            raise Cancelled('Cancelled before tool execution.')
        a = arguments
        if name in ('fractal_read_block_parameters', 'fractal_read_amp_roster', 'fractal_capture_live_control_context', 'fractal_capture_performance_context', 'fractal_monitor_live_state'):
            device = self.live()
            if name == 'fractal_read_amp_roster' and a['start'] + a['count'] > 16384:
                raise ValueError('Roster range exceeds ordinal field.')
            identifier, folder = self.artifacts.create()
            command_name, subfolder = {'fractal_read_block_parameters': ('read-block-parameters', 'parameters'), 'fractal_read_amp_roster': ('amp-roster', 'roster'), 'fractal_capture_live_control_context': ('capture-control', 'context'),
                                       'fractal_capture_performance_context': ('capture-performance', 'performance'),
                                       'fractal_monitor_live_state': ('live-monitor', 'monitor')}[name]
            command = [command_name, *self.device_args(True), '--firmware', device['firmware'], '--destination', folder / subfolder]
            if name == 'fractal_read_block_parameters':
                command += ['--effect-id', a['effect_id']]
            if name == 'fractal_read_amp_roster':
                command += ['--start', a['start'], '--count', a['count']]
            if name == 'fractal_monitor_live_state':
                command += ['--samples', a['samples'], '--interval-ms', a['interval_ms']]
            try:
                with device_owner(device):
                    result = self.runtime.run('cli', command, cancel, log=folder / 'runtime.json')
            except Exception as error:
                raise ValueError(f'Read stopped; retained evidence artifact {identifier}. {error}') from error
            return {'artifact_id': identifier, 'result': result, 'read_interpretation': READ_INTERPRETATION.copy()}
        if name == 'fractal_status':
            return {'release': self.version, 'transport': 'local-stdio', 'live_reads_enabled': self.config['allow_live_reads'],
                    'writes_enabled': self.config['allow_writes'], 'device': self.config.get('device'),
                    'approval': 'one exact local operator approval per execution review', 'protocol_versions': ['2025-11-25', '2025-06-18', '2025-03-26'],
                    'capabilities': {
                        'network_transport': 'Explicit Apple RTP-MIDI endpoint uses the same typed scopes and exact reviews as local ports. Loss/disconnect stops the session; no automatic replay. See fractal://reference/docs/network-notifications.md.',
                        'device_notifications': 'Bounded passive capture decodes published tempo/tuner pushes and known state reply formats; unknown frames remain visible. No saved-state or complete event coverage claim.',
                        'batch_editing': 'Ordered saved-edit batches with per-target verification, durable checkpoints, query-only reconciliation and completed-item undo. See fractal://reference/docs/batch-editing.md.',
                        'configured_midi': 'Explicit bank/PC recall, assigned CC scene/channel/bypass execution and host-fed note/CC routing. See fractal://reference/docs/configured-midi-routing.md.',
                        'automatic_audio_paths': 'Forward grid paths through explicit occupied waypoints produce ordinary executable edit bundles. Existing blocks/cables preserved; sharing and feedback remain explicit. See fractal://reference/docs/automatic-audio-paths.md.',
                        'controller_listener': 'Bounded standalone WinMM input-only listener and MCP event capture feed the shared note/CC router; no automatic writes. See fractal://reference/docs/controller-listener.md.',
                        'catalog': 'fractal://reference/knowledge-base/catalog/capabilities.json',
                        'live_tools': 'Bounded amp roster, sampled selection/block-state monitor and capture-bound absolute scene/channel/bypass controls are implemented across all model definitions; hardware qualification is separate. See fractal://reference/docs/live-tools.md.',
                        'performance_controls': 'Capture-bound tempo read/set, single tap, tuner on/off, looper buttons and absolute play/reverse/half-speed use exact reviews with no Save/replay. Tempo queries require review; tuner/events remain sent-unverified. FM3/FM9 command adaptations and hardware behavior remain unqualified. See fractal://reference/docs/performance-controls.md.',
                        'block_catalogue': 'fractal://reference/knowledge-base/catalog/block-identities.json',
                        'block_insertion': 'Implemented: add a caller-resolved block instance at a chosen isolated cell; preserve existing preset content. Instance availability and hardware qualification depend on model, firmware and block family.',
                        'block_removal': 'Implemented: remove an exact isolated block instance of any family; refuse connected or uncertain routing. Insertion undo uses removal and requires complete original-byte equality. Hardware qualification depends on model, firmware and block family.',
                        'parameter_editing': 'Mapped and exact capture-backed saved block/modifier values, witnessed Scene Ignore, modifier sources/templates, Scene MIDI, names and assignments share the common planner and verifier; physical calibration remains separate.',
                        'template_library': 'Private named block/channel donors, offline search/inspection/application and baseline-bound swaps with explicit scene-reference policy. See fractal://reference/docs/template-library.md. Physical qualification remains separate.',
                        'preset_organization': 'Immutable saved preset rename/copy/import, explicit replacement-based move, swap and closed reordering; guarded ordinary batch reviews, query-only reconciliation and completed-target undo. External references/assets remain unchanged; no hardware relocation qualification. See fractal://reference/docs/preset-organization.md.',
                        'combined_operations': 'General parameter, scene, Scene MIDI, template insertion/removal and connected grid operations compose into one reviewed temporary upload and optional Save/Reload. Legacy Scene MIDI insertion/configuration uses separate reviews.'},
                    'hardware_evidence': 'fractal://reference/docs/device-support.md', 'ports_opened': False}
        if name == 'fractal_search_knowledge':
            result = self._module('scripts/search_knowledge.py').search(a['query'], a.get('limit', 5))
            result['read_interpretation'] = READ_INTERPRETATION.copy()
            return result
        if name == 'fractal_listen_device_notifications':
            device = self.live()
            identifier, folder = self.artifacts.create()
            args = ['listen-notifications', *self.device_args(), '--firmware', device['firmware'], '--duration', a['duration_seconds'], '--max-events', a['max_events']]
            self.runtime.run('cli', [*args, '--validate-only', 'true'], cancel, maximum_seconds=30)
            try:
                with device_owner(device):
                    result = self.runtime.run('cli', [*args, *self.connection_args(input_only=True), '--destination', folder / 'notifications'], cancel, maximum_seconds=a['duration_seconds'] + 30, log=folder / 'runtime.json')
            except Exception as error:
                raise ValueError(f'Notification capture stopped; partial evidence artifact {identifier}. {error}') from error
            return {'artifact_id': identifier, 'result': result, 'read_interpretation': READ_INTERPRETATION.copy()}
        if name == 'fractal_listen_controller':
            device = self.live()
            identifier, folder = self.artifacts.create()
            request = {'schema_version': 1, 'model': device['model'], 'capacity': device['capacity'], 'firmware': device['firmware'], 'rules': a['rules']}
            atomic_json(folder / 'request.json', request)
            args = ['listen-midi', '--request', folder / 'request.json', '--duration', a['duration_seconds'], '--max-events', a['max_events']]
            self.runtime.run('cli', [*args, '--validate-only', 'true'], cancel, maximum_seconds=30)
            try:
                result = self.runtime.run('cli', [*args, '--input', a['input'], '--destination', folder / 'listener'], cancel, maximum_seconds=a['duration_seconds'] + 30, log=folder / 'runtime.json')
            except Exception as error:
                raise ValueError(f'Controller listener stopped; partial evidence artifact {identifier}. {error}') from error
            return {'artifact_id': identifier, 'result': result}
        if name == 'fractal_route_midi_events':
            identifier, folder = self.artifacts.create()
            atomic_json(folder / 'request.json', {'schema_version': 1, **a})
            result = self.runtime.run('cli', ['route-midi', '--request', folder / 'request.json'], cancel, log=folder / 'routing.json')
            return {'artifact_id': identifier, 'result': result}
        if name == 'fractal_plan_setlists':
            inventory = confined(self.artifacts.directory(a['inventory_artifact']), 'inventory')
            retained = self.runtime.run('cli', ['inventory-inspect', '--inventory', inventory, '--limit', 1], cancel)
            if 'device' in self.config:
                self.match_context({'device': {'model': retained['model'], 'preset_capacity': retained['capacity'], 'firmware': retained['firmware']}})
            identifier, folder = self.artifacts.create()
            request = {'schema_version': 1, 'model': retained['model'], 'capacity': retained['capacity'], 'firmware': retained['firmware'],
                       'songs': a['songs'], 'setlists': a['setlists']}
            for field in ('active_setlist', 'fc_controller_present'):
                if field in a: request[field] = a[field]
            atomic_json(folder / 'request.json', request)
            result = self.runtime.run('cli', ['plan-setlists', '--request', folder / 'request.json', '--inventory', inventory,
                                              '--destination', folder / 'setlists'], cancel)
            return {'artifact_id': identifier, 'result': result, 'ports_opened': False}
        if name in ('fractal_capture_inventory', 'fractal_resume_inventory'):
            device = self.live()
            if name == 'fractal_capture_inventory':
                if ('slots' in a) == (a.get('all') is True):
                    raise ValueError('Choose explicit slots or all=true.')
                if 'slots' in a and max(a['slots']) >= device['capacity']:
                    raise ValueError('Requested slot exceeds configured capacity.')
                identifier, folder = self.artifacts.create()
                args = ['inventory', *self.device_args(True), '--firmware', device['firmware'], '--destination', folder / 'inventory']
                if a.get('all'):
                    args += ['--all']
                else:
                    for slot in a['slots']:
                        args += ['--preset', slot]
            else:
                identifier = a['inventory_artifact']; folder = self.artifacts.directory(identifier)
                inventory = confined(folder, 'inventory')
                prior = self.runtime.run('cli', ['inventory-inspect', '--inventory', inventory, '--limit', 1], cancel)
                if (prior['model'], prior['capacity'], prior['firmware']) != (device['model'], device['capacity'], device['firmware']):
                    raise ValueError('Inventory context differs from configured device.')
                args = ['inventory', '--resume', inventory, *self.connection_args()]
            with device_owner(device):
                try:
                    result = self.runtime.run('cli', args, cancel)
                except Exception as error:
                    raise ValueError(f'Inventory stopped; inspect retained inventory artifact {identifier}. {error}') from error
            return {'artifact_id': identifier, 'result': result, 'read_interpretation': READ_INTERPRETATION.copy()}
        if name in ('fractal_inspect_inventory', 'fractal_export_inventory', 'fractal_migrate_inventory', 'fractal_compare_inventories', 'fractal_inventory_amp_usage'):
            def inventory(identifier):
                return confined(self.artifacts.directory(identifier), 'inventory')
            if name == 'fractal_compare_inventories':
                args = ['inventory-compare', '--before', inventory(a['before_artifact']), '--after', inventory(a['after_artifact'])]
            elif name in ('fractal_export_inventory', 'fractal_migrate_inventory'):
                identifier, folder = self.artifacts.create()
                migrating = name == 'fractal_migrate_inventory'
                result = self.runtime.run('cli', ['inventory-migrate' if migrating else 'inventory-export', '--inventory', inventory(a['inventory_artifact']),
                                                  '--destination', folder / ('inventory' if migrating else 'export')], cancel)
                return {'artifact_id': identifier, 'result': result, 'ports_opened': False}
            else:
                args = ['inventory-inspect' if name == 'fractal_inspect_inventory' else 'inventory-amp-usage', '--inventory', inventory(a['inventory_artifact'])]
                args += ['--offset', a.get('offset', 0), '--limit', a.get('limit', 10 if name == 'fractal_inspect_inventory' else 100)]
                if 'amp_type' in a:
                    args += ['--amp-type', a['amp_type']]
                if 'roster_artifact' in a or 'roster_file' in a:
                    if not {'roster_artifact', 'roster_file'} <= a.keys():
                        raise ValueError('Supply both roster artifact and file.')
                    args += ['--roster', self.artifacts.file(a['roster_artifact'], a['roster_file'])]
            return self.runtime.run('cli', args, cancel)
        if name in ('fractal_import_template', 'fractal_list_templates', 'fractal_inspect_template', 'fractal_plan_template'):
            library = confined(self.artifacts.root, 'templates', exists=False)
            if name == 'fractal_list_templates':
                if not library.exists():
                    return {'entries': [], 'total': 0, 'next_offset': None, 'ports_opened': False}
                args = ['template-list', '--library', library, '--offset', a.get('offset', 0), '--limit', a.get('limit', 20)]
                for field in ('query', 'tag'):
                    if field in a: args += ['--' + field, a[field]]
                return self.runtime.run('cli', args, cancel, maximum_seconds=60)
            if name == 'fractal_inspect_template':
                return self.runtime.run('cli', ['template-inspect', '--library', library, '--entry', a['entry_id'],
                    '--expected-hash', a['entry_sha256']], cancel, maximum_seconds=30)
            _, snapshot = self.export(a['backup_artifact']); self.match_context(snapshot)
            entry = next((e for e in self.artifacts.json(a['backup_artifact'], 'export/manifest.json')['captures'] if e['slot'] == a['slot']), None)
            if entry is None: raise ValueError('Template source/target slot is absent from the retained backup.')
            wire = self.artifacts.file(a['backup_artifact'], 'export/' + entry['file'])
            if sha(wire) != entry['sysex_sha256']: raise ValueError('Template source/target differs from the backup manifest.')
            identifier, folder = self.artifacts.create(); device = snapshot['device']
            request = {'schema_version': 1, 'model': device['model'], 'capacity': device['preset_capacity'], 'firmware': device['firmware'],
                       'effect_id': a['effect_id'], 'kind': a['kind']}
            if name == 'fractal_import_template':
                request.update(name=a['name'], tags=a.get('tags', []), provenance=a['provenance'], capture_sha256=entry['sysex_sha256'])
                if 'channel' in a: request['channel'] = a['channel']
                atomic_json(folder / 'request.json', request)
                result = self.runtime.run('cli', ['template-import', '--library', library, '--request', folder / 'request.json', '--capture', wire], cancel)
                return {'artifact_id': identifier, **result, 'ports_opened': False}
            source = confined(library, a['entry_id'] + '.json')
            request.update(slot=a['slot'], persistence=a['persistence'], base_sha256=entry['sysex_sha256'])
            for field in ('channel', 'row', 'column', 'policy'):
                if field in a: request[field] = a[field]
            atomic_json(folder / 'request.json', request)
            result = self.runtime.run('cli', ['plan-template', '--entry-file', source, '--entry-hash', a['entry_sha256'],
                '--request', folder / 'request.json', '--capture', wire, '--destination', folder / 'bundle'], cancel, maximum_seconds=60)
            # Capture provenance for discovery; execution authority remains the frozen ordinary bundle.
            atomic_json(folder / 'template-source.json', {'entry_id': a['entry_id'], 'entry_sha256': a['entry_sha256']})
            return {'artifact_id': identifier, **result, 'ports_opened': False}
        if name == 'fractal_inspect_preset_organization':
            bundle = confined(self.artifacts.directory(a['organization_artifact']), 'organization')
            return self.runtime.run('cli', ['inspect-organization', '--bundle', bundle, '--expected-hash', sha(bundle / 'plan.json')], cancel, maximum_seconds=60)
        if name in ('fractal_plan_preset_organization', 'fractal_plan_preset_organization_undo'):
            _, snapshot = self.export(a['backup_artifact']); self.match_context(snapshot)
            device = snapshot['device']; operations = []; slots = set()
            if name == 'fractal_plan_preset_organization':
                for raw in a['operations']:
                    operation = dict(raw)
                    for field in ('slot', 'source_slot', 'destination_slot', 'replacement_slot'):
                        if field in operation: slots.add(operation[field])
                    for assignment in operation.get('assignments', []):
                        slots.update((assignment['source_slot'], assignment['destination_slot']))
                    if operation['kind'] == 'import':
                        keys = ('donor_artifact', 'donor_file', 'donor_sha256', 'donor_model', 'donor_capacity', 'donor_firmware')
                        if any(key not in operation for key in keys): raise ValueError('Import requires exact donor artifact/file/hash and declared model/capacity/firmware.')
                        file = self.artifacts.file(operation['donor_artifact'], operation['donor_file'])
                        if file.stat().st_size > 60000: raise ValueError('Individual preset donor exceeds bound.')
                        wire = file.read_bytes()
                        if hashlib.sha256(wire).hexdigest() != operation['donor_sha256']: raise ValueError('Import donor hash differs.')
                        operation['donor'] = {'model': operation['donor_model'], 'capacity': operation['donor_capacity'], 'firmware': operation['donor_firmware'],
                                              'sha256': operation['donor_sha256'], 'capture': base64.b64encode(wire).decode('ascii')}
                        for key in keys: operation.pop(key)
                    operations.append(operation)
            else:
                progress = self.artifacts.json(a['execution_artifact'], 'execution/progress.json')
                slots = {row['slot'] for row in progress['completed'] if not row.get('unchanged', False)}
            if not slots or len(slots) > 64: raise ValueError('Organization needs 1–64 explicit captured slots.')
            manifest = self.artifacts.json(a['backup_artifact'], 'export/manifest.json')
            captures = []
            for slot in sorted(slots):
                entry = next((e for e in manifest['captures'] if e['slot'] == slot), None)
                if entry is None: raise ValueError('Required organization slot is absent from the backup artifact.')
                wire = self.artifacts.file(a['backup_artifact'], 'export/' + entry['file']).read_bytes()
                if hashlib.sha256(wire).hexdigest() != entry['sysex_sha256']: raise ValueError('Organization capture differs from its manifest.')
                captures.append({'slot': slot, 'sha256': entry['sysex_sha256'], 'capture': base64.b64encode(wire).decode('ascii')})
            identifier, folder = self.artifacts.create()
            request = {'schema_version': 1, 'model': device['model'], 'capacity': device['preset_capacity'], 'firmware': device['firmware'], 'captures': captures}
            if name == 'fractal_plan_preset_organization': request['operations'] = operations
            atomic_json(folder / 'request.json', request)
            args = ['plan-organization', '--request', folder / 'request.json', '--destination', folder / 'organization']
            if name == 'fractal_plan_preset_organization_undo':
                args = ['plan-organization-undo', '--receipt', confined(self.artifacts.directory(a['execution_artifact']), 'execution'),
                        '--expected-hash', a['progress_sha256'], '--request', folder / 'request.json', '--destination', folder / 'organization']
            result = self.runtime.run('cli', args, cancel, maximum_seconds=60, log=folder / 'runtime.json')
            return {'artifact_id': identifier, **result, 'ports_opened': False}
        if name in ('fractal_query_edit_capabilities', 'fractal_query_parameters', 'fractal_query_performance_capabilities'):
            args = [{'fractal_query_edit_capabilities': 'edit-capabilities', 'fractal_query_parameters': 'edit-catalogue', 'fractal_query_performance_capabilities': 'performance-capabilities'}[name],
                    '--model', a['model'], '--capacity', a['capacity'], '--firmware', a['firmware']]
            if 'family' in a:
                args += ['--family', a['family']]
            return self.runtime.run('cli', args, cancel, maximum_seconds=30)
        if name == 'fractal_calibrate_saved_parameter':
            witnesses = []
            for sample in a['samples']:
                _, snapshot = self.export(sample['backup_artifact']); self.match_context(snapshot)
                entry = next((e for e in self.artifacts.json(sample['backup_artifact'], 'export/manifest.json')['captures'] if e['slot'] == sample['slot']), None)
                if entry is None:
                    raise ValueError('Calibration sample slot is absent.')
                wire = self.artifacts.file(sample['backup_artifact'], 'export/' + entry['file']).read_bytes()
                if hashlib.sha256(wire).hexdigest() != entry['sysex_sha256']:
                    raise ValueError('Calibration sample differs from its manifest.')
                witnesses.append({'value': sample['value'], 'sha256': entry['sysex_sha256'], 'capture': base64.b64encode(wire).decode('ascii')})
            device = self.device()
            request = {'schema_version': 1, 'model': device['model'], 'capacity': device['capacity'], 'firmware': device['firmware'],
                       'effect_id': a['effect_id'], 'channel': a.get('channel'), 'key': a['key'], 'unit': a['unit'], 'kind': a['kind'],
                       'modifier_slot': a.get('modifier_slot'), 'expected_binding': a.get('expected_binding'), 'samples': witnesses}
            identifier, folder = self.artifacts.create(); atomic_json(folder / 'request.json', request)
            result = self.runtime.run('cli', ['calibrate-parameter', '--request', folder / 'request.json', '--destination', folder / 'calibration'], cancel, log=folder / 'runtime.json')
            return {'artifact_id': identifier, 'result': result, 'ports_opened': False}
        if name in ('fractal_plan_preset_edit', 'fractal_plan_audio_path'):
            backup, snapshot = self.export(a['backup_artifact'])
            self.match_context(snapshot)
            entry = next((e for e in self.artifacts.json(a['backup_artifact'], 'export/manifest.json')['captures'] if e['slot'] == a['slot']), None)
            if entry is None:
                raise ValueError('Requested slot is absent from the retained backup.')
            operations = []
            for operation in a.get('operations', []):
                operation = dict(operation)
                if 'calibration_artifact' in operation:
                    if operation['kind'] not in ('set-calibrated-parameter', 'set-calibrated-modifier', 'set-scene-ignore'):
                        raise ValueError('Calibration applies only to calibrated field, modifier or Scene Ignore operations.')
                    operation['calibration'] = self.artifacts.json(operation.pop('calibration_artifact'), 'calibration/calibration.json')
                if 'template_artifact' in operation or 'template_slot' in operation:
                    if operation['kind'] not in ('insert-block', 'copy-channel', 'copy-block-settings', 'copy-modifier', 'copy-modifier-selector') or not {'template_artifact', 'template_slot'} <= operation.keys():
                        raise ValueError('Insertion templates need both artifact and slot.')
                    template_id = operation.pop('template_artifact'); template_slot = operation.pop('template_slot')
                    _, template_snapshot = self.export(template_id); self.match_context(template_snapshot)
                    donor = next((e for e in self.artifacts.json(template_id, 'export/manifest.json')['captures'] if e['slot'] == template_slot), None)
                    if donor is None:
                        raise ValueError('Template slot is absent from its capture artifact.')
                    wire = self.artifacts.file(template_id, 'export/' + donor['file']).read_bytes()
                    operation.update(template_capture=base64.b64encode(wire).decode('ascii'), template_sha256=hashlib.sha256(wire).hexdigest(),
                                     template_firmware=template_snapshot['device']['firmware'])
                operations.append(operation)
            identifier, folder = self.artifacts.create(); device = snapshot['device']
            request = {'schema_version': 1, 'model': device['model'], 'capacity': device['preset_capacity'], 'firmware': device['firmware'],
                       'slot': a['slot'], 'persistence': a['persistence'], 'base_sha256': entry['sysex_sha256'], 'operations': operations,
                       'feedback_policy': a.get('feedback_policy', 'preserve')}
            if name == 'fractal_plan_audio_path':
                request.pop('operations')
                request.update(waypoints=a['waypoints'], shunt_id=a['shunt_id'], sharing_policy=a.get('sharing_policy', 'isolated'))
            atomic_json(folder / 'request.json', request)
            baseline = self.artifacts.file(a['backup_artifact'], 'export/' + entry['file'])
            result = self.runtime.run('cli', ['plan-audio-path' if name == 'fractal_plan_audio_path' else 'plan-edit', '--request', folder / 'request.json', '--capture', baseline, '--destination', folder / 'bundle'],
                                      cancel, maximum_seconds=60)
            return {'artifact_id': identifier, **result, 'ports_opened': False}
        if name == 'fractal_plan_selective_scene_midi_undo':
            _, snapshot = self.export(a['backup_artifact']); self.match_context(snapshot)
            entry = next((e for e in self.artifacts.json(a['backup_artifact'], 'export/manifest.json')['captures'] if e['slot'] == a['slot']), None)
            if entry is None:
                raise ValueError('Requested slot is absent from the retained current saved capture.')
            receipt = 'execution' + (f"/item-{a['item_index']:03}" if 'item_index' in a else '')
            source = confined(self.artifacts.directory(a['execution_artifact']), receipt)
            attempt = confined(source, 'attempt.json')
            identifier, folder = self.artifacts.create()
            result = self.runtime.run('cli', ['plan-selective-scene-midi-undo', '--receipt', source,
                '--receipt-hash', sha(attempt), '--capture', self.artifacts.file(a['backup_artifact'], 'export/' + entry['file']),
                '--destination', folder / 'bundle'], cancel, maximum_seconds=60)
            return {'artifact_id': identifier, **result, 'ports_opened': False}
        if name == 'fractal_plan_selective_recovery_undo':
            _, snapshot = self.export(a['backup_artifact']); self.match_context(snapshot)
            entry = next((e for e in self.artifacts.json(a['backup_artifact'], 'export/manifest.json')['captures'] if e['slot'] == a['slot']), None)
            if entry is None:
                raise ValueError('Requested slot is absent from the retained current saved capture.')
            recovery = self.recovery(a['recovery_artifact'], a['state_sha256'])
            identifier, folder = self.artifacts.create()
            result = self.runtime.run('cli', ['plan-selective-recovery-undo', '--recovery', recovery,
                '--state-hash', a['state_sha256'], '--slot', a['slot'],
                '--capture', self.artifacts.file(a['backup_artifact'], 'export/' + entry['file']),
                '--destination', folder / 'bundle'], cancel, maximum_seconds=60)
            return {'artifact_id': identifier, **result, 'ports_opened': False}
        if name == 'fractal_query_block':
            if 'query' not in a and 'effect_id' not in a:
                raise ValueError('Provide a block name or effect ID.')
            args = ['blocks', '--model', a['model']]
            for key, option in [('firmware', 'firmware'), ('query', 'query'), ('effect_id', 'effect-id')]:
                if key in a:
                    args += ['--' + option, a[key]]
            return self.runtime.run('cli', args, cancel, maximum_seconds=30)
        if name == 'fractal_query_amp':
            return self._module('scripts/query_amp.py').lookup(a.get('query'), a.get('model_id'), model=a['model'], firmware=a['firmware'])
        if name == 'fractal_read_artifact':
            path = self.artifacts.file(a['artifact_id'], a['file'])
            if path.suffix not in ('.json', '.jsonl', '.md', '.txt') or path.stat().st_size > 16 * 1024 * 1024:
                raise ValueError('Only bounded text artifacts can be read through this tool.')
            text = path.read_text(encoding='utf-8'); offset = a.get('offset', 0); length = a.get('length', 20000)
            return {'artifact_id': a['artifact_id'], 'file': a['file'], 'sha256': sha(path), 'text': text[offset:offset + length],
                    'next_offset': offset + length if offset + length < len(text) else None, 'characters': len(text)}
        if name == 'fractal_list_artifacts':
            folders = sorted((p for p in (self.artifacts.root / 'artifacts').iterdir() if p.is_dir()), key=lambda p: p.stat().st_mtime, reverse=True)
            offset = a.get('offset', 0); limit = a.get('limit', 20)
            rows = []
            for folder in folders[offset:offset + limit]:
                directory = self.artifacts.directory(folder.name)
                rows.append({'artifact_id': folder.name, 'files': list(self.artifacts.tree_hashes(directory))[:100]})
            return {'artifacts': rows, 'next_offset': offset + limit if offset + limit < len(folders) else None}
        if name == 'fractal_inspect_attempt':
            return self.inspect_attempt(a['review_id'], a.get('wait_seconds', 0), cancel)
        if name == 'fractal_cancel_attempt':
            with self.execution_lock:
                job = self.execution_jobs.get(a['review_id'])
                if job is None or job['done'].is_set():
                    raise ValueError('No active execution job owned by this server; inspect retained evidence.')
                job['cancel'].set()
            return {'review_id': a['review_id'], 'stop_requested': True, 'completion_verified': False,
                    'action': 'Poll fractal_inspect_attempt; reconcile before any new mutation.'}
        if name in ('fractal_list_ports', 'fractal_inspect_device', 'fractal_capture_presets', 'fractal_read_scene_midi', 'fractal_capture_load_context'):
            device = self.live()
            with device_owner(device):
                if name == 'fractal_list_ports':
                    return self.runtime.run('cli', ['ports'], cancel)
                if name == 'fractal_inspect_device':
                    result = self.runtime.run('cli', ['inspect', *self.device_args(True)], cancel)
                    if result['device']['firmware'] != device['firmware']:
                        raise ValueError('Device firmware differs from operator configuration.')
                    return result
                identifier, folder = self.artifacts.create()
                if name == 'fractal_capture_presets':
                    if max(a['slots']) >= device['capacity']:
                        raise ValueError('Requested slot exceeds configured capacity.')
                    command = ['snapshot', *self.device_args(True), '--destination', folder / 'export']
                    for slot in a['slots']:
                        command += ['--preset', slot]
                elif name == 'fractal_capture_load_context':
                    if a['target_slot'] >= device['capacity']:
                        raise ValueError('Requested slot exceeds configured capacity.')
                    command = ['prepare-load', *self.device_args(True), '--preset', a['target_slot'], '--discard-changes', str(a['discard_changes']).lower(), '--destination', folder / 'context']
                    if 'midi_mapping' in a:
                        atomic_json(folder / 'mapping.json', a['midi_mapping'])
                        command += ['--midi-mapping', folder / 'mapping.json']
                else:
                    command = ['scene-midi', *self.device_args(True), '--destination', folder / 'export']
                try:
                    result = self.runtime.run('cli', command, cancel, log=folder / 'runtime.json')
                except Exception as error:
                    raise ValueError(f'Read stopped; retained evidence artifact {identifier}. {error}') from error
                if name == 'fractal_capture_load_context':
                    if result['firmware'] != device['firmware']:
                        raise ValueError('Device firmware differs; retained captures remain private.')
                else:
                    _, snapshot = self.export(identifier)
                    self.match_context(snapshot)
                return {'artifact_id': identifier, 'result': result, 'read_interpretation': READ_INTERPRETATION.copy()}
        if name == 'fractal_check_backup':
            path = self.artifacts.file(a['artifact_id'], a['file'])
            if sha(path) != a['sha256']:
                raise ValueError('Backup hash differs.')
            return self.runtime.run('cli', ['check-backup', '--model', a['model'], '--capacity', a['capacity'], '--capture', path], cancel)
        if name == 'fractal_decode_captures':
            identifier, folder = self.artifacts.create()
            command = ['decode', '--model', a['model'], '--capacity', a['capacity'], '--firmware', a['firmware'], '--destination', folder / 'export']
            for file in a['files']:
                command += ['--capture', self.artifacts.file(a['artifact_id'], file)]
            result = self.runtime.run('cli', command, cancel, log=folder / 'runtime.json')
            return {'artifact_id': identifier, 'result': result, 'read_interpretation': READ_INTERPRETATION.copy()}
        if name in ('fractal_plan_scene_midi', 'fractal_plan_block_insertion', 'fractal_plan_block_removal'):
            _, snapshot = self.export(a['snapshot_artifact'])
            if name == 'fractal_plan_scene_midi':
                planner = self._module('skills/fractal-fm9/scripts/plan_scene_midi.py')
                plan = planner.plan_scene_midi(snapshot, preset_slots=a['slots'], scenes=a['scenes'], message_slot=a['message_slot'],
                                              message_type=a['type'], channel=a['channel'], value=a['value'], controller=a.get('controller'),
                                              block_row=a.get('block_row'), block_column=a.get('block_column'), placement_policy=a.get('placement_policy'))
            elif name == 'fractal_plan_block_removal':
                planner = self._module('skills/fractal-fm9/scripts/plan_block_removal.py')
                plan = planner.plan_block_removal(snapshot, preset_slots=a['slots'], effect_id=a['effect_id'])
            else:
                planner = self._module('skills/fractal-fm9/scripts/plan_block_insertion.py')
                plan = planner.plan_block_insertion(snapshot, preset_slots=a['slots'], effect_id=a['effect_id'], row=a.get('row'), column=a.get('column'), placement_policy=a.get('placement_policy'))
            identifier, folder = self.artifacts.create()
            atomic_json(folder / 'plan.json', plan)
            return {'artifact_id': identifier, 'plan_sha256': sha(folder / 'plan.json'), 'plan': plan, 'ports_opened': False}
        if name == 'fractal_inspect_recovery':
            path = self.recovery(a['recovery_artifact'], a['state_sha256'])
            return self.runtime.run('hardware', ['--hardware', '--scene-midi-recovery', '--mode', 'inspect', '--recovery', path,
                                                '--expected-hash', a['state_sha256']], cancel)
        if name == 'fractal_migrate_recovery':
            source = self.recovery(a['recovery_artifact'], a['state_sha256'])
            identifier, folder = self.artifacts.create()
            result = self.runtime.run('cli', ['recovery-migrate', '--recovery', source,
                '--state-hash', a['state_sha256'], '--destination', folder / 'recovery'], cancel, maximum_seconds=60)
            return {'artifact_id': identifier, 'result': result, 'ports_opened': False}
        if name.startswith('fractal_prepare_'):
            return self.prepare(name, a, cancel)
        if name == 'fractal_execute_review':
            return self.start_execution(a, cancel)
        raise KeyError('Unknown tool.')

    def recovery(self, identifier, expected):
        state = self.artifacts.file(identifier, 'recovery/state.private.json')
        if sha(state) != expected:
            raise ValueError('Recovery head is stale or damaged.')
        return state.parent

    def insertion_undo_inputs(self, identifier, cancel):
        """Derive a closed removal request from complete retained insertion evidence, offline."""
        from .storage import loads
        receipt = self.artifacts.json(identifier, 'result.json')
        review_id = receipt['review_id']
        journal = confined(self.artifacts.root, 'attempts/' + review_id + '.json')
        attempt = loads(journal.read_text(encoding='utf-8'))
        if attempt.get('status') != 'verified-complete' or attempt.get('execution_artifact') != identifier or attempt.get('operation') != 'insert-blocks':
            raise ValueError('Insertion undo requires the exact verified-complete insertion attempt.')
        review = self.validate_review(review_id, attempt['review_sha256'])
        if review['operation'] != 'insert-blocks' or receipt.get('result', {}).get('complete') is not True:
            raise ValueError('Not a verified insertion receipt.')
        report = self.artifacts.json(identifier, 'execution/report.private.json')
        requests = self.artifacts.json(review_id, 'insertion-request.json')
        if report.get('complete') is not True or report.get('completed_count') != len(requests) or len(report.get('results', [])) != len(requests):
            raise ValueError('Partial insertion evidence cannot authorize removal.')
        if (report['model'], report['firmware']) != (self.device()['model'], self.device()['firmware']):
            raise ValueError('Insertion evidence context differs from configured device.')
        inserted_files = []
        receipt_files = [journal, self.artifacts.file(identifier, 'result.json'), self.artifacts.file(identifier, 'execution/report.private.json'),
                         self.artifacts.file(review_id, 'review.json')]
        for index, (request, result) in enumerate(zip(requests, report['results']), 1):
            if any(result.get(k) != request[k] for k in ('slot', 'row', 'column', 'effect_id', 'saved_scene')) or result.get('original_hash') != request['sysex_sha256']:
                raise ValueError('Insertion receipt target differs from approved intent.')
            if any(result.get(k) is not True for k in ('complete', 'existing_body_and_scene_midi_preserved', 'raw_header_and_padding_preserved', 'repeated_saved_equal', 'reloaded_equal')):
                raise ValueError('Insertion preservation/persistence was not verified.')
            relative = f'execution/item-{index:03}/reloaded/'
            manifest = self.artifacts.json(identifier, relative + 'manifest.json')
            entries = manifest['captures']
            if len(entries) != 1 or entries[0]['slot'] != request['slot'] or entries[0]['sysex_sha256'] != result['saved_hash']:
                raise ValueError('Inserted capture differs from verified result.')
            file = self.artifacts.file(identifier, relative + entries[0]['file'])
            if sha(file) != result['saved_hash']:
                raise ValueError('Inserted capture bytes changed.')
            inserted_files.append(file)
            receipt_files.extend([file, self.artifacts.file(identifier, relative + 'manifest.json')])
        backup_id, backup_folder = self.artifacts.create()
        args = ['decode', *self.device_args(), '--firmware', self.device()['firmware'], '--destination', backup_folder / 'export']
        for file in inserted_files:
            args += ['--capture', file]
        self.runtime.run('cli', args, cancel, log=backup_folder / 'runtime.json')
        _, snapshot = self.export(backup_id)
        self.match_context(snapshot)
        planner = self._module('skills/fractal-fm9/scripts/plan_block_removal.py')
        items = []
        for request in requests:
            item = planner.plan_block_removal(snapshot, preset_slots=[request['slot']], effect_id=request['effect_id'])['presets'][0]
            if item['status'] != 'planned' or (item['actions'][0]['row'], item['actions'][0]['column']) != (request['row'], request['column']):
                raise ValueError('Verified inserted block is not safely removable at its exact original cell.')
            items.append(item)
        plan = planner.blocks.plan_envelope(snapshot, items)
        plan['operation'] = 'undo-verified-insertion'
        plan_id, plan_folder = self.artifacts.create()
        atomic_json(plan_folder / 'plan.json', plan)
        return {'backup_artifact': backup_id, 'plan_artifact': plan_id, 'original_review': review_id}, self.artifacts.directory(review_id) / 'backup', receipt_files

    def prepare(self, name, a, cancel):
        device = self.device()
        identifier, folder = self.artifacts.create()
        sources = []
        summary = {'model': device['model'], 'capacity': device['capacity'], 'firmware': device['firmware']}
        def freeze(source, destination):
            if source.is_dir():
                hashes = self.artifacts.tree_hashes(source)
                shutil.copytree(source, destination)
                if self.artifacts.tree_hashes(destination) != hashes or self.artifacts.tree_hashes(source) != hashes:
                    raise ValueError('Input directory changed while freezing review.')
                sources.append({'path': source.relative_to(self.artifacts.root).as_posix(), 'files': hashes})
            else:
                digest = sha(source); shutil.copyfile(source, destination)
                if sha(source) != digest or sha(destination) != digest:
                    raise ValueError('Input file changed while freezing review.')
                sources.append({'path': source.relative_to(self.artifacts.root).as_posix(), 'sha256': digest})
        undo_source = None
        if name == 'fractal_prepare_insertion_undo':
            a, undo_source, receipt_files = self.insertion_undo_inputs(a['execution_artifact'], cancel)
            name = 'fractal_prepare_removal'
            for index, file in enumerate(receipt_files):
                freeze(file, folder / ('receipt-' + str(index) + file.suffix))
            freeze(undo_source, folder / 'undo-backup')
            summary['undo_insertion'] = True
        if name in ('fractal_prepare_preset_organization', 'fractal_prepare_organization_reconciliation'):
            if name == 'fractal_prepare_preset_organization':
                freeze(confined(self.artifacts.directory(a['organization_artifact']), 'organization'), folder / 'organization')
                freeze(self.artifacts.file(a['context_artifact'], 'context/context.json'), folder / 'context.json')
                validation = self.runtime.run('cli', ['organization-batch', '--bundle', folder / 'organization', '--expected-hash', sha(folder / 'organization/plan.json'),
                    '--context', folder / 'context.json', '--discard-changes', str(a.get('discard_changes', False)).lower(), '--destination', folder / 'batch'], cancel, log=folder / 'planning.json')
                summary['organization'] = validation['organization']
            else:
                freeze(confined(self.artifacts.directory(a['organization_review_artifact']), 'batch'), folder / 'batch')
            operation = 'preset-organization' if name == 'fractal_prepare_preset_organization' else 'reconcile-preset-organization'
            args = ['--hardware', '--edit-batch', '--bundle', folder / 'batch', '--expected-hash', sha(folder / 'batch/batch.json'),
                    '--mode', 'apply' if name == 'fractal_prepare_preset_organization' else 'reconcile']
            validation = self.runtime.run('hardware', [*args, '--validate-only', 'true'], cancel, log=folder / 'validation.json')
            if (validation['model'], validation['capacity'], validation['firmware']) != (device['model'], device['capacity'], device['firmware']):
                raise ValueError('Organization differs from configured device context.')
            summary.update(validation)
        elif name in ('fractal_prepare_edit_batch', 'fractal_prepare_batch_reconciliation'):
            if name == 'fractal_prepare_edit_batch':
                freeze(self.artifacts.file(a['context_artifact'], 'context/context.json'), folder / 'context.json')
                from .storage import loads
                context = loads((folder / 'context.json').read_text(encoding='utf-8'))
                references = []
                for index, plan_id in enumerate(a['plan_artifacts']):
                    relative = f'plan-{index:03}'
                    freeze(confined(self.artifacts.directory(plan_id), 'bundle'), folder / relative)
                    references.append({'bundle': relative, 'sha256': sha(folder / relative / 'plan.json')})
                atomic_json(folder / 'batch-request.json', {'schema_version': 1, 'context': context, 'plans': references, 'discard_changes': a.get('discard_changes', False)})
                self.runtime.run('cli', ['create-edit-batch', '--request', folder / 'batch-request.json', '--destination', folder / 'batch'], cancel)
            else:
                freeze(confined(self.artifacts.directory(a['batch_artifact']), 'batch'), folder / 'batch')
            operation = 'edit-batch' if name == 'fractal_prepare_edit_batch' else 'reconcile-edit-batch'
            args = ['--hardware', '--edit-batch', '--bundle', folder / 'batch', '--expected-hash', sha(folder / 'batch/batch.json'),
                    '--mode', 'apply' if name == 'fractal_prepare_edit_batch' else 'reconcile']
            validation = self.runtime.run('hardware', [*args, '--validate-only', 'true'], cancel, log=folder / 'validation.json')
            if (validation['model'], validation['capacity'], validation['firmware']) != (device['model'], device['capacity'], device['firmware']):
                raise ValueError('Batch differs from configured device context.')
            summary.update(validation)
        elif name in ('fractal_prepare_live_control', 'fractal_prepare_performance_control'):
            performance = name == 'fractal_prepare_performance_control'
            freeze(self.artifacts.file(a['context_artifact'], 'performance/context.json' if performance else 'context/context.json'), folder / 'context.json')
            from .storage import loads
            context = loads((folder / 'context.json').read_text(encoding='utf-8'))
            preset_context = context['preset'] if performance else context
            if (preset_context['model'], preset_context['capacity'], preset_context['firmware']) != (device['model'], device['capacity'], device['firmware']):
                raise ValueError('Live context differs from configured device.')
            request_data = {'schema_version': 1, 'context': context, **{key: value for key, value in a.items() if key != 'context_artifact'}}
            atomic_json(folder / 'request.json', request_data); operation = 'performance-control' if performance else 'live-control'
            args = ['--hardware', '--' + operation, '--request', folder / 'request.json', '--expected-hash', sha(folder / 'request.json')]
            validation = self.runtime.run('hardware', [*args, '--validate-only', 'true'], cancel, log=folder / 'validation.json')
            summary.update(validation)
        elif name in ('fractal_prepare_preset_edit', 'fractal_prepare_edit_reconciliation'):
            source = confined(self.artifacts.directory(a['plan_artifact']), 'bundle')
            freeze(source, folder / 'bundle'); operation = 'apply-preset-edit'
            digest = sha(folder / 'bundle/plan.json')
            args = ['--hardware', '--apply-preset-edit', '--bundle', folder / 'bundle', '--expected-hash', digest]
            if name == 'fractal_prepare_edit_reconciliation':
                args += ['--mode', 'reconcile']; operation = 'reconcile-preset-edit'
            validation = self.runtime.run('hardware', [*args, '--validate-only', 'true'], cancel, log=folder / 'validation.json')
            resolved = validation['plan']
            if (resolved['model'], resolved['capacity'], resolved['firmware']) != (device['model'], device['capacity'], device['firmware']):
                raise ValueError('Edit plan differs from configured device context.')
            summary.update(slot=resolved['slot'], persistence=resolved['persistence'], saved_scene=resolved['saved_scene'],
                           items=resolved['items'], routing=resolved['routing'], feedback_policy=resolved['feedback_policy'], base_sha256=resolved['base_sha256'],
                           expected_sha256=resolved['expected_sha256'], execution_method=resolved['execution_method'])
        elif name == 'fractal_prepare_preset_edit_undo':
            receipt = 'execution' + (f"/item-{a['item_index']:03}" if 'item_index' in a else '')
            source = confined(self.artifacts.directory(a['execution_artifact']), receipt)
            freeze(source, folder / 'receipt'); operation = 'undo-preset-edit'
            args = ['--hardware', '--apply-preset-edit', '--mode', 'undo', '--receipt', folder / 'receipt',
                    '--receipt-hash', sha(folder / 'receipt/attempt.json')]
            validation = self.runtime.run('hardware', [*args, '--validate-only', 'true'], cancel, log=folder / 'validation.json')
            resolved = validation['plan']
            if (resolved['model'], resolved['capacity'], resolved['firmware']) != (device['model'], device['capacity'], device['firmware']):
                raise ValueError('Undo receipt differs from configured device context.')
            summary.update(slot=resolved['slot'], persistence='save', restore_original_sha256=resolved['base_sha256'],
                           required_current_sha256=resolved['expected_sha256'], undo=True)
        elif name in ('fractal_prepare_scene_midi', 'fractal_prepare_insertion', 'fractal_prepare_removal'):
            backup, snapshot = self.export(a['backup_artifact']); self.match_context(snapshot)
            plan_file = self.artifacts.file(a['plan_artifact'], 'plan.json')
            plan = self.artifacts.json(a['plan_artifact'], 'plan.json')
            freeze(backup, folder / 'backup'); freeze(plan_file, folder / 'planner.json')
            if name == 'fractal_prepare_removal':
                planner = self._module('skills/fractal-fm9/scripts/plan_block_removal.py')
                for item in plan['presets']:
                    expected = planner.plan_block_removal(snapshot, preset_slots=[item['slot']], effect_id=plan.get('effect_id', item['actions'][0]['effect_id'] if item['actions'] else 0))
                    if expected['presets'] != [item]:
                        raise ValueError('Removal plan differs from the complete retained snapshot.')
            if not plan['presets'] or any(p['status'] not in ('planned', 'unchanged') or p['conflicts'] for p in plan['presets']):
                raise ValueError('Resolve every plan conflict/skipped target before execution review.')
            summary['targets'] = [{'slot': p['slot'], 'status': p['status'], 'actions': p['actions']} for p in plan['presets']]
            if name == 'fractal_prepare_scene_midi':
                if any(action['kind'] != 'set-scene-midi-message' for p in plan['presets'] for action in p['actions']):
                    raise ValueError('This review accepts message edits only. Use fractal_plan_block_insertion and fractal_prepare_insertion for a separate insertion review; after verified execution, capture fresh backups and replan the messages. A combined insertion/configuration transaction is not implemented.')
                operation = 'apply-scene-midi'; request = 'planner.json'
                args = ['--hardware', '--apply-scene-midi', *self.device_args(), '--firmware', device['firmware'], '--backup', folder / 'backup',
                        '--plan', folder / request, '--expected-hash', sha(folder / request), '--undo-after', 'false']
            else:
                removing = name == 'fractal_prepare_removal'
                entries = {e['slot']: e for e in self.artifacts.json(a['backup_artifact'], 'export/manifest.json')['captures']}
                bodies = {p['slot']: p for p in snapshot['presets']}
                targets = []
                for p in plan['presets']:
                    if p['status'] == 'unchanged':
                        continue
                    if len(p['actions']) != 1 or p['actions'][0]['kind'] != ('remove-block' if removing else 'insert-block'):
                        raise ValueError('Only one exact isolated block operation per target is supported.')
                    action = p['actions'][0]
                    scene_word = bodies[p['slot']]['body_header_words'][0]
                    if type(scene_word) is not int or not 0 <= scene_word < 8:
                        raise ValueError('Retained saved scene is outside the qualified field range.')
                    targets.append({'slot': p['slot'], 'row': action['row'], 'column': action['column'], 'effect_id': action['effect_id'],
                                    'saved_scene': scene_word + 1, 'sysex_sha256': entries[p['slot']]['sysex_sha256']})
                    if undo_source is not None:
                        original_manifest = self.artifacts.json(a['original_review'], 'backup/manifest.json')
                        original = next(e for e in original_manifest['captures'] if e['slot'] == p['slot'])
                        targets[-1].update(undo_file=original['file'], undo_sha256=original['sysex_sha256'])
                if not targets:
                    raise ValueError('All block targets are unchanged; no execution is needed.')
                operation = 'remove-blocks' if removing else 'insert-blocks'
                request = 'removal-request.json' if removing else 'insertion-request.json'; atomic_json(folder / request, targets)
                args = ['--hardware', '--' + operation, *self.device_args(), '--firmware', device['firmware'], '--backup', folder / 'backup',
                        '--plan', folder / request, '--expected-hash', sha(folder / request)]
                if undo_source is not None:
                    args += ['--undo-backup', folder / 'undo-backup']
            validation = self.runtime.run('hardware', [*args, '--validate-only', 'true'], cancel, log=folder / 'validation.json')
        elif name == 'fractal_prepare_load':
            context = confined(self.artifacts.directory(a['context_artifact']), 'context')
            request_data = self.artifacts.json(a['context_artifact'], 'context/load-request.json')
            if (request_data['model'], request_data['capacity'], request_data['firmware']) != (device['model'], device['capacity'], device['firmware']):
                raise ValueError('Load context differs from configured device.')
            freeze(context, folder / 'context'); request = 'context/load-request.json'; operation = 'load-preset'
            args = ['--hardware', '--load-preset', '--request', folder / request, '--expected-hash', sha(folder / request)]
            validation = self.runtime.run('hardware', [*args, '--validate-only', 'true'], cancel, log=folder / 'validation.json')
            summary.update(current_slot=request_data['current_slot'], target_slot=request_data['target_slot'], discard_changes=request_data['discard_changes'])
            summary.update(midi_mapping=request_data.get('midi_mapping'), wire_messages=validation.get('wire_messages'))
        elif name == 'fractal_prepare_recovery':
            source = self.recovery(a['recovery_artifact'], a['state_sha256'])
            freeze(source, folder / 'recovery'); request = 'recovery/state.private.json'; operation = a['mode']
            args = ['--hardware', '--scene-midi-recovery', '--mode', a['mode'], '--recovery', folder / 'recovery', '--expected-hash', a['state_sha256']]
            validation = self.runtime.run('hardware', [*args, '--validate-only', 'true'], cancel, log=folder / 'validation.json')
            if (validation['model'], validation['firmware']) != (device['model'], device['firmware']):
                raise ValueError('Recovery context differs from configured device.')
            if validation['capacity'] != device['capacity']:
                raise ValueError('Recovery capacity differs from configured device.')
            summary['items'] = validation['items']
        else:
            raise KeyError('Unknown review operation.')
        bindings = self.artifacts.tree_hashes(folder)
        review = {'schema_version': 1, 'release': self.version, 'data_root': str(self.artifacts.root), 'operation': operation, 'device': device, 'summary': summary,
                  'sources': sources, 'frozen_files': bindings, 'arguments': [str(arg) for arg in args], 'validation': validation,
                  'automatic_retry': False, 'automatic_rollback': False}
        atomic_json(folder / 'review.json', review)
        return {'review_id': identifier, 'review_sha256': sha(folder / 'review.json'), 'summary': summary, 'validation': validation,
                'approval_required': True, 'ports_opened': False}

    def validate_review(self, identifier, expected):
        path = self.artifacts.file(identifier, 'review.json')
        if sha(path) != expected:
            raise ValueError('Review hash differs from the reviewed bytes.')
        review = self.artifacts.json(identifier, 'review.json')
        if review['schema_version'] != 1 or review['release'] != self.version or review['device'] != self.device() or review['data_root'] != str(self.artifacts.root):
            raise ValueError('Review release or device/ports differ from the current operator configuration.')
        folder = path.parent
        for name, digest in review['frozen_files'].items():
            if sha(self.artifacts.file(identifier, name)) != digest:
                raise ValueError('Frozen review input changed.')
        for source in review['sources']:
            original = confined(self.artifacts.root, source['path'])
            if 'files' in source:
                if self.artifacts.tree_hashes(original) != source['files']:
                    raise ValueError('Source artifact changed after review.')
            elif sha(original) != source['sha256']:
                raise ValueError('Source artifact changed after review.')
        return review

    def approve(self, identifier, expected):
        if not self.config['allow_writes']:
            raise ValueError('Writes are disabled by the operator.')
        review = self.validate_review(identifier, expected)
        path = self.artifacts.root / 'approvals' / (identifier + '.json')
        consumed = path.with_suffix('.consumed')
        if path.exists() or consumed.exists():
            raise ValueError('This review was already approved or attempted. Prepare a fresh review.')
        # Exclusive creation; only this operator CLI exposes approval, never an MCP tool.
        with path.open('x', encoding='utf-8') as stream:
            stream.write(dumps({'review_id': identifier, 'review_sha256': expected, 'device': review['device']}) + '\n')
            stream.flush(); __import__('os').fsync(stream.fileno())
        return {'approved_review': identifier, 'review_sha256': expected, 'summary': review['summary']}

    def inspect_attempt(self, identifier, wait_seconds=0, cancel=None):
        from .storage import loads
        path = confined(self.artifacts.root, 'attempts/' + identifier + '.json')
        deadline = time.monotonic() + wait_seconds
        while True:
            value = loads(path.read_text(encoding='utf-8'))
            if value['status'] != 'attempted' or time.monotonic() >= deadline:
                break
            if cancel is not None and cancel.wait(min(.1, max(0, deadline - time.monotonic()))):
                from .runtime import Cancelled
                raise Cancelled('Status wait cancelled; the accepted job is still running. Use cancel_attempt to stop it.')
        folder = self.artifacts.directory(value['execution_artifact'])
        recovery = folder / 'recovery/state.private.json'
        if recovery.is_file():
            value.update(recovery_artifact=value['execution_artifact'], state_sha256=sha(self.artifacts.file(value['execution_artifact'], 'recovery/state.private.json')))
        result = folder / 'result.json'
        if (folder / 'execution/progress.json').is_file():
            value['progress_sha256'] = sha(self.artifacts.file(value['execution_artifact'], 'execution/progress.json'))
        if value['status'] in ('verified-complete', 'sent-unverified') and result.is_file():
            value['result'] = self.artifacts.json(value['execution_artifact'], 'result.json')
        value['completion_verified'] = value['status'] == 'verified-complete'
        value['next_action'] = ('Poll fractal_inspect_attempt with wait_seconds 20; do not execute again.'
                                if value['status'] == 'attempted' else
                                'Inspect exact recovery head and reconcile before preparing any new mutation.'
                                if value['status'] == 'stopped-uncertain' else
                                'Read the sent-unverified result; do not replay the event to obtain confirmation.'
                                if value['status'] == 'sent-unverified' else 'Read the verified result.')
        return value

    def start_execution(self, a, request_cancel):
        # Refuse invalid/unapproved requests synchronously, before accepting a job.
        device = self.live()
        if not self.config['allow_writes']:
            raise ValueError('Writes are disabled by the operator.')
        self.validate_review(a['review_id'], a['review_sha256'])
        from .storage import loads
        approval = confined(self.artifacts.root, 'approvals/' + a['review_id'] + '.json')
        if loads(approval.read_text(encoding='utf-8')) != {'review_id': a['review_id'], 'review_sha256': a['review_sha256'], 'device': device}:
            raise ValueError('Operator approval does not match this exact review/device.')
        job = {'cancel': threading.Event(), 'ready': threading.Event(), 'done': threading.Event(), 'error': None}
        class StartCancellation:
            def is_set(self):
                return job['cancel'].is_set() or (not job['ready'].is_set() and request_cancel.is_set())
        def run():
            try:
                self.execute(a, StartCancellation(), on_started=job['ready'].set)
            except Exception as error:
                job['error'] = str(error)
            finally:
                job['done'].set(); job['ready'].set()
        with self.execution_lock:
            previous = self.execution_jobs.get(a['review_id'])
            if previous is not None:
                raise ValueError('This review already has an execution job; inspect its attempt. No replay.')
            self.execution_jobs = {key: value for key, value in self.execution_jobs.items() if not value['done'].is_set()}
            if len(self.execution_jobs) >= 32:
                raise ValueError('Execution job limit reached; no operation started.')
            self.execution_jobs[a['review_id']] = job
            job['thread'] = threading.Thread(target=run, daemon=True)
            job['thread'].start()
        # The ready checkpoint is written before launching hardware. No long tool call.
        if not job['ready'].wait(5):
            job['cancel'].set()
            raise ValueError('Execution startup uncertain; inspect the attempt, never resend.')
        journal = self.artifacts.root / 'attempts' / (a['review_id'] + '.json')
        if not journal.is_file():
            raise ValueError(job['error'] or 'Execution did not start; inspect before any retry.')
        value = self.inspect_attempt(a['review_id'])
        value['accepted'] = True
        return value

    def stop_execution_jobs(self):
        with self.execution_lock:
            jobs = list(self.execution_jobs.values())
            for job in jobs:
                job['cancel'].set()
        deadline = time.monotonic() + 15
        for job in jobs:
            job['thread'].join(timeout=max(0, deadline - time.monotonic()))

    def execute(self, a, cancel, on_started=None):
        device = self.live()
        if not self.config['allow_writes']:
            raise ValueError('Writes are disabled by the operator.')
        review = self.validate_review(a['review_id'], a['review_sha256'])
        approval = confined(self.artifacts.root, 'approvals/' + a['review_id'] + '.json')
        from .storage import loads
        authorized = loads(approval.read_text(encoding='utf-8'))
        if authorized != {'review_id': a['review_id'], 'review_sha256': a['review_sha256'], 'device': device}:
            raise ValueError('Operator approval does not match this exact review/device.')
        with device_owner(device):
            if cancel.is_set():
                from .runtime import Cancelled
                raise Cancelled('Cancelled before approval consumption.')
            # Consume once BEFORE starting the runtime, including when its outcome is uncertain.
            if approval.with_suffix('.consumed').exists():
                raise ValueError('This review was already attempted; no replay.')
            __import__('os').rename(approval, approval.with_suffix('.consumed'))
            identifier, folder = self.artifacts.create()
            attempt = {'review_id': a['review_id'], 'review_sha256': a['review_sha256'], 'operation': review['operation'],
                       'status': 'attempted', 'execution_artifact': identifier, 'automatic_retry': False, 'automatic_rollback': False}
            journal = self.artifacts.root / 'attempts' / (a['review_id'] + '.json')
            atomic_json(journal, attempt)
            if on_started is not None:
                on_started()
            try:
                # A review is immutable. Recovery work occurs on a private copy, leaving earlier evidence intact.
                source = self.artifacts.directory(a['review_id'])
                command = list(review['arguments'])
                if review['operation'] in ('resume', 'undo'):
                    shutil.copytree(source / 'recovery', folder / 'recovery')
                    command[command.index('--recovery') + 1] = str(folder / 'recovery')
                command += [*self.connection_args(), '--destination', str(folder / 'execution')]
                result = self.runtime.run('hardware', command, cancel, log=folder / 'runtime.json')
                if result.get('complete') is not True:
                    raise ValueError('Runtime did not report verified completion.')
                if review['operation'] == 'performance-control' and not isinstance(result.get('state_verified'), bool):
                    raise ValueError('Performance runtime omitted its state verification limit.')
                # Existing initial executor writes its durable record under its evidence directory.
                if (folder / 'execution/recovery').is_dir():
                    shutil.copytree(folder / 'execution/recovery', folder / 'recovery')
                attempt['status'] = ('sent-unverified' if review['operation'] == 'performance-control' and result.get('state_verified') is False else 'verified-complete')
                return_value = {'artifact_id': identifier, 'result': result, 'review_id': a['review_id']}
                if (folder / 'recovery/state.private.json').is_file():
                    return_value.update(recovery_artifact=identifier, state_sha256=sha(folder / 'recovery/state.private.json'))
                atomic_json(folder / 'result.json', return_value)
                return return_value
            except Exception as error:
                attempt['status'] = 'stopped-uncertain'
                attempt['error'] = str(error)
                # The child may have checkpointed before stopping. Surface that record without claiming completion.
                if (folder / 'execution/recovery').is_dir() and not (folder / 'recovery').exists():
                    shutil.copytree(folder / 'execution/recovery', folder / 'recovery')
                raise ValueError(f'Execution stopped; approval consumed. Evidence artifact {identifier}. {error}') from error
            finally:
                atomic_json(journal, attempt)
