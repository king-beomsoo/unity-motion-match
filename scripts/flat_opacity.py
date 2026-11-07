"""Measure group opacity from opaque interior pixels of a single-paint graphic."""
from dataclasses import dataclass
from math import ceil, floor

import cv2
import numpy as np


@dataclass(frozen=True)
class Patch:
    bounds: tuple
    sample_count: int
    rest_peak: float


def single_paint(rgba):
    """Check the compositing model RGB = paint * coverage on the actual template."""
    alpha = rgba[:, :, 3]
    active = alpha > .05
    if np.count_nonzero(alpha > .95) < 12:
        return None
    coverage = alpha[active, None]
    rgb = rgba[:, :, :3][active]
    paint = np.clip((coverage * rgb).sum(axis=0) / np.square(coverage).sum(), 0, 1)
    error = np.max(np.abs(rgb - coverage * paint), axis=1)
    if np.quantile(error, .95) > .025 or np.sqrt(np.mean(error ** 2)) > .025:
        return None
    return paint


def ink_coverage(rgb, background, paint):
    # For flat paint, (frame - BG) = group opacity * coverage * (paint - BG).
    direction = paint - background
    power = np.sum(direction ** 2, axis=2)
    numerator = np.sum((rgb - background) * direction, axis=2)
    # A matching foreground and background give no opacity information.
    # Preserve that distinction from a visible, high-contrast transparent pixel.
    return np.divide(numerator, power, out=np.full_like(power, np.nan), where=power > .01)


def strongest_mean(values, count):
    return float(np.mean(np.partition(values, -count)[-count:]))


def reference_patches(rgba, rest_coverage):
    # Allow the small registration offset accepted at the reference rest pose.
    support = cv2.dilate((rgba[:, :, 3] > .02).astype(np.uint8), np.ones((9, 9), np.uint8))
    ys, xs = np.nonzero(support)
    patches = []
    for y in range(ys.min(), ys.max() + 1, 24):
        for x in range(xs.min(), xs.max() + 1, 24):
            tile = rest_coverage[y:y + 24, x:x + 24]
            mask = (support[y:y + 24, x:x + 24] != 0) & (tile > .12)
            py, px = np.nonzero(mask)
            if px.size < 8 or np.ptp(px) < 2 or np.ptp(py) < 2:
                continue
            count = max(3, int(px.size * .05))
            peak = strongest_mean(tile[mask], count)
            if peak > .75:
                bounds = (x + px.min(), y + py.min(), x + px.max() + 1, y + py.max() + 1)
                patches.append(Patch(bounds, count, peak))
    return patches


def projected_bounds(bounds, pivot, key, margin):
    left, top, right, bottom = bounds
    px, py = pivot
    scale = key['scale']
    return (floor(px + (left - px) * scale + key['x']) - margin,
            floor(py + (top - py) * scale + key['y']) - margin,
            ceil(px + (right - px) * scale + key['x']) + margin,
            ceil(py + (bottom - py) * scale + key['y']) + margin)


def intersects(first, second):
    return (first[0] < second[2] and first[2] > second[0]
            and first[1] < second[3] and first[3] > second[1])


def opaque_peak(coverage, rgb, background, paint, patch, box):
    """Locate a native peak near an opaque rest interior and retain its pixel footprint."""
    if not .98 <= patch.rest_peak <= 1.02:
        return None
    residual = rgb - background - coverage[:, :, None] * (paint - background)
    same_paint = np.max(np.abs(residual), axis=2) <= .02
    values = coverage[same_paint]
    if values.size < patch.sample_count:
        return None
    supported = np.where(same_paint, coverage, -np.inf)
    y, x = np.unravel_index(np.argmax(supported), supported.shape)
    peak = float(supported[y, x])
    level = peak / patch.rest_peak
    if not 0 < level <= 1.02 or strongest_mean(values, patch.sample_count) < .75 * peak:
        return None
    # The two-pixel registration margins of adjacent windows can overlap.
    # Compare the actual peak neighborhoods so one stroke cannot vote twice.
    neighbors = supported[max(0, y - 2):y + 3, max(0, x - 2):x + 3]
    support = np.count_nonzero(neighbors >= peak - .01 * patch.rest_peak)
    x, y = box[0] + int(x), box[1] + int(y)
    return level, (x - 2, y - 2, x + 3, y + 3), support


def corroborated_peak(peaks):
    """Return the lower of two matching peaks with disjoint pixel neighborhoods."""
    levels = sorted(peaks, key=lambda item: item[0], reverse=True)
    for i, (lower, box, support) in enumerate(levels):
        for j in range(i - 1, -1, -1):
            upper, other, other_support = levels[j]
            if upper - lower > .01:
                break
            if max(support, other_support) >= 2 and not intersects(box, other):
                return lower
    return None


def refine_flat_opacity(files, read_rgb, background, elements, tracks, diagnostics, read_rgba):
    rest = read_rgb(files[-1])
    pivots = [(e['pivotX'], e['pivotY']) for e in elements]
    candidates = {}
    identity = {'x': 0, 'y': 0, 'scale': 1}
    for index, element in enumerate(elements):
        rgba = read_rgba(element)
        paint = single_paint(rgba)
        if paint is None:
            continue
        patches = reference_patches(rgba, ink_coverage(rest, background, paint))
        # Overlap in the rest frame would contaminate the calibration itself.
        other_bounds = [item['sourceBounds'] for j, item in enumerate(diagnostics) if j != index]
        patches = [p for p in patches if not any(
            intersects(projected_bounds(p.bounds, pivots[index], identity, 2), other) for other in other_bounds)]
        if len(patches) >= 3:
            candidates[index] = (paint, patches)
            diagnostics[index]['flatPaintOpacity'] = {
                'patches': len(patches), 'measuredFrames': 0, 'geometryFallbackFrames': 0,
                'corroboratedPeaks': []}
    if not candidates:
        return

    height, width = background.shape[:2]
    # Use original fitted visibility throughout so element order cannot affect exclusions.
    visibility = [[k['alpha'] > .012 for k in track['keys']] for track in tracks]
    for frame_index, file in enumerate(files):
        active = [i for i in candidates if frame_index < diagnostics[i]['staticTailStart']]
        if not active:
            continue
        rgb = read_rgb(file)
        bounds = [projected_bounds(item['sourceBounds'], pivot, track['keys'][frame_index], 2)
                  for item, pivot, track in zip(diagnostics, pivots, tracks)]
        for index in active:
            paint, patches = candidates[index]
            key = tracks[index]['keys'][frame_index]
            others = [box for j, box in enumerate(bounds) if j != index and visibility[j][frame_index]]
            ratios = []
            peaks = []
            for patch in patches:
                box = projected_bounds(patch.bounds, pivots[index], key, 2)
                if any(intersects(box, other) for other in others):
                    continue
                x, y, right, bottom = box
                if x < 0 or y < 0 or right > width or bottom > height:
                    continue
                region = np.s_[y:bottom, x:right]
                coverage = ink_coverage(rgb[region], background[region], paint)
                finite = coverage[np.isfinite(coverage)]
                if finite.size < patch.sample_count:
                    continue
                ratios.append(strongest_mean(finite, patch.sample_count) / patch.rest_peak)
                # For nearly opaque rest interiors, native coverage is at most one.
                # At a subpixel pose it may drop in many letter strokes at once.
                # Look for surviving interiors of the same paint in separate patches.
                peak = opaque_peak(coverage, rgb[region], background[region], paint, patch, box)
                if peak is not None:
                    peaks.append(peak)
            stats = diagnostics[index]['flatPaintOpacity']
            if len(ratios) < max(3, ceil(len(patches) / 2)):
                stats['geometryFallbackFrames'] += 1
                continue
            estimate = float(np.median(ratios))
            # Require agreement from independent areas before replacing the image fit.
            if np.median(np.abs(np.asarray(ratios) - estimate)) > .12:
                stats['geometryFallbackFrames'] += 1
                continue
            witness = corroborated_peak(peaks)
            if witness is not None and witness > estimate + .01:
                stats['corroboratedPeaks'].append(dict(frame=frame_index, median=estimate, level=witness))
                estimate = witness
            key['alpha'] = float(np.clip(estimate, 0, 1))
            stats['measuredFrames'] += 1
