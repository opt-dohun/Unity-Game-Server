"""Turn generated contact sheets into equal-sized, foot-anchored Unity sprites."""
from pathlib import Path
from PIL import Image

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / "Client" / "UnityGame" / "Assets" / "Resources" / "Sprites"
SOURCES = {
    "Boss": ROOT / "Client" / "UnityGame" / "Assets" / "Art" / "Source" / "boss-generated-sheet.png",
    "Hero": ROOT / "Client" / "UnityGame" / "Assets" / "Art" / "Source" / "hero-generated-sheet.png",
}

for name, source in SOURCES.items():
    sheet = Image.open(source).convert("RGBA")
    target = OUT / name
    target.mkdir(parents=True, exist_ok=True)
    for index in range(8):
        col, row = index % 4, index // 4
        left = round(col * sheet.width / 4)
        right = round((col + 1) * sheet.width / 4)
        top = round(row * sheet.height / 2)
        bottom = round((row + 1) * sheet.height / 2)
        cell = sheet.crop((left, top, right, bottom))
        alpha = cell.getchannel("A")
        # Generated transparent pixels can contain stray color; remove the fringe.
        alpha = alpha.point(lambda value: 0 if value < 10 else value)
        cell.putalpha(alpha)
        bounds = alpha.getbbox()
        if bounds is None:
            raise ValueError(f"Empty frame: {name} {index}")
        canvas = Image.new("RGBA", (512, 512))
        x = (512 - cell.width) // 2
        y = 492 - bounds[3]
        canvas.alpha_composite(cell, (x, y))
        canvas.save(target / f"{name.lower()}_{index:02}.png")
        print(name, index, "source bounds", bounds, "offset", (x, y))
