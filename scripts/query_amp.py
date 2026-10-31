"""Look up recorded FM9 12.00 amp models, families and qualified attributions."""
import argparse
import json
import re
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
CATALOGUE = ROOT / 'knowledge-base/catalog/fm9-12-amp-identities.json'


def load_catalogue():
    return json.loads(CATALOGUE.read_text(encoding='utf-8'))


def lookup(query=None, model_id=None, *, model='FM9', firmware='12.00', catalogue=None):
    data = catalogue if catalogue is not None else load_catalogue()
    if model.upper() != data['device_model'] or firmware != data['firmware']:
        raise ValueError('This catalogue is scoped to FM9 firmware 12.00. Obtain a verified roster for the requested scope.')
    if (query is None) == (model_id is None):
        raise ValueError('Provide either search words or one numeric model ID.')
    if model_id is not None and (type(model_id) is not int or not 0 <= model_id < 336):
        raise ValueError('The recorded numeric model ID must be between 0 and 335.')
    terms = set(re.findall(r'[a-z0-9]+', query.lower())) if query is not None else set()
    if query is not None and not terms:
        raise ValueError('Use at least one searchable word.')
    families = {f['id']: f for f in data['families']}
    matches = []
    for record in data['models']:
        family = families[record['family_id']]
        text = ' '.join([record['fractal_name'], record['specific_model'], family['manufacturer'], family['name'], *family['aliases']])
        words = set(re.findall(r'[a-z0-9]+', text.lower()))
        name_fields = [record['fractal_name'], family['name'], *family['aliases']]
        compact_names = {re.sub(r'[^a-z0-9]', '', field.lower()) for field in name_fields}
        compact_query = re.sub(r'[^a-z0-9]', '', query.lower()) if query is not None else ''
        text_matches = terms <= words or compact_query in compact_names
        if (model_id is not None and record['id'] != model_id) or not text_matches:
            continue
        matches.append({**record, 'family': family})
    source_ids = {sid for r in matches for sid in r['source_ids']} | {'recorded-roster', 'reviewed-amp-identities'}
    register = json.loads((ROOT / 'knowledge-base/catalog/sources.json').read_text(encoding='utf-8'))
    return {'device_model': data['device_model'], 'firmware': data['firmware'],
            'reviewed_on': data['reviewed_on'], 'evidence_policy': data['evidence_policy'],
            'scope': data['scope'], 'numeric_identity_source': data['numeric_identity_source'],
            'query': query, 'model_id': model_id, 'matches': matches,
            'sources': [s for s in register['sources'] if s['id'] in source_ids]}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    selector = parser.add_mutually_exclusive_group(required=True)
    selector.add_argument('--query', help='Model, manufacturer, family or alias words; all words must match.')
    selector.add_argument('--id', type=int, dest='model_id', help='Recorded numeric device model ID, 0–335.')
    parser.add_argument('--model', default='FM9')
    parser.add_argument('--firmware', default='12.00')
    parser.add_argument('--json', action='store_true', dest='as_json')
    args = parser.parse_args()
    try:
        result = lookup(args.query, args.model_id, model=args.model, firmware=args.firmware)
    except ValueError as exc:
        parser.error(str(exc))
    if args.as_json:
        print(json.dumps(result, indent=2, ensure_ascii=False))
    else:
        print('FM9 12.00 — historical roster and qualified attribution review, ' + result['reviewed_on'])
        for record in result['matches']:
            family = record['family']
            print(f"{record['id']}: {record['fractal_name']} → {record['specific_model']}")
            print(f"  Family: {family['manufacturer']} / {family['name']}; relationship: {record['relationship']}")
            print('  Attribution: ' + record['attribution_status'])
            if record['qualifications']:
                print('  Qualification: ' + record['qualifications'])
            print('  Reading: ' + record['wiki_url'])
        print(f"{len(result['matches'])} match(es). Family confidence does not verify every revision-level detail.")


if __name__ == '__main__':
    main()
