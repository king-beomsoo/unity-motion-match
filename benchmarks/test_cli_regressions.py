"""Exercise video replacement and benchmark exit codes at their I/O boundaries."""
import json
from pathlib import Path
import shutil
import subprocess
import sys
from tempfile import TemporaryDirectory
import unittest

BENCH = Path(__file__).resolve().parent
sys.path.insert(0, str(BENCH.parent / 'scripts'))
from unity import frames_for


class ReferenceVideoTest(unittest.TestCase):
    @unittest.skipUnless(shutil.which('ffmpeg'), 'requires ffmpeg')
    def test_replacing_one_revision_never_reuses_old_frames(self):
        from PIL import Image
        with TemporaryDirectory() as tmp:
            workspace = Path(tmp)
            refs = workspace / 'refs'
            src = refs / 'menu-r1'
            src.mkdir(parents=True)
            video = src / 'reference.mp4'
            for count, color, channel in ((61, 'red', 0), (60, 'blue', 2),
                                          (30, 'green', None), (70, 'red', None)):
                with self.subTest(count=count):
                    subprocess.run(['ffmpeg', '-nostdin', '-y', '-loglevel', 'error',
                                    '-f', 'lavfi', '-i', f'color=c={color}:s=32x32:r=30',
                                    '-frames:v', str(count), '-c:v', 'mpeg4', str(video)], check=True)
                    if channel is None:
                        with self.assertRaisesRegex(ValueError, '60 or 61'):
                            with frames_for(workspace, refs, 'menu', 'r1'):
                                self.fail('An invalid video reached the fitter')
                    else:
                        with frames_for(workspace, refs, 'menu', 'r1') as frames:
                            pngs = sorted(frames.glob('*.png'))
                            self.assertEqual(len(pngs), count)
                            with Image.open(pngs[-1]) as last:
                                rgb = last.convert('RGB').getpixel((0, 0))
                            self.assertGreater(rgb[channel], 240)
                            self.assertLess(max(rgb[i] for i in range(3) if i != channel), 10)
                    self.assertTrue(video.is_file())
                    self.assertEqual(list((workspace / 'frames').iterdir()), [])


class ScorerExitTest(unittest.TestCase):
    def test_peak_and_preservation_failures_reach_the_caller(self):
        truth = BENCH / 'evidence/truth.json'
        with TemporaryDirectory() as tmp:
            root = Path(tmp)
            replay, report = root / 'replay.json', root / 'score.json'
            def check(peak, expected):
                args = [sys.executable, str(BENCH / 'score.py'), str(truth), str(replay),
                        '--out', str(report)]
                if peak:
                    args.append('--require-peak')
                result = subprocess.run(args, capture_output=True, text=True)
                self.assertEqual(result.returncode, expected, result.stderr)
                self.assertIs(json.loads(result.stdout)['passed'], expected == 0)
                self.assertIs(json.loads(report.read_text())['passed'], expected == 0)
                report.unlink()
            native = json.loads((BENCH / 'evidence/replay.json').read_text(encoding='utf-8-sig'))
            replay.write_text(json.dumps(native))
            check(True, 0)
            native['samples'][0]['items'][0]['alpha'] = 0.0  # starts opaque; one bad frame
            replay.write_text(json.dumps(native))
            check(False, 0)
            check(True, 2)
            native['layoutPreserved'] = False
            replay.write_text(json.dumps(native))
            check(False, 2)


if __name__ == '__main__':
    unittest.main()
