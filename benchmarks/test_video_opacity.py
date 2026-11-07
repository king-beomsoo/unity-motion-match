"""Check that compression spikes and legitimate one-frame flashes stay distinct."""
import sys
import unittest
from pathlib import Path

import numpy as np

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'scripts'))
from video_opacity import plateau, temporal_levels


class VideoOpacityTest(unittest.TestCase):
    def test_ringing_peaks_do_not_replace_the_level_of_a_stroke(self):
        ink = np.r_[np.zeros(400), np.full(100, .4), np.full(4, .7)]
        self.assertAlmostEqual(plateau(ink, 100)[0], .4)
        self.assertIsNone(plateau(np.full(100, np.nan), 100))

    def test_flash_and_missing_samples_are_preserved(self):
        alpha = np.zeros(25)
        alpha[8] = .8
        alpha[16] = .13
        observations = np.repeat(alpha[:, None], 3, axis=1)
        observations[12] = np.nan
        result = temporal_levels(observations, 30)
        self.assertEqual(result[8], .8)
        self.assertGreaterEqual(result[16], .115)
        self.assertTrue(np.isnan(result[12]))
