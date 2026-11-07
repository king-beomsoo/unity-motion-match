"""Distinguish an unobservable opacity from a measurable zero opacity."""
import sys
import unittest
from pathlib import Path

import numpy as np

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'scripts'))
from flat_opacity import refine_flat_opacity


class FlatOpacityTest(unittest.TestCase):
    def sample_moved_paint(self, same_color, opacity):
        paint = np.array([.8, .55, .3], np.float32)
        background = np.full((40, 256, 3), .15, np.float32)
        if same_color:
            background[:, 128:] = paint
        coverage = np.zeros(background.shape[:2], np.float32)
        for x in range(5, 110, 24):
            coverage[9:21, x:x + 9] = 1
        mask = coverage[:, :, None]
        template = np.dstack((mask * paint, coverage))
        rest = background + mask * (paint - background)
        moved = background + opacity * np.roll(mask, 128, axis=1) * (paint - background)
        keys = [dict(x=128, y=0, scale=1, alpha=.31), dict(x=0, y=0, scale=1, alpha=1)]
        diagnostics = [dict(staticTailStart=1, sourceBounds=[0, 0, 128, 40])]
        refine_flat_opacity([0, 1], [moved, rest].__getitem__, background,
                            [dict(pivotX=0, pivotY=0)], [dict(keys=keys)], diagnostics,
                            lambda _: template)
        return keys[0]['alpha'], diagnostics[0]['flatPaintOpacity']

    def test_matching_background_preserves_fallback(self):
        alpha, stats = self.sample_moved_paint(same_color=True, opacity=.47)
        self.assertEqual(alpha, .31)
        self.assertEqual(stats['geometryFallbackFrames'], 1)
        self.assertEqual(stats['measuredFrames'], 0)

    def test_contrasting_background_measures_zero_and_partial_opacity(self):
        for expected in (0, .47):
            with self.subTest(opacity=expected):
                alpha, stats = self.sample_moved_paint(same_color=False, opacity=expected)
                self.assertAlmostEqual(alpha, expected, places=5)
                self.assertEqual(stats['measuredFrames'], 1)

    def test_partial_strokes_and_bright_outliers(self):
        background = np.full((32, 192, 3), .15, np.float32)
        paint = np.array([.8, .55, .3], np.float32)
        coverage = np.zeros(background.shape[:2], np.float32)
        for left in range(8, 160, 24):
            coverage[9:20, left:left + 8] = 1
        mask = coverage[:, :, None]
        rest = background + mask * (paint - background)
        template = np.dstack((mask * paint, coverage))
        for surviving_interiors in (False, True):
            with self.subTest(surviving_interiors=surviving_interiors):
                current = coverage.copy()
                if surviving_interiors:
                    current[:, :120] *= .65
                opacity = .6 if surviving_interiors else .85
                frame = background + opacity * current[:, :, None] * (paint - background)
                if not surviving_interiors:
                    # An isolated full-bright pixel in each of two different patches.
                    frame[10, 9] = paint
                    frame[10, 33] = paint
                keys = [dict(x=0, y=0, scale=1, alpha=.2), dict(x=0, y=0, scale=1, alpha=1)]
                diagnostics = [dict(staticTailStart=1, sourceBounds=[0, 0, 192, 32])]
                refine_flat_opacity([0, 1], [frame, rest].__getitem__, background,
                                    [dict(pivotX=0, pivotY=0)], [dict(keys=keys)], diagnostics,
                                    lambda _: template)
                self.assertAlmostEqual(keys[0]['alpha'], opacity, places=5)


if __name__ == '__main__':
    unittest.main()
