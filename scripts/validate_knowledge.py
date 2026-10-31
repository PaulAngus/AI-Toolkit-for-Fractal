"""Validate portable domain capabilities, sources, data and local links."""
import json
import re
from pathlib import Path
from urllib.parse import urlsplit

ROOT = Path(__file__).resolve().parents[1]
KB = ROOT / 'knowledge-base'


def read(name):
    return json.loads((KB / 'catalog' / name).read_text(encoding='utf-8'))


def main():
    capabilities = read('capabilities.json')['capabilities']
    sources = read('sources.json')['sources']
    source_ids = {s['id'] for s in sources}
    assert len(source_ids) == len(sources)
    assert len({c['id'] for c in capabilities}) == len(capabilities)
    for capability in capabilities:
        assert capability['title'] and capability['inputs'] and capability['outputs'] and capability['evidence_scope']
        assert capability['toolkit_support'] in {'reference-only', 'research-reference', 'offline-tool', 'runtime-tool'}
        assert (KB / capability['workflow']).is_file(), capability['id']
        assert set(capability['source_ids']) <= source_ids, capability['id']
        if capability['toolkit_support'] in {'offline-tool', 'runtime-tool'}:
            assert (ROOT / capability['tool']).is_file(), capability['id']
    for source in sources:
        if 'url' in source:
            parts = urlsplit(source['url'])
            assert parts.scheme in ('https', 'http') and parts.netloc
            assert not source['url'].endswith(('):', ':)', ':', "'")), source['url']
            assert 'chatgpt.com/' not in source['url'], source['url']
        else:
            assert (ROOT / source['local_reference']).is_file(), source['id']
    roster = read('fm9-12-amp-roster.json')['entries']
    blocks = read('block-identities.json')
    entries = blocks['entries']
    assert len(entries) == len({e['effect_id'] for e in entries}) == 162
    assert len({e['family'] for e in entries}) == 41
    models_supported = {'FM9', 'FM3', 'Axe-Fx III'}
    for entry in entries:
        assert entry['name'] and entry['family'] and entry['instance'] > 0
        assert set(entry['identity_source_ids']) <= source_ids
        assert set(entry['identity_models']) <= models_supported
        assert set(entry['documented_instances']) == set(entry['documented_available']) == models_supported
        assert all(type(v) is bool for v in entry['documented_available'].values())
        assert all(type(v) is int and 0 <= v <= 5 for v in entry['documented_instances'].values())
    assert {s['source_id'] for s in blocks['sources']} <= source_ids
    assert all(re.fullmatch(r'[0-9a-f]{64}', s['sha256']) and s['revision'] and s['locator'] for s in blocks['sources'])
    by_id = {e['effect_id']: e for e in entries}
    for family in {e['family'] for e in entries}:
        group = [e for e in entries if e['family'] == family]
        for model in models_supported:
            assert len({e['documented_instances'][model] for e in group}) == 1
            assert sum(e['documented_available'][model] for e in group) == group[0]['documented_instances'][model]
    assert by_id[66]['name'] == 'Reverb 1' and by_id[67]['documented_available'] == {'Axe-Fx III': True, 'FM9': True, 'FM3': False}
    assert not by_id[40]['documented_available']['FM9'] and by_id[41]['documented_available']['FM9']
    assert blocks['unmapped_families'][0]['family'] == 'Dynamic Distortion'
    edit = read('edit-read-profiles.json')
    assert edit['schema_version'] == 1 and edit['revision'] == 3
    assert {s['id'] for s in edit['sources']} <= source_ids
    assert all(re.fullmatch(r'[0-9a-f]{64}', s['sha256']) for s in edit['sources'] if 'sha256' in s)
    assert {p['model'] for p in edit['models']} == {'FM9', 'FM3', 'Axe-Fx III'} and len(edit['models']) == 3
    assert len({p['profile_id'] for p in edit['models']}) == 3
    for profile in edit['models']:
        assert profile['instance_prefix_delta'] == -12
        assert profile['scene_names']['write_supported'] is False
        route = profile['routing']
        assert route['encoding'] == 'previous-column-source-row-mask'
        assert route['known_mask'] == (15 if profile['model'] == 'FM3' else 63)
        assert route['edge_decode_supported'] is True and route['write_supported'] is False
        assert route['source_id'] in source_ids
        assert len(profile['amp']['type_offsets']) == 4
        assert len(profile['amp']['channel_offsets']) == len(profile['amp']['bypass_offsets']) == 8
        keys = set()
        for field in profile['audio_fields']:
            assert field['source_id'] in source_ids and field['locator']
            assert field['write_supported'] is False and field['read_support'] == 'candidate-offline-read'
            assert field['scale'] in {'identity', 'u16-to-0-10'} and field['rows'] == 4
            assert all(by_id[e]['family'] == field['family'] and profile['model'] in by_id[e]['identity_models'] for e in field['effect_ids'])
            assert set(map(str, field['columns'])) == set(field['offsets_by_columns'])
            for cols, offsets in field['offsets_by_columns'].items():
                assert (field['key'], cols) not in keys
                keys.add((field['key'], cols))
                assert len(offsets) == 4 and all(type(o) is int and o % 2 == 0 and -12 <= o and o + 2 <= -12 + (23 + int(cols) * 4) * 2 for o in offsets)
            if 'condition' in field:
                condition = field['condition']
                assert any(f['key'] == condition['key'] and f['columns'] == field['columns'] and f['effect_ids'] == field['effect_ids'] and 'condition' not in f for f in profile['audio_fields'])
    audio = read('audio-parameter-mappings.json')
    scales = read('saved-value-scales.json')
    assert scales['schema_version'] == 1 and scales['revision'] == 2
    assert re.fullmatch(r'[0-9a-f]{40}', scales['source_revision'])
    assert {s['source_id'] for s in scales['sources']} <= source_ids
    assert all(re.fullmatch(r'[0-9a-f]{64}', s['sha256']) for s in scales['sources'])
    profile_keys = set()
    for profile in scales['model_profiles']:
        assert profile['model'] in models_supported and re.fullmatch(r'[0-9]{1,3}\.[0-9]{2}', profile['firmware'])
        assert (profile['model'], profile['firmware']) not in profile_keys
        profile_keys.add((profile['model'], profile['firmware']))
        words, keys = set(), set()
        for field in profile['fields']:
            assert field['key'] not in keys and field['source_id'] in source_ids and field['source_locator']
            keys.add(field['key'])
            scale = field['scale']
            assert scale['minimum'] < scale['maximum'] and scale['raw_maximum'] == 65534 and scale['unit']
            assert scale['taper'] in ('linear', 'log', 'int-raw') and (scale['taper'] != 'log' or scale['minimum'] > 0)
            if scale['taper'] == 'int-raw':
                assert type(scale['minimum']) is int and type(scale['maximum']) is int
                assert 0 <= scale['minimum'] < scale['maximum'] <= scale['raw_maximum']
            assert 0 <= field['parameter_id'] < field['columns'] <= 500
            for effect in field['effect_ids']:
                assert by_id[effect]['family'] == field['family'] and profile['model'] in by_id[effect]['identity_models']
                address = (effect, field['columns'], field['parameter_id'])
                assert address not in words
                words.add(address)
        modifier_maps = [profile['modifier_percent_fields'], profile['modifier_normalized_fields'], profile['modifier_raw_selectors']]
        modifier_keys = [key for mapping in modifier_maps for key in mapping]
        modifier_words = [word for mapping in modifier_maps for word in mapping.values()]
        assert len(set(modifier_keys)) == len(modifier_keys) and len(set(modifier_words)) == len(modifier_words)
        assert all(0 < word < 25 and word not in (8, 9) for word in modifier_words)
    assert audio['schema_version'] == audio['revision'] == 1
    assert re.fullmatch(r'[0-9a-f]{40}', audio['source_revision'])
    assert {m['model'] for m in audio['models']} == models_supported and len(audio['models']) == 3
    for model in audio['models']:
        assert model['source_id'] in source_ids and audio['source_revision'] in model['source_url']
        assert re.fullmatch(r'[0-9a-f]{64}', model['source_sha256'])
        parameters = model['entries']
        assert len(parameters) == {'FM9': 1577, 'FM3': 1581, 'Axe-Fx III': 1651}[model['model']]
        assert len({(p['family'], p['symbol']) for p in parameters}) == len(parameters)
        for parameter in parameters:
            assert type(parameter['parameter_id']) is int and parameter['parameter_id'] >= 0
            assert parameter['addressable'] is (parameter['parameter_id'] <= 16383)
            assert parameter['calibration_evidence'] and parameter['unit_hint']
            assert 'saved_offset' not in parameter
            for key in ('display_minimum_hint', 'display_maximum_hint'):
                assert parameter[key] is None or type(parameter[key]) in (int, float)
    assert [e['id'] for e in roster] == list(range(336))
    assert all(e['name'] for e in roster)
    identities = read('fm9-12-amp-identities.json')
    assert identities['device_model'] == 'FM9' and identities['firmware'] == '12.00'
    assert identities['numeric_identity_source'] in source_ids
    models = identities['models']
    assert [(m['id'], m['fractal_name']) for m in models] == [(r['id'], r['name']) for r in roster]
    families = {f['id']: f for f in identities['families']}
    assert len(families) == len(identities['families']) == 162
    membership = [mid for f in families.values() for mid in f['model_ids']]
    assert sorted(membership) == list(range(336)), 'Each numeric ID must belong to exactly one explicit family'
    for model in models:
        assert model['id'] in families[model['family_id']]['model_ids']
        assert model['specific_model'] and model['relationship'] and model['attribution_status']
        assert set(model['source_ids']) <= source_ids
        assert all(ref['source_id'] in source_ids and ref['locator'] and ref['role'] for ref in model['source_locators'])
        assert model['wiki_url'].startswith('https://wiki.fractalaudio.com/wiki/index.php?title=Amp_models#')
    assert all(re.fullmatch(r'[0-9a-f]{64}', item['sha256']) for item in identities['input_artifacts'])
    audit = json.loads((ROOT / 'docs/capability-audit.json').read_text(encoding='utf-8'))
    assert {c['capability_id'] for c in audit['capabilities']} == {c['id'] for c in capabilities}
    assert len(audit['capabilities']) == len(capabilities)
    for row in audit['capabilities']:
        capability = next(c for c in capabilities if c['id'] == row['capability_id'])
        assert row['execution'] == capability['toolkit_support'] and row['gap']
        assert row['workflow'] == capability['workflow']
        assert row['source_ids'] == capability['source_ids']
    for reference in ('documentation_index', 'source_register'):
        assert (ROOT / audit[reference]).is_file()
    # The distributable base must not accidentally regain the application-history export.
    assert not (KB / 'evidence').exists(), 'Application snapshots belong outside the toolkit'
    assert not list((KB / 'catalog').glob('chat-*')), 'Chat records belong outside the toolkit'
    for excluded in ('requests.json', 'chats.json', 'documents.json', 'coverage.json', 'coverage.md', 'chats.md'):
        assert not (KB / 'catalog' / excluded).exists(), excluded
    for path in [*KB.rglob('*.json'), *(ROOT / 'docs').glob('*.json')]:
        content = path.read_text(encoding='utf-8')
        assert not re.search(r'C:[/\\]Users[/\\]|192\.168\.\d+\.\d+|"(?:chat_id|chat_title|turn_id|specific_request)"', content), str(path)
        def check_evidence_labels(value):
            if isinstance(value, dict):
                for key, item in value.items():
                    if key == 'private_evidence_sha256':
                        assert isinstance(item, dict) and all(re.fullmatch(r'artifact-\d{3}', label) for label in item), \
                            f'{path}: publish neutral evidence IDs, not private filenames or setup paths'
                    check_evidence_labels(item)
            elif isinstance(value, list):
                for item in value:
                    check_evidence_labels(item)
        check_evidence_labels(json.loads(content))
    errors = []
    markdown = [ROOT / 'README.md', ROOT / 'AGENTS.md', *KB.rglob('*.md'), *(ROOT / 'docs').rglob('*.md'), *(ROOT / 'skills').rglob('*.md')]
    for path in markdown:
        content = path.read_text(encoding='utf-8')
        assert not re.search(r'C:[/\\]Users[/\\]|192\.168\.\d+\.\d+', content), str(path)
        for target in re.findall(r'\]\(([^\n]+?)\)', content):
            if target.startswith(('http:', 'https:', '#', 'mailto:')):
                continue
            clean = target.strip('<>').split('#', 1)[0]
            if clean and not (path.parent / clean).is_file():
                errors.append(str(path.relative_to(ROOT)) + ': ' + target)
    assert not errors, '\n'.join(errors)
    print(f'Knowledge validation passed: {len(capabilities)} capabilities, {len(sources)} sources, 336 model mappings, 162 families; audit, portable links and scope checked.')


if __name__ == '__main__':
    main()
