"""Adapter-free scene discovery and binding orchestration; no game-specific names."""
from pathlib import Path
import json, re, time
import unity as core


def request(a):
    if (a.width == 0) != (a.height == 0):
        raise ValueError('Specify both --width and --height, or neither')
    if a.width < 0 or a.height < 0 or max(a.width, a.height) > 4096:
        raise ValueError('Capture dimensions must be between 1 and 4096')
    if not 1 <= a.max_targets <= 24 or not 1 <= a.max_cases <= 12:
        raise ValueError('Use 1..24 targets and 1..12 cases')
    return dict(scene=a.scene or '', canvas=a.canvas or '', targets=a.target or [],
                execution=a.execution, width=a.width, height=a.height,
                fps=30, duration=2, settleSeconds=a.settle, timeoutSeconds=60,
                maxTargets=a.max_targets, maxCases=a.max_cases,
                includeInactive=a.include_inactive, maxScenes=64)


def start(a):
    source=Path(a.source).resolve()
    manifest=json.loads((source/'Packages/manifest.json').read_text(encoding='utf-8-sig'))
    lock=source/'Packages/packages-lock.json'
    packages=set(manifest.get('dependencies', {}))
    if lock.exists(): packages.update(json.loads(lock.read_text(encoding='utf-8-sig')).get('dependencies', {}))
    if 'com.unity.ugui' not in packages:
        raise ValueError('No uGUI package detected. Auto binding currently supports Canvas/RectTransform UI, not UI Toolkit or IMGUI. The source was not modified.')
    req=request(a)
    if req['settleSeconds'] < 0 or req['settleSeconds'] > 45:
        raise ValueError('--settle must be between 0 and 45 seconds')
    if req['scene']:
        p=(source/req['scene']).resolve()
        if not p.is_relative_to(source/'Assets') or p.suffix != '.unity' or not p.is_file():
            raise ValueError('--scene must name an existing Assets/.../*.unity scene')
        req['scene']=p.relative_to(source).as_posix()
    w=core.initialize(a)
    old=w/'auto-request.json'
    if (w/'asset-baseline.json').exists():
        raise ValueError('This workspace is already prepared. Use a fresh workspace to change discovery or bindings; the integrity baseline will not be reset.')
    old.write_text(json.dumps(req,indent=2),encoding='utf-8')
    return w,req


def scan(a):
    w,req=start(a)
    core.launch(w,'discover')
    inventory=json.loads((w/'scene-inventory.json').read_text(encoding='utf-8'))
    print(json.dumps({'inventory':str(w/'scene-inventory.json'),'scenes':len(inventory['scenes']),
                      'entryScene':inventory.get('entryScene'),'truncated':inventory.get('truncated',False)}))


def prepare(a):
    w,req=start(a); started=core.now(); clock=time.perf_counter()
    core.launch(w,'discover')
    inventory=json.loads((w/'scene-inventory.json').read_text(encoding='utf-8'))
    available=[s for s in inventory['scenes'] if any(c['graphics'] for c in s['canvases'] if c['renderMode']!='WorldSpace')]
    ranked=sorted(available,key=lambda s:sum(c['graphics']+3*c['selectables'] for c in s['canvases'] if c['renderMode']!='WorldSpace'),reverse=True)
    if not req['scene']:
        if req['execution']=='play' and inventory.get('entryScene') and (Path(core.state(w)['project'])/inventory['entryScene']).is_file():
            req['scene']=inventory['entryScene']
        else:
            if not available:
                raise RuntimeError('No serialized screen-space uGUI found. For runtime-created UI, select its startup --scene and use --execution play. See scene-inventory.json.')
            req['scene']=ranked[0]['path']
    (w/'auto-request.json').write_text(json.dumps(req,indent=2),encoding='utf-8')
    previous=None;fallbacks=[]
    for attempt in range(3):
        try:
            core.launch(w,'prepare')
        except RuntimeError:
            error=w/'auto-error.json'
            details=json.loads(error.read_text(encoding='utf-8')) if error.exists() else {}
            alternatives=[s['path'] for s in ranked if s['path']!=req['scene']]
            if a.scene or req['execution']!='play' or fallbacks or details.get('type')!='TimeoutException' or not alternatives:
                raise
            fallback={'from':req['scene'],'to':alternatives[0],'reason':'Normal startup did not expose stable uGUI; one automatic Play Mode retry in a discovered UI scene.'}
            fallbacks.append(fallback);core.append(w/'events.jsonl',{'stage':'startupFallback','endUtc':core.now(),**fallback})
            req['scene']=alternatives[0]
            (w/'auto-request.json').write_text(json.dumps(req,indent=2),encoding='utf-8')
            core.launch(w,'prepare')
        current=core.hashes(Path(core.state(w)['project']),include_settings=True)
        if previous is not None:
            changed=sorted(k for k in set(previous)|set(current) if previous.get(k)!=current.get(k))
            core.append(w/'events.jsonl',dict(stage='importStability',attempt=attempt+1,endUtc=core.now(),changed=changed))
            if not changed: break
        previous=current
    else: raise RuntimeError('Imports did not stabilize within three preparation launches. See events.jsonl.')
    destination=json.loads((w/'destination.json').read_text(encoding='utf-8'))
    if not destination['cases']: raise RuntimeError('Auto binding did not produce a usable case')
    for c in destination['cases']:
        if not c['elements']:raise RuntimeError('Auto binding selected no visible targets: '+c['caseName'])
        for e in c['elements']:
            if e['layoutDriven'] or not (w/e['template']).is_file(): raise RuntimeError('Invalid automatic target: '+e['name'])
    d=core.state(w);d.update(cases=[c['caseName'] for c in destination['cases']],
                            execution=req['execution'],scene=req['scene'],width=destination['width'],
                            height=destination['height'],fps=destination['fps'],duration=destination['duration'],startupFallbacks=fallbacks)
    (w/'workspace.json').write_text(json.dumps(d,indent=2),encoding='utf-8')
    (w/'asset-baseline.json').write_text(json.dumps(current,indent=2),encoding='utf-8')
    report=json.loads((w/'auto-report.json').read_text(encoding='utf-8'))
    report['startupFallbacks']=fallbacks
    (w/'auto-report.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
    from selection_report import write_selection_report
    write_selection_report(w)
    core.append(w/'events.jsonl',dict(stage='prepareComplete',startUtc=started,endUtc=core.now(),
                                    seconds=time.perf_counter()-clock,profile='auto',cases=d['cases'],
                                    templates=sum(len(c['elements']) for c in destination['cases'])))
    print(json.dumps({'workspace':str(w),'cases':d['cases'],'execution':d['execution'],
                      'bindings':str(w/'auto-bindings.json'),'report':str(w/'auto-report.json')}))
