# Crimson Tide — Unity project

Open this folder with Unity Editor 6000.6.3f1. The playable scene is `Assets/Scenes/BossBattle.unity`.

The generated art sources are in `Assets/Art/Source`; the normalized animation PNGs are in `Assets/Resources/Sprites`. To regenerate animation frames after replacing a source sheet, run `uv run --with pillow python tools/slice_generated_sprites.py` and `uv run --with pillow python tools/slice_motion_sprites.py` from the project root (`../..`). The custom Unity menu `Crimson Tide > Create Boss Battle Scene` reimports sprite settings and recreates the scene. `Crimson Tide > Build macOS Game` produces `Builds/CrimsonTide-TurnBased.app`.

Turn choices: 1/J attacks in place for server-calculated 1–20 damage, 2/K guards to reduce the next boss hit by 80%. Enter starts or restarts. Start the C# server before playing.

Victory results show turns instead of elapsed time. The register and ranking screens connect to the local C# and SQLite server at `127.0.0.1:8001`; run it from the project root as described in the main README.
