"""Slice the revised animation sheets into consistent, anchored Unity sprites."""
from pathlib import Path
from collections import deque
from PIL import Image

ROOT = Path(__file__).resolve().parents[1]
ART = ROOT / "Client" / "UnityGame" / "Assets" / "Art" / "Source"
OUT = ROOT / "Client" / "UnityGame" / "Assets" / "Resources" / "Sprites"
SHEETS = [
    ("boss-facing-left-sheet.png", "BossFacingLeft", "boss_left", 4, 2, 8, 440, 330),
    ("hero-idle-sheet.png", "HeroIdle", "hero_idle", 2, 2, 4, 470, 390),
    ("hero-run-wide-sheet.png", "HeroRun", "hero_run", 2, 3, 6, 730, 390),
    ("hero-attack-clean-sheet.png", "HeroAttack", "hero_attack", 2, 2, 4, 730, 390),
    ("hero-guard-clean-sheet.png", "HeroGuard", "hero_guard", 2, 2, 4, 730, 390),
]


def remove_detached_fragments(canvas: Image.Image) -> Image.Image:
    """Generated sheets sometimes leak a piece of the neighboring cell."""
    alpha = canvas.getchannel("A")
    pixels = alpha.load()
    width, height = canvas.size
    seen = set()
    components = []
    for y in range(height):
        for x in range(width):
            if pixels[x, y] < 10 or (x, y) in seen:
                continue
            queue = deque([(x, y)])
            seen.add((x, y))
            component = []
            while queue:
                px, py = queue.popleft()
                component.append((px, py))
                for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                    nx, ny = px + dx, py + dy
                    if (0 <= nx < width and 0 <= ny < height
                            and pixels[nx, ny] >= 10 and (nx, ny) not in seen):
                        seen.add((nx, ny))
                        queue.append((nx, ny))
            components.append(component)
    if components:
        largest = max(components, key=len)
        for component in components:
            if component is largest:
                continue
            for x, y in component:
                pixels[x, y] = 0
        canvas.putalpha(alpha)
    return canvas

for file, folder, prefix, columns, rows, count, max_width, max_height in SHEETS:
    sheet = Image.open(ART / file).convert("RGBA")
    output = OUT / folder
    output.mkdir(parents=True, exist_ok=True)
    for index in range(count):
        col, row = index % columns, index // columns
        left = round(col * sheet.width / columns)
        right = round((col + 1) * sheet.width / columns)
        top = round(row * sheet.height / rows)
        bottom = round((row + 1) * sheet.height / rows)
        cell = sheet.crop((left, top, right, bottom))
        alpha = cell.getchannel("A").point(lambda value: 0 if value < 10 else value)
        cell.putalpha(alpha)
        cell = remove_detached_fragments(cell)
        alpha = cell.getchannel("A")
        bounds = alpha.getbbox()
        if not bounds:
            raise ValueError(f"Empty sprite: {file} frame {index}")
        content = cell.crop(bounds)
        scale = min(max_width / content.width, max_height / content.height)
        size = (round(content.width * scale), round(content.height * scale))
        content = content.resize(size, Image.Resampling.LANCZOS)
        canvas_width = 768 if folder in ("HeroRun", "HeroAttack", "HeroGuard") else 512
        canvas = Image.new("RGBA", (canvas_width, 512))
        baseline = 492
        if folder == "HeroRun" and index in (1, 5):
            baseline -= 22
        canvas.alpha_composite(content, ((canvas_width - size[0]) // 2, baseline - size[1]))
        remove_detached_fragments(canvas).save(output / f"{prefix}_{index:02}.png")
        print(folder, index, "bounds", bounds, "size", size)

# The contact sheet's impact sword touched its cell edge. Replace only that
# frame with a separately generated, complete pose.
impact = Image.open(ART / "hero-attack-impact.png").convert("RGBA")
impact = remove_detached_fragments(impact)
bounds = impact.getchannel("A").getbbox()
if bounds is None:
    raise ValueError("Empty isolated attack impact frame")
impact = impact.crop(bounds)
scale = min(730 / impact.width, 390 / impact.height)
size = (round(impact.width * scale), round(impact.height * scale))
impact = impact.resize(size, Image.Resampling.LANCZOS)
canvas = Image.new("RGBA", (768, 512))
canvas.alpha_composite(impact, ((768 - size[0]) // 2, 492 - size[1]))
# The isolated image has transparent padding inside its nominal bounds.
# Match the visible figure height to the other attack poses after compositing.
visible = canvas.getchannel("A").point(lambda value: 255 if value >= 10 else 0).getbbox()
if visible is None:
    raise ValueError("Empty isolated attack impact after scaling")
impact = canvas.crop(visible)
scale = min(768 / impact.width, 390 / impact.height)
size = (round(impact.width * scale), round(impact.height * scale))
impact = impact.resize(size, Image.Resampling.LANCZOS)
canvas = Image.new("RGBA", (768, 512))
canvas.alpha_composite(impact, ((768 - size[0]) // 2, 492 - size[1]))
canvas.save(OUT / "HeroAttack" / "hero_attack_02.png")
print("HeroAttack 2 isolated impact", size)
