"""Score a pair of 61-frame Unity Play Mode captures of the same bound UI."""
import argparse
import json
import math
from pathlib import Path
from statistics import mean


def read(path):
    return json.loads(path.read_text(encoding='utf-8-sig'))


def items(sample):
    indexed = {item['name']: item for item in sample['items']}
    if len(indexed) != len(sample['items']) or not indexed:
        raise ValueError('Empty or duplicate target IDs in capture.')
    for item in indexed.values():
        if not all(math.isfinite(item[k]) for k in ('x', 'y', 'scale', 'alpha')):
            raise ValueError('Non-finite capture value.')
        if not 0 <= item['alpha'] <= 1 or item['scale'] <= 0:
            raise ValueError('Invalid opacity or scale.')
    return indexed


def score(truth, replay, diagnostics=(), *, require_peak=False):
    if truth['caseName'] != replay['caseName'] or not all(
        c['enteredPlayMode'] and c['nativeCapture'] and len(c['samples']) == 61 for c in (truth, replay)
    ):
        raise ValueError('Expected two native Play Mode captures of the same 61-frame case.')
    names = sorted(items(truth['samples'][0]))
    observations = {name: [] for name in names}
    for index, (ref, got) in enumerate(zip(truth['samples'], replay['samples'])):
        if not all(math.isclose(s['t'], index / 30, abs_tol=1e-5) for s in (ref, got)):
            raise ValueError('Expected matching 30 fps timestamps.')
        expected, actual = items(ref), items(got)
        if sorted(expected) != names or sorted(actual) != names:
            raise ValueError('Target IDs changed during replay.')
        for name in names:
            observations[name].append((expected[name], actual[name]))
    estimates = {d['name']: d.get('flatPaintOpacity') for d in diagnostics}
    tracks = []
    for name, pairs in observations.items():
        alpha = [abs(a['alpha'] - b['alpha']) for a, b in pairs]
        fade = [alpha[i] for i in range(1, len(pairs))
                if abs(pairs[i][0]['alpha'] - pairs[i-1][0]['alpha']) > .001]
        visible = [(a, b) for a, b in pairs if a['alpha'] > .1]
        if not visible:
            raise ValueError('No visible geometry samples for ' + name)
        position = [math.hypot(a['x'] - b['x'], a['y'] - b['y']) for a, b in visible]
        scale = [abs(a['scale'] - b['scale']) for a, b in visible]
        worst = alpha.index(max(alpha))
        tracks.append(dict(name=name, frames=len(alpha), alphaMAE=mean(alpha),
                           alphaP95=sorted(alpha)[math.ceil(.95*len(alpha))-1],
                           alphaMax=alpha[worst], worstTimeSeconds=worst/30,
                           framesAbove035=sum(v > .035 for v in alpha),
                           fadeFrames=len(fade), fadeAlphaMAE=mean(fade) if fade else None,
                           visibleFrames=len(visible), positionMAE=mean(position),
                           positionMax=max(position), scaleMAE=mean(scale),
                           patchSampling=estimates.get(name),
                           accuracyPassed=(mean(alpha) <= .035 and (not require_peak or alpha[worst] <= .035)
                                           and mean(position) <= 5
                                           and max(position) <= 18 and mean(scale) <= .025)))
    expected_checks = {'raycast_destinations'} | {'restore:' + name for name in names}
    reference_checks = {c['name'] for c in truth['checks']}
    replay_checks = {c['name'] for c in replay['checks']}
    integrity = (all(c[k] for c in (truth, replay) for k in
                     ('listenerPreserved', 'referencesPreserved', 'layoutPreserved'))
                 and all(check['pass'] for c in (truth, replay) for check in c['checks'])
                 and expected_checks <= reference_checks == replay_checks
                 and sum(name.startswith('resize:') for name in reference_checks) >= 2
                 and truth['raycastProbes'] == replay['raycastProbes']
                 and truth['buttonCount'] == replay['buttonCount'])
    return dict(case=truth['caseName'], truth=truth['revision'], replay=replay['revision'],
                tracks=tracks, requirePeak035=require_peak, structuralChecksPassed=integrity,
                raycastProbes=replay['raycastProbes'],
                passed=integrity and all(t['accuracyPassed'] for t in tracks))


if __name__ == '__main__':
    cli = argparse.ArgumentParser(description=__doc__)
    cli.add_argument('truth', type=Path)
    cli.add_argument('replay', type=Path)
    cli.add_argument('--diagnostics', type=Path)
    cli.add_argument('--require-peak', action='store_true', help='Also require opacity error <= 0.035 in every frame of every track')
    cli.add_argument('--out', type=Path, required=True)
    args = cli.parse_args()
    result = score(read(args.truth), read(args.replay), read(args.diagnostics) if args.diagnostics else [],
                   require_peak=args.require_peak)
    args.out.write_text(json.dumps(result, indent=2), encoding='utf-8')
    print(json.dumps({'report': str(args.out), 'passed': result['passed']}))
    raise SystemExit(0 if result['passed'] else 2)
