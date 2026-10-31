import importlib.util
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location('query_amp', ROOT / 'scripts/query_amp.py')
amp = importlib.util.module_from_spec(spec)
spec.loader.exec_module(amp)


class AmpLookupTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.catalogue = amp.load_catalogue()

    def query(self, *args, **kwargs):
        return amp.lookup(*args, catalogue=self.catalogue, **kwargs)

    def test_portabass_keeps_the_inference_and_sources(self):
        match = self.query(model_id=263)['matches'][0]
        self.assertEqual(match['fractal_name'], 'Porta-Bass')
        self.assertEqual(match['family']['manufacturer'], 'Ampeg')
        self.assertIn('inference', match['qualifications'])
        self.assertIn('does not explicitly name', match['qualifications'])
        self.assertIn('archived-ares-models', match['source_ids'])

    def test_block_crunch_is_peavey_not_evh(self):
        matches = self.query('block crunch')['matches']
        self.assertEqual(len(matches), 1)
        self.assertEqual(matches[0]['family']['manufacturer'], 'Peavey')
        self.assertIn('5150', matches[0]['specific_model'])

    def test_separator_variation_finds_portabass(self):
        self.assertEqual([r['id'] for r in self.query('PortaBass')['matches']], [263])

    def test_reissue_is_a_distinct_family(self):
        original = self.query(model_id=0)['matches'][0]
        reissue = self.query(model_id=302)['matches'][0]
        self.assertNotEqual(original['family_id'], reissue['family_id'])

    def test_every_model_has_exactly_one_family_and_matches_roster(self):
        import json
        roster = json.loads((ROOT / 'knowledge-base/catalog/fm9-12-amp-roster.json').read_text(encoding='utf-8'))['entries']
        membership = [mid for f in self.catalogue['families'] for mid in f['model_ids']]
        self.assertEqual(sorted(membership), list(range(336)))
        self.assertEqual([(r['id'], r['fractal_name']) for r in self.catalogue['models']], [(r['id'], r['name']) for r in roster])

    def test_scope_mismatch_and_invalid_ids_are_rejected(self):
        for arguments in [dict(model='FM3'), dict(firmware='13.00'), dict(model_id=-1), dict(model_id=336), dict(model_id=True)]:
            with self.subTest(arguments=arguments), self.assertRaises(ValueError):
                self.query(**({'model_id':0} | arguments))

    def test_no_invented_matches(self):
        self.assertEqual(self.query('unlisted manufacturer xyz')['matches'], [])
        for arguments in [dict(), dict(query='---'), dict(query='Fender', model_id=0)]:
            with self.subTest(arguments=arguments), self.assertRaises(ValueError):
                self.query(**arguments)


if __name__ == '__main__':
    unittest.main()
