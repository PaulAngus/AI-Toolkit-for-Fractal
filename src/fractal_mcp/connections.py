"""Resolve human-readable MIDI connections without opening ports or probing a device."""
import re
from .device_settings import normalize_model

MODEL_NAMES = {
    'FM9': r'\bFM9\b',
    'FM3': r'\bFM3\b',
    'Axe-Fx III': r'\bAxe[\s_-]*Fx[\s_-]*(?:III|3)\b',
}


def connection_name(name):
    # USB drivers commonly append direction to one shared connection name.
    return re.sub(r'\s+(?:in|out|input|output)$', '', name, flags=re.IGNORECASE).strip()


def resolve_connection(ports, model, connection='Automatic'):
    model = normalize_model(model)
    if not isinstance(connection, str) or not connection.strip():
        raise ValueError('Choose Automatic or enter the MIDI connection name.')
    automatic = connection.strip().casefold() == 'automatic'
    selected = {}
    for direction in ('inputs', 'outputs'):
        roster = ports.get(direction) if isinstance(ports, dict) else None
        if not isinstance(roster, list):
            raise ValueError('Could not read the available MIDI connections.')
        matches = []
        for port in roster:
            if not isinstance(port, dict) or set(port) != {'index', 'name'} or type(port['index']) is not int or not 0 <= port['index'] <= 65535 or not isinstance(port['name'], str):
                raise ValueError('Invalid MIDI connection listing.')
            name = port['name']
            match = re.search(MODEL_NAMES[model], name, flags=re.IGNORECASE) if automatic else connection.strip().casefold() in (name.casefold(), connection_name(name).casefold())
            if match:
                matches.append(port)
        if len(matches) != 1:
            if not matches:
                raise ValueError(f'No {model if automatic else connection} MIDI {"receive" if direction == "inputs" else "send"} connection found. Connect the device by USB, or enter the name of your MIDI interface in Configure.')
            names = ', '.join(sorted({connection_name(port['name']) for port in matches}))
            raise ValueError(f'More than one matching MIDI connection: {names}. Enter the intended connection name in Configure; duplicate names must be made distinct or disconnected.')
        selected[direction] = matches[0]
    if automatic and connection_name(selected['inputs']['name']).casefold() != connection_name(selected['outputs']['name']).casefold():
        raise ValueError('Receive and send connections do not identify the same device. Enter the intended connection name in Configure.')
    return {'input': selected['inputs']['index'], 'output': selected['outputs']['index']}
