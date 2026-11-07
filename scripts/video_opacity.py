"""Estimate flat-paint opacity from decoded video with ringing at hard edges."""
import cv2
import numpy as np
from scipy.optimize import least_squares

from flat_opacity import ink_coverage, projected_bounds, single_paint


def plateau(coverage, opaque_area):
    """Estimate the level of an opaque stroke without using its brightest pixel."""
    values = coverage[np.isfinite(coverage)]
    count = round(opaque_area)
    if count < 12 or len(values) < count:
        return None
    bright = np.sort(np.partition(values, -count)[-count:])[::-1]
    near_top = bright[int(.025 * count)]
    shoulder = bright[bright >= .82 * near_top]
    if not len(shoulder):
        return None
    center = bright[count // 3]
    if len(shoulder) >= .1 * count:
        center = (center + np.median(shoulder)) / 2
    # The range of the observed stroke constrains any temporal correction.
    return np.clip([center, bright[int(.85 * count)], bright[int(.05 * count)]], 0, 1)


def temporal_levels(measurements, fps):
    """Fit a short robust local curve within the levels supported by each frame."""
    values = measurements[:, 0]
    result = values.copy()
    radius = max(2, round(fps * .2))
    for i, (level, low, high) in enumerate(measurements):
        if not np.isfinite(level):
            continue
        start, stop = i, i + 1
        # No interpolation across missing observations, cuts or a brief flash.
        while start > max(0, i-radius) and abs(values[start]-values[start-1]) < .20:
            start -= 1
        while stop < min(len(values), i+radius+1) and abs(values[stop]-values[stop-1]) < .20:
            stop += 1
        if stop-start < 5:
            continue
        time = np.arange(start-i, stop-i, dtype=float) / radius
        basis = np.column_stack((np.ones_like(time), time, time*time))
        observed = values[start:stop]
        seed = np.linalg.lstsq(basis, observed, rcond=None)[0]
        def residual(coef):
            return np.clip(basis @ coef, 0, 1) - observed
        fit = least_squares(residual, seed, loss='soft_l1', f_scale=.012)
        result[i] = np.clip(fit.x[0], max(0, low-.015), min(1, high+.015))
    return result


def refine_video_opacity(files, read_rgb, background, elements, tracks, diagnostics, read_rgba, fps):
    candidates = {}
    silhouettes = []
    for i, (element, info) in enumerate(zip(elements, diagnostics)):
        rgba = read_rgba(element)
        silhouettes.append((rgba[:, :, 3] > .02).astype(np.uint8))
        if 'flatPaintOpacity' not in info:
            continue
        paint = single_paint(rgba)
        yy, xx = np.nonzero(rgba[:, :, 3] > .98)
        if paint is not None and len(xx) >= 12:
            candidates[i] = (paint, xx, yy, np.full((len(files), 3), np.nan))
    if not candidates:
        return

    for frame, path in enumerate(files):
        image = read_rgb(path)
        boxes = [projected_bounds(info['sourceBounds'], (el['pivotX'], el['pivotY']),
                                  track['keys'][frame], 2)
                 for el, track, info in zip(elements, tracks, diagnostics)]
        height, width = image.shape[:2]
        for i, (paint, xx, yy, samples) in candidates.items():
            x, y, r, b = boxes[i]
            if x < 0 or y < 0 or r > width or b > height:
                continue
            region = np.s_[y:b, x:r]
            coverage = ink_coverage(image[region], background[region], paint)
            for j, (left, top, right, bottom) in enumerate(boxes):
                if j == i or left >= r or top >= b or right <= x or bottom <= y:
                    continue
                # Exclude the neighboring graphic, including its uncertain edges.
                # Empty space in its bounding rectangle can contain useful strokes.
                neighbor = tracks[j]['keys'][frame]
                scale = neighbor['scale']
                dx = (1-scale)*elements[j]['pivotX'] + neighbor['x']-x
                dy = (1-scale)*elements[j]['pivotY'] + neighbor['y']-y
                transform = np.float32([[scale, 0, dx], [0, scale, dy]])
                mask = cv2.warpAffine(silhouettes[j], transform, (r-x, b-y), flags=cv2.INTER_NEAREST)
                coverage[cv2.dilate(mask, np.ones((9, 9), np.uint8)) != 0] = np.nan
            key = tracks[i]['keys'][frame]
            el = elements[i]
            px = np.rint(el['pivotX'] + (xx-el['pivotX'])*key['scale'] + key['x']).astype(int)-x
            py = np.rint(el['pivotY'] + (yy-el['pivotY'])*key['scale'] + key['y']).astype(int)-y
            inside = (px >= 0) & (py >= 0) & (px < r-x) & (py < b-y)
            visible = np.count_nonzero(np.isfinite(coverage[py[inside], px[inside]]))
            if visible < .8 * len(xx):
                continue
            value = plateau(coverage, visible * key['scale']**2)
            if value is not None:
                samples[frame] = value

    for i, (_, _, _, samples) in candidates.items():
        if not np.isfinite(samples[-1, 0]) or samples[-1, 0] < .9:
            continue
        tail = diagnostics[i]['staticTailStart']
        samples[tail:] = 1   # calibrated rest, same boundary as the geometry fit
        estimates = temporal_levels(samples, fps)
        for key, alpha in zip(tracks[i]['keys'][:tail], estimates[:tail]):
            if np.isfinite(alpha):
                key['alpha'] = float(alpha)
        diagnostics[i]['videoOpacity'] = dict(measuredFrames=int(np.isfinite(estimates[:tail]).sum()),
                                               restStartsAt=tail)
