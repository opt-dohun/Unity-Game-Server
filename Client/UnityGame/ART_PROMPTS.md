# Generated art prompts

These project assets were made with the built-in `image_gen` tool. Original PNGs in the parent `unity` folder were used as visual references. The final generated source sheets and background are saved under `Assets/Art/Source` and `Assets/Resources/Background`.

## Boss animation sheet

> Use case: stylized-concept. Asset type: production sprite sheet for a 2D Unity side-scrolling boss fight. Input image: reference for the boss character design only. Create a CLEAN 4-COLUMN BY 2-ROW animation sprite sheet of the same adorable coral-red flower-covered sea crab boss, consistent identity and size in every cell. Frame sequence left-to-right top row: idle neutral, inhale/rise slightly, both claws open with body lifted slightly, settle neutral. Bottom row: wind-up with left claw high, claw swing forward, follow-through low, recovery neutral. The crab's feet must stay on one identical baseline in every frame and its body center must stay at the center of each cell. Each cell has even padding and contains exactly one complete crab, never cropped or overlapping neighboring cells. 2D hand-painted pixel-art inspired fantasy game sprite, crisp contour, intricate coral shell and white blossoms, subtle sea blue accents. Genuinely transparent background, no scenery, no floor, no text, no labels, no borders, no grid lines, no extra objects or effects. Sprite animation production asset; coherent frame-to-frame morphology and readable motion.

## Hero animation sheet

> Use case: stylized-concept. Asset type: production sprite sheet for a 2D Unity side-scrolling action game. Input image is the hero character design reference. Create a CLEAN 4-COLUMN BY 2-ROW sequence of exactly eight separate full-body animation poses of the same red-haired female sword knight, consistent face, costume, sword and scale. Top row left to right: ready idle, breathing idle, walk contact left, walk contact right. Bottom row left to right: sword wind-up, forward slash, follow-through, shield guard. Feet remain on a consistent baseline within each row and the character center stays fixed in each cell. Give each cell ample even transparent padding; no overlapping, no cropped hair or weapon. Crisp detailed fantasy pixel-art inspired 2D game sprite matching reference. Genuinely transparent background. No ground, no captions, no labels, no grid lines, no extra characters or effects.

## Coastal arena background

> Use case: stylized-concept. Asset type: seamless-looking wide 2D game background for a Unity side-scrolling boss arena, 16:9 composition. Empty enchanted coastal ruin at dusk: layered blue mountains and sea, coral-tinted sky, pale sun, distant stone gate silhouettes, a flat broad foreground stone platform spanning the full width suitable for 2D characters. Painterly pixel-art inspired fantasy aesthetic, restrained detail behind gameplay sprites, coral red and slate blue palette matching a red flower crab boss and red-haired sword knight. No characters, no enemies, no text, no UI, no close foreground objects. Landscape background image, opaque.

## Revised motion assets

The final boss sheet (`boss-facing-left-sheet.png`) keeps all eight poses facing the knight on the left. The hero idle sheet (`hero-idle-sheet.png`) shows the knight standing with her sword in hand and only subtle breathing and hair movement. Separate transparent sheets for attack (`hero-attack-clean-sheet.png`) and guard (`hero-guard-clean-sheet.png`) keep the whole character and weapon inside each frame. An isolated impact pose (`hero-attack-impact.png`) replaces the one attack frame whose sword had crossed into a neighboring cell. `tools/slice_motion_sprites.py` extracts and aligns individual frames and removes disconnected spillover.

The six-frame forward charge source (`hero-run-wide-sheet.png`) is retained as an art source, but movement was removed from the turn-based battle. The jump uses the existing isolated knight pose. All visible battle animations swap complete sprite frames; the attack action keeps the knight's world position fixed.

The later attack correction removes nearly transparent spill below the isolated thrust pose before aligning its boots with the other attack frames. The attack timing holds the anticipation, snaps through the slash and thrust, then allows a longer recovery. The jump art remains in the project but is no longer used in the battle.

## Waterfall arena revision

Built-in image generation edited the existing arena using the supplied `images.jpeg` as a landscape reference. The final prompt was:

> Use case: stylized-concept. Asset type: finished 16:9 background for the Crimson Tide 2D boss battle. Image 1 is the waterfall landscape reference for geography, palette and mood. Image 2 is the existing arena to update; preserve its wide side-view game framing and a clear continuous flat stone fighting floor across the entire bottom 22% at the same height. Replace the coastal sunset and ruins with the reference's lush canyon: multiple luminous turquoise-white waterfalls cascading behind dark mossy boulders, pale blue sky, green foliage and deep teal rocky cliffs. Painterly fantasy pixel-art inspired finish, atmospheric depth, crisp foreground silhouettes, readable midground behind characters. The central horizontal gameplay area must remain uncluttered and not contain large foreground rocks. No characters, crab, interface, text, labels, watermark or inset frames. Opaque landscape image, cinematic 16:9.

Final workspace asset: `Assets/Resources/Background/arena-waterfalls.png`. The earlier `arena.png` remains available as the previous version.
