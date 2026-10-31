"""The small JSON Schema subset used by this adapter; no external dependencies."""
import re
import math


def string(description='', **kw):
    return {'type': 'string', 'minLength': 1, 'maxLength': 512, 'description': description, **kw}


def integer(low, high, description=''):
    return {'type': 'integer', 'minimum': low, 'maximum': high, 'description': description}


def array(items, maximum=1024):
    return {'type': 'array', 'items': items, 'minItems': 1, 'maxItems': maximum, 'uniqueItems': True}


def obj(properties, required=None):
    return {'type': 'object', 'properties': properties, 'required': list(properties) if required is None else required, 'additionalProperties': False}


ID = string('Opaque artifact or review ID returned by a tool.', pattern='^[a-f0-9]{32}$')
HASH = string('Exact SHA-256 of the reviewed bytes.', pattern='^[a-f0-9]{64}$')
MODEL = string(enum=['FM9', 'FM3', 'Axe-Fx III'])
FIRMWARE = string(pattern=r'^\d{1,3}\.\d{2}$')
SLOTS = array(integer(0, 1023))
BOOL = {'type': 'boolean'}


def validate(schema, value, label='arguments'):
    types = {'object': dict, 'array': list, 'string': str, 'integer': int, 'boolean': bool}
    if (schema['type'] == 'number' and (type(value) not in (int, float) or not math.isfinite(value))) or (schema['type'] != 'number' and type(value) is not types[schema['type']]):
        raise ValueError(f'{label}: expected {schema["type"]}.')
    if 'enum' in schema and value not in schema['enum']:
        raise ValueError(f'{label}: choose one of {schema["enum"]}.')
    if isinstance(value, dict):
        if set(schema['required']) - value.keys():
            raise ValueError(f'{label}: missing {sorted(set(schema["required"]) - value.keys())}.')
        if value.keys() - schema['properties'].keys():
            raise ValueError(f'{label}: unknown fields {sorted(value.keys() - schema["properties"].keys())}.')
        for key, item in value.items():
            validate(schema['properties'][key], item, f'{label}.{key}')
    elif isinstance(value, list):
        if not schema['minItems'] <= len(value) <= schema['maxItems']:
            raise ValueError(f'{label}: invalid number of items.')
        if schema.get('uniqueItems') and any(value[i] in value[:i] for i in range(len(value))):
            raise ValueError(f'{label}: duplicate items.')
        for item in value:
            validate(schema['items'], item, label)
    elif type(value) in (int, float):
        if not schema['minimum'] <= value <= schema['maximum']:
            raise ValueError(f'{label}: outside supported range.')
    elif isinstance(value, str):
        if not schema['minLength'] <= len(value) <= schema['maxLength'] or ('pattern' in schema and re.fullmatch(schema['pattern'], value) is None):
            raise ValueError(f'{label}: invalid string.')
