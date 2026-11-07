"""Generate and fit a controlled reversal on the prepared UGUI vs UIToolkit main menu."""
import argparse
import hashlib
import json
import math
import shutil
import subprocess
import sys
from pathlib import Path
from uuid import uuid4


SKILL = Path(__file__).resolve().parents[1]
# dx, dy (pixels), relative scale change, opacity reduction, onset and length (seconds)
MOTION = {
    'Main Menu Canvas/Menu Panel': (26, -12, .055, .65, 0, 1.65),
    'Main Menu Canvas/Navigation Information Panel': (-30, -16, -.045, .80, .08, 1.67),
    'Main Menu Canvas/Logo': (22, 18, -.075, .50, .14, 1.66),
}


def read(path):
    return json.loads(path.read_text(encoding='utf-8-sig'))


def write(path, value):
    path.write_text(json.dumps(value, indent=2), encoding='utf-8')


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def keys(parameters):
    dx, dy, ds, depth, onset, duration = parameters
    samples = []
    for frame in range(61):
        t = frame / 30
        u = (t - onset) / duration
        pulse = math.sin(math.pi * u) if 0 < u < 1 else 0.0
        offset = pulse * math.sin(2 * math.pi * u)
        samples.append(dict(t=t, x=dx * offset, y=dy * pulse**2,
                            scale=1 + ds * pulse**2, alpha=1 - depth * pulse**2))
    return samples


def run(workspace):
    state = read(workspace / 'workspace.json')
    dest = read(workspace / 'destination.json')
    if (state.get('profile'), state.get('execution'), dest['width'], dest['height'],
            dest['fps'], dest['duration'], len(dest['cases'])) != ('auto', 'play', 960, 540, 30, 2, 1):
        raise ValueError('Use the auto Play Mode main menu workspace at 960x540, 2 seconds, 30 fps.')
    case, = dest['cases']
    case_id = case['caseName']
    if (state['cases'] != [case_id] or case_id != 'c_190de100f8bd' or len(case['elements']) != 3
            or {e['path'] for e in case['elements']} != set(MOTION)):
        raise ValueError('Expected the three documented targets from UGUI vs UIToolkit.')
    run_id = 'reversal-' + uuid4().hex[:12]
    folder = workspace / 'benchmarks' / run_id
    folder.mkdir(parents=True, exist_ok=False)
    truth_rev, fit_rev = run_id + '-truth', run_id + '-fit'
    cli = [sys.executable, '-X', 'utf8', str(SKILL / 'scripts/unity.py')]
    scripts = ['scripts/textured.py', 'scripts/flat_opacity.py', 'scripts/motion.py',
               'scripts/unity.py', 'assets/auto/AutoBridge.cs', 'benchmarks/score.py', 'benchmarks/reversal.py']
    protocol = dict(run=run_id, motion=MOTION, criteria=dict(alphaMAE=.035, alphaMax=.035, positionMAE=5,
                    maxPosition=18, scaleMAE=.025, visibleAboveAlpha=.1),
                    codeSha256={f: digest(SKILL / f) for f in scripts},
                    installedBridgeSha256=digest(Path(state['project']) / 'Assets/MotionMatchGenerated/Editor/MotionMatchBridge.cs'),
                    baselineSha256=digest(workspace / 'asset-baseline.json'))
    if protocol['installedBridgeSha256'] != protocol['codeSha256']['assets/auto/AutoBridge.cs']:
        raise ValueError('Prepared bridge differs from the benchmarked skill.')
    write(folder / 'protocol.json', protocol)
    solution = workspace / 'solutions' / truth_rev
    solution.mkdir(exist_ok=False)
    write(solution / (case_id + '.json'), dict(caseName=case_id, tracks=[
        dict(name=e['name'], keys=keys(MOTION[e['path']])) for e in case['elements']]))
    subprocess.run(cli + ['capture', '--workspace', str(workspace), '--revision', truth_rev], check=True)
    truth = workspace / 'evidence' / truth_rev
    pngs = [truth / case_id / f'{i:04d}.png' for i in range(1, 62)]
    references = folder / 'references'
    images = references / (case_id + '-' + fit_rev)
    images.mkdir(parents=True)
    for png in pngs:
        shutil.copyfile(png, images / png.name)
    write(folder / 'frame-hashes.json', {p.name: digest(images / p.name) for p in pngs})
    subprocess.run(cli + ['match', '--workspace', str(workspace), '--reference', str(references),
                          '--revision', fit_rev], check=True)
    fitted = workspace / 'evidence' / fit_rev
    calibration = workspace / 'solutions' / fit_rev / (case_id + '.fit.json')
    if digest(workspace / 'asset-baseline.json') != protocol['baselineSha256']:
        raise RuntimeError('The baseline changed during the benchmark.')
    shutil.copyfile(workspace / 'asset-integrity.json', folder / 'integrity.json')
    result = subprocess.run([sys.executable, str(SKILL / 'benchmarks/score.py'), str(truth / (case_id + '.json')),
                             str(fitted / (case_id + '.json')), '--diagnostics', str(calibration),
                             '--require-peak', '--out', str(folder / 'score.json')])
    print(str(folder), flush=True)
    return result.returncode


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--workspace', required=True, type=Path)
    raise SystemExit(run(parser.parse_args().workspace.resolve()))
