"""Search the portable knowledge base without an AI account or dependencies."""
import argparse
import json
import re
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def score(text, terms):
    words = re.findall(r'[a-z0-9]+', text.lower())
    return 10 * sum(term in words for term in terms) + sum(min(words.count(term), 2) for term in terms)


def search(query, limit):
    terms = set(re.findall(r'[a-z0-9]+', query.lower())) - {'the', 'a', 'an', 'and', 'to', 'of', 'in', 'how'}
    if not terms:
        raise ValueError('Use at least one searchable word.')
    recipes = []
    for path in sorted((ROOT / 'knowledge-base/workflows').glob('*.md')):
        body = path.read_text(encoding='utf-8')
        relevance = score(body, terms)
        if relevance:
            recipes.append({'score': relevance, 'title': body.splitlines()[0].removeprefix('# '), 'path': path.relative_to(ROOT).as_posix()})
    data = json.loads((ROOT / 'knowledge-base/catalog/capabilities.json').read_text(encoding='utf-8'))
    capabilities = []
    for record in data['capabilities']:
        combined = record['title'] + ' ' + record['id'].replace('-', ' ') + ' ' + ' '.join(record['inputs'] + record['outputs']) + ' ' + record['evidence_scope']
        present = set(re.findall(r'[a-z0-9]+', combined.lower())) & terms
        if len(present) < min(2, len(terms)):
            continue
        relevance = 3 * score(record['title'], terms) + score(combined, terms)
        for phrase in ('scene midi', 'default scene', 'saved preset', 'amp roster', 'real amplifier', 'network midi'):
            if phrase in query.lower() and phrase in combined.lower():
                relevance += 60
        if relevance:
            capabilities.append({'score': relevance, 'id': record['id'], 'title': record['title'], 'workflow': 'knowledge-base/' + record['workflow'], 'toolkit_support': record['toolkit_support'], 'evidence_scope': record['evidence_scope'], 'source_ids': record['source_ids'], 'tool': record.get('tool')})
    ranked_capabilities = sorted(capabilities, key=lambda r: (-r['score'], r['id']))[:limit]
    wanted_sources = {sid for c in ranked_capabilities for sid in c['source_ids']}
    register = json.loads((ROOT / 'knowledge-base/catalog/sources.json').read_text(encoding='utf-8'))
    sources = []
    for source in register['sources']:
        combined = source['title'] + ' ' + source['scope']
        relevance = 3 * score(source['title'], terms) + score(combined, terms)
        if source['id'] not in wanted_sources and not (set(re.findall(r'[a-z0-9]+', combined.lower())) & terms):
            continue
        sources.append({**source, 'score': relevance + (30 if source['id'] in wanted_sources else 0)})
    return {'query': query, 'documentation_index': 'knowledge-base/reference-map.md',
            'workflows': sorted(recipes, key=lambda r: (-r['score'], r['path']))[:limit],
            'capabilities': ranked_capabilities,
            'sources': sorted(sources, key=lambda r: (-r['score'], r['id']))[:limit]}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('query')
    parser.add_argument('--limit', type=int, default=5)
    parser.add_argument('--json', action='store_true', dest='as_json')
    args = parser.parse_args()
    if not 1 <= args.limit <= 50:
        parser.error('--limit must be between 1 and 50')
    try:
        result = search(args.query, args.limit)
    except ValueError as exc:
        parser.error(str(exc))
    if args.as_json:
        print(json.dumps(result, indent=2, ensure_ascii=False))
    else:
        for item in result['workflows']:
            print(item['title'] + '\n  ' + item['path'])
        for item in result['capabilities']:
            print(item['id'] + ': ' + item['title'] + '\n  ' + item['toolkit_support'] + ' · ' + item['workflow'])
        print('Documentation sections and symbols: ' + result['documentation_index'])
        for item in result['sources']:
            print(item['id'] + ': ' + item['title'] + '\n  ' + (item.get('url') or item['local_reference']))


if __name__ == '__main__':
    main()
