#!/usr/bin/env python3
"""Repository entry point; the portable skill contains the one maintained planner."""
from pathlib import Path
import runpy

if __name__ == "__main__":
    runpy.run_path(str(Path(__file__).resolve().parents[1] / "skills/fractal-fm9/scripts/plan_scene_midi.py"), run_name="__main__")
