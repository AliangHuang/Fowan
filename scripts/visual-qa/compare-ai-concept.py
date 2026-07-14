#!/usr/bin/env python3
"""Generate deterministic AI concept parity artifacts without distorting aspect ratio."""

from __future__ import annotations

import argparse
import json
from pathlib import Path

from PIL import Image, ImageChops, ImageEnhance, ImageStat


REGIONS = {
    "titlebar": (0.0, 0.0, 1.0, 0.0712),
    "sidebar": (0.0, 0.0712, 0.22, 1.0),
    "toolbar": (0.22, 0.0712, 1.0, 0.18),
    "content": (0.22, 0.18, 1.0, 0.82),
    "bottom": (0.22, 0.82, 1.0, 1.0),
}


def contain(image: Image.Image, size: tuple[int, int]) -> Image.Image:
    scale = min(size[0] / image.width, size[1] / image.height)
    scaled = image.resize((round(image.width * scale), round(image.height * scale)), Image.Resampling.LANCZOS)
    canvas = Image.new("RGB", size, "white")
    canvas.paste(scaled, ((size[0] - scaled.width) // 2, (size[1] - scaled.height) // 2))
    return canvas


def box_for(region: tuple[float, float, float, float], size: tuple[int, int]) -> tuple[int, int, int, int]:
    return tuple(round(value * (size[index % 2])) for index, value in enumerate(region))  # type: ignore[return-value]


def average(image: Image.Image) -> list[float]:
    return [round(value, 2) for value in ImageStat.Stat(image.convert("RGB")).mean]


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--concept", required=True)
    parser.add_argument("--actual", required=True)
    parser.add_argument("--output-root", required=True)
    parser.add_argument("--sidebar-x", required=True, type=int)
    args = parser.parse_args()

    output = Path(args.output_root)
    crops = output / "crops"
    output.mkdir(parents=True, exist_ok=True)
    crops.mkdir(parents=True, exist_ok=True)
    actual = Image.open(args.actual).convert("RGB")
    concept = contain(Image.open(args.concept).convert("RGB"), actual.size)
    overlay = Image.blend(concept, actual, 0.5)
    overlay.save(output / "overlay-50.png")
    concept.save(output / "concept-normalized.png")
    actual.save(output / "actual.png")
    concept.save(output / "blink.gif", save_all=True, append_images=[actual], duration=[550, 550], loop=0)
    diff = ImageChops.difference(concept, actual)
    ImageEnhance.Contrast(diff).enhance(2.5).save(output / "heatmap.png")

    region_report: dict[str, object] = {}
    for name, relative in REGIONS.items():
        box = box_for(relative, actual.size)
        left = concept.crop(box)
        right = actual.crop(box)
        strip = Image.new("RGB", (left.width * 2, left.height))
        strip.paste(left, (0, 0))
        strip.paste(right, (left.width, 0))
        strip.save(crops / f"{name}.png")
        c_avg = average(left)
        a_avg = average(right)
        region_report[name] = {
            "box": box,
            "concept_average_rgb": c_avg,
            "actual_average_rgb": a_avg,
            "channel_delta": [round(a_avg[i] - c_avg[i], 2) for i in range(3)],
        }

    expected_sidebar = 350 if "chat" in Path(args.actual).name else 315
    report = {
        "canvas": list(actual.size),
        "normalization": "uniform-contain-no-axis-stretch",
        "sidebar": {
            "expected_x": expected_sidebar,
            "actual_x": args.sidebar_x,
            "absolute_error_px": abs(expected_sidebar - args.sidebar_x),
            "passes_2px": abs(expected_sidebar - args.sidebar_x) <= 2,
        },
        "regions": region_report,
        "artifacts": ["overlay-50.png", "blink.gif", "heatmap.png", "crops/"],
    }
    (output / "report.json").write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")


if __name__ == "__main__":
    main()
