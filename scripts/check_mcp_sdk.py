#!/usr/bin/env python3
"""Optional official Python MCP SDK interoperability check. Install the SDK only in a test environment."""
import argparse
import asyncio
import importlib.metadata
from pathlib import Path
import sys

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / 'src'))
from fractal_mcp.runtime import load_config


async def check(config):
    # The SDK is deliberately not a toolkit runtime/build dependency.
    from mcp import ClientSession, StdioServerParameters
    from mcp.client.stdio import stdio_client
    configuration = load_config(config)
    if configuration['allow_live_reads'] or configuration['allow_writes']:
        raise ValueError('SDK interoperability checks require offline-only configuration.')
    params = StdioServerParameters(command=sys.executable, args=['-X', 'utf8', str(ROOT / 'scripts/fractal_mcp.py'), '--config', str(config.resolve())])
    async with stdio_client(params) as (read, write):
        async with ClientSession(read, write) as client:
            initialized = await client.initialize()
            assert initialized.protocolVersion == '2025-11-25'
            tools = await client.list_tools()
            assert any(tool.name == 'fractal_execute_review' for tool in tools.tools)
            from jsonschema import Draft202012Validator
            for tool in tools.tools:
                Draft202012Validator.check_schema(tool.inputSchema)
            status = await client.call_tool('fractal_status', {})
            assert not status.isError and status.structuredContent['ports_opened'] is False
            lookup = await client.call_tool('fractal_search_knowledge', {'query': 'Scene MIDI recovery'})
            assert not lookup.isError and lookup.structuredContent['sources']
            resources = await client.list_resources()
            reference = next(resource for resource in resources.resources if str(resource.uri).endswith('reference-map.md'))
            contents = await client.read_resource(reference.uri)
            assert contents.contents
            refused = await client.call_tool('fractal_inspect_device', {})
            assert refused.isError
            print(f'Official MCP Python SDK {importlib.metadata.version("mcp")}: initialization, {len(tools.tools)} tool schemas, structured calls, resources and disabled-live refusal passed; no MIDI ports opened.')


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--config', type=Path, required=True)
    asyncio.run(check(parser.parse_args().config))
