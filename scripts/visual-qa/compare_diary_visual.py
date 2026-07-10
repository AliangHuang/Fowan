#!/usr/bin/env python3
"""Generate lightweight visual QA artifacts for the Fowan Diary shell."""

from __future__ import annotations

import argparse
import json
from pathlib import Path

from PIL import Image, ImageChops, ImageEnhance, ImageFilter, ImageOps


REGIONS = {
    "sidebar": (0.000, 0.000, 0.168, 1.000),
    "main_header": (0.185, 0.055, 0.670, 0.150),
    "metric_strip": (0.185, 0.150, 0.670, 0.232),
    "editor": (0.185, 0.235, 0.670, 0.450),
    "timeline": (0.185, 0.465, 0.670, 0.930),
    "detail_header": (0.705, 0.060, 0.980, 0.155),
    "meta_card": (0.705, 0.165, 0.980, 0.380),
    "calendar": (0.705, 0.385, 0.980, 0.675),
    "todo": (0.705, 0.680, 0.980, 0.870),
    "detail_actions": (0.705, 0.870, 0.980, 0.965),
}

TAG_REGIONS = {
    "timeline_tags": (0.250, 0.565, 0.380, 0.785),
    "meta_tags": (0.775, 0.310, 0.905, 0.375),
    "todo_pills": (0.910, 0.710, 0.975, 0.865),
}


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser()
    parser.add_argument("--concept", required=True, type=Path)
    parser.add_argument("--actual", required=True, type=Path)
    parser.add_argument("--output-root", required=True, type=Path)
    return parser.parse_args()


def luminance(pixel: tuple[int, int, int]) -> float:
    return pixel[0] * 0.299 + pixel[1] * 0.587 + pixel[2] * 0.114


def average_color(image: Image.Image, box: tuple[int, int, int, int]) -> dict[str, int]:
    crop = image.crop(box).convert("RGB")
    pixels = list(crop.getdata())
    total = max(1, len(pixels))
    r = sum(pixel[0] for pixel in pixels) // total
    g = sum(pixel[1] for pixel in pixels) // total
    b = sum(pixel[2] for pixel in pixels) // total
    return {"r": r, "g": g, "b": b, "hex": f"#{r:02X}{g:02X}{b:02X}"}


def color_delta(concept_color: dict[str, int], actual_color: dict[str, int]) -> dict[str, float | int]:
    red = int(actual_color["r"]) - int(concept_color["r"])
    green = int(actual_color["g"]) - int(concept_color["g"])
    blue = int(actual_color["b"]) - int(concept_color["b"])
    euclidean = (red * red + green * green + blue * blue) ** 0.5
    return {
        "r": red,
        "g": green,
        "b": blue,
        "euclidean": round(euclidean, 2),
    }


def scaled_box(region: tuple[float, float, float, float], size: tuple[int, int]) -> tuple[int, int, int, int]:
    width, height = size
    left, top, right, bottom = region
    return (
        max(0, min(width, round(left * width))),
        max(0, min(height, round(top * height))),
        max(0, min(width, round(right * width))),
        max(0, min(height, round(bottom * height))),
    )


def edge_energy(image: Image.Image, axis: str, step: int = 3) -> list[float]:
    rgb = image.convert("RGB")
    width, height = rgb.size
    pixels = rgb.load()
    if axis == "x":
        values: list[float] = []
        for x in range(width - 1):
            total = 0.0
            count = 0
            for y in range(8, height - 8, step):
                a = pixels[x, y]
                b = pixels[x + 1, y]
                total += (abs(a[0] - b[0]) + abs(a[1] - b[1]) + abs(a[2] - b[2])) / 3
                count += 1
            values.append(total / max(1, count))
        return values

    values = []
    for y in range(height - 1):
        total = 0.0
        count = 0
        for x in range(8, width - 8, step):
            a = pixels[x, y]
            b = pixels[x, y + 1]
            total += (abs(a[0] - b[0]) + abs(a[1] - b[1]) + abs(a[2] - b[2])) / 3
            count += 1
        values.append(total / max(1, count))
    return values


def peaks(values: list[float], min_gap: int, count: int, start: int, end: int) -> list[dict[str, float]]:
    candidates = sorted(((values[i], i) for i in range(start, min(end, len(values)))), reverse=True)
    selected: list[tuple[float, int]] = []
    for value, index in candidates:
        if all(abs(index - existing) >= min_gap for _, existing in selected):
            selected.append((value, index))
        if len(selected) >= count:
            break
    return [{"position": index, "energy": round(value, 2)} for value, index in sorted(selected, key=lambda item: item[1])]


def diff_hotspots(diff: Image.Image, cols: int = 12, rows: int = 8) -> list[dict[str, float | int]]:
    gray = ImageOps.grayscale(diff)
    width, height = gray.size
    pixels = gray.load()
    cells = []
    for row in range(rows):
        for col in range(cols):
            left = round(col * width / cols)
            top = round(row * height / rows)
            right = round((col + 1) * width / cols)
            bottom = round((row + 1) * height / rows)
            total = 0
            count = 0
            for y in range(top, bottom, 4):
                for x in range(left, right, 4):
                    total += pixels[x, y]
                    count += 1
            cells.append(
                {
                    "col": col,
                    "row": row,
                    "mean_delta": round(total / max(1, count), 2),
                    "box": [left, top, right, bottom],
                }
            )
    return sorted(cells, key=lambda cell: cell["mean_delta"], reverse=True)[:12]


def save_region_crops(
    concept: Image.Image,
    actual: Image.Image,
    output_root: Path,
    regions: dict[str, tuple[float, float, float, float]],
    directory_name: str,
) -> dict[str, dict[str, object]]:
    crop_root = output_root / directory_name
    crop_root.mkdir(parents=True, exist_ok=True)
    result: dict[str, dict[str, object]] = {}
    concept_scaled = concept.resize(actual.size, Image.Resampling.LANCZOS)
    for name, region in regions.items():
        box = scaled_box(region, actual.size)
        concept_crop = concept_scaled.crop(box)
        actual_crop = actual.crop(box)
        side_by_side = Image.new("RGB", (concept_crop.width + actual_crop.width + 8, max(concept_crop.height, actual_crop.height)), "#0B1118")
        side_by_side.paste(concept_crop, (0, 0))
        side_by_side.paste(actual_crop, (concept_crop.width + 8, 0))
        path = crop_root / f"{name}.png"
        side_by_side.save(path)
        concept_average = average_color(concept_scaled, box)
        actual_average = average_color(actual, box)
        result[name] = {
            "box": list(box),
            "concept_average": concept_average,
            "actual_average": actual_average,
            "average_delta": color_delta(concept_average, actual_average),
            "side_by_side": str(path),
        }
    return result


def main() -> int:
    args = parse_args()
    args.output_root.mkdir(parents=True, exist_ok=True)

    concept = Image.open(args.concept).convert("RGB")
    actual = Image.open(args.actual).convert("RGB")
    concept_scaled = concept.resize(actual.size, Image.Resampling.LANCZOS)

    diff = ImageChops.difference(concept_scaled, actual)
    heatmap = ImageOps.grayscale(diff)
    heatmap = ImageEnhance.Contrast(heatmap).enhance(3.0).filter(ImageFilter.GaussianBlur(radius=0.6))
    heatmap = ImageOps.colorize(heatmap, black="#071019", white="#FF3B30", mid="#2F80FF")
    heatmap_path = args.output_root / "diary_visual_diff_heatmap.png"
    heatmap.save(heatmap_path)

    regions = save_region_crops(concept, actual, args.output_root, REGIONS, "crops")
    tag_regions = save_region_crops(concept, actual, args.output_root, TAG_REGIONS, "tag-crops")
    x_edges_actual = peaks(edge_energy(actual, "x"), min_gap=25, count=12, start=20, end=actual.width - 20)
    y_edges_actual = peaks(edge_energy(actual, "y"), min_gap=20, count=16, start=20, end=actual.height - 20)
    x_edges_concept = peaks(edge_energy(concept_scaled, "x"), min_gap=25, count=12, start=20, end=actual.width - 20)
    y_edges_concept = peaks(edge_energy(concept_scaled, "y"), min_gap=20, count=16, start=20, end=actual.height - 20)

    report = {
        "concept": str(args.concept),
        "actual": str(args.actual),
        "image_sizes": {
            "concept": list(concept.size),
            "concept_scaled": list(concept_scaled.size),
            "actual": list(actual.size),
        },
        "column_boundaries": {
            "concept_scaled_vertical_edge_peaks": x_edges_concept,
            "actual_vertical_edge_peaks": x_edges_actual,
        },
        "horizontal_edges": {
            "concept_scaled_horizontal_edge_peaks": y_edges_concept,
            "actual_horizontal_edge_peaks": y_edges_actual,
        },
        "major_card_boxes": regions,
        "tag_pill_boxes": tag_regions,
        "region_color_deltas": sorted(
            [
                {
                    "region": name,
                    "concept": values["concept_average"]["hex"],
                    "actual": values["actual_average"]["hex"],
                    "delta": values["average_delta"],
                }
                for name, values in regions.items()
            ],
            key=lambda item: item["delta"]["euclidean"],
            reverse=True,
        ),
        "diff_hotspots": diff_hotspots(diff),
        "outputs": {
            "heatmap": str(heatmap_path),
            "crops_dir": str(args.output_root / "crops"),
            "tag_crops_dir": str(args.output_root / "tag-crops"),
        },
    }

    report_path = args.output_root / "diary_visual_report.json"
    report_path.write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
    print(f"Visual QA report: {report_path}")
    print(f"Diff heatmap: {heatmap_path}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
