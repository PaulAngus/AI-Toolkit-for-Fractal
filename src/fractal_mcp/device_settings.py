"""Normalize operator-entered version components; never infer firmware from hardware."""
import re


def normalize_model(value):
    aliases = {'fm9': 'FM9', 'fm3': 'FM3', 'axefxiii': 'Axe-Fx III', 'axefx3': 'Axe-Fx III'}
    if isinstance(value, str):
        key = re.sub(r'[\s_-]+', '', value).casefold()
        if key in aliases:
            return aliases[key]
    raise ValueError('Choose FM9, FM3 or Axe-Fx III as the device model; uppercase and lowercase both work.')


def normalize_firmware(value):
    if not isinstance(value, str):
        raise ValueError('Enter the firmware version shown on your device, for example 12.00.')
    match = re.fullmatch(r'(?:v\s*)?(\d{1,3})\.(\d{1,2})', value.strip(), flags=re.IGNORECASE | re.ASCII)
    if not match:
        raise ValueError('Firmware needs a major and minor version, for example 12.00 or 12.0. Copy the version shown on your device; beta suffixes are not supported.')
    major, minor = (int(part) for part in match.groups())
    return f'{major}.{minor:02d}'
