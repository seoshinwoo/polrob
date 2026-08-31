#!/usr/bin/env python3
"""Rebuild robber run frames so their palette/alpha matches the idle sprite."""

from collections import deque
from pathlib import Path

from PIL import Image


ASSET_DIR = Path(__file__).resolve().parents[1] / "polrob.Client" / "Resources" / "Raw"


def is_eye_fill(pixel: tuple[int, int, int, int]) -> bool:
    red, green, blue, alpha = pixel
    return (
        alpha >= 32
        and max(red, green, blue) - min(red, green, blue) <= 20
        and (red + green + blue) / 3 > 32
    )


def find_eye_components(image: Image.Image) -> list[list[tuple[int, int]]]:
    candidates = {
        (x, y)
        for y in range(235, 350)
        for x in range(130, 500)
        if is_eye_fill(image.getpixel((x, y)))
    }
    components: list[list[tuple[int, int]]] = []

    while candidates:
        first = candidates.pop()
        queue = deque([first])
        component = [first]
        while queue:
            x, y = queue.popleft()
            for neighbor in ((x - 1, y), (x + 1, y), (x, y - 1), (x, y + 1)):
                if neighbor in candidates:
                    candidates.remove(neighbor)
                    queue.append(neighbor)
                    component.append(neighbor)

        left = min(x for x, _ in component)
        right = max(x for x, _ in component)
        top = min(y for _, y in component)
        bottom = max(y for _, y in component)
        width = right - left + 1
        height = bottom - top + 1
        if 1_500 <= len(component) <= 3_500 and 70 <= width <= 105 and 35 <= height <= 65:
            components.append(component)

    return sorted(components, key=len, reverse=True)[:2]


def match_idle_neutral_palette(pixel: tuple[int, int, int, int]) -> tuple[int, int, int, int]:
    red, green, blue, alpha = pixel
    if alpha < 32 or max(red, green, blue) - min(red, green, blue) > 18:
        return pixel

    luminance = (red + green + blue) / 3
    if not 45 <= luminance <= 140:
        return pixel

    # The generated run art used a lighter charcoal than the idle character.
    # Compress only neutral midtones; warm skin/glow and black outlines stay intact.
    target = round(36 + (luminance - 36) * 0.65)
    delta = target - luminance
    return (
        max(0, min(255, round(red + delta))),
        max(0, min(255, round(green + delta))),
        max(0, min(255, round(blue + delta))),
        alpha,
    )


def rebuild_frame(path: Path) -> None:
    image = Image.open(path).convert("RGBA")
    eye_components = find_eye_components(image)
    if not eye_components:
        # Transparent eye openings mean this frame has already been rebuilt.
        return
    if len(eye_components) != 2:
        raise RuntimeError(f"Expected two eye fills in {path.name}, found {len(eye_components)}")

    pixels = image.load()
    for y in range(image.height):
        for x in range(image.width):
            pixels[x, y] = match_idle_neutral_palette(pixels[x, y])

    # Idle eyes are transparent openings. Rebuild the run-frame eyes with the
    # same real alpha rather than an opaque gray fill that changes character color.
    for component in eye_components:
        for x, y in component:
            red, green, blue, _ = pixels[x, y]
            pixels[x, y] = red, green, blue, 0

    image.save(path, optimize=True)


def main() -> None:
    for frame_number in range(1, 9):
        rebuild_frame(ASSET_DIR / f"char_robber_run_{frame_number}.png")


if __name__ == "__main__":
    main()
