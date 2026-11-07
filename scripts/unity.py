"""Prepare an isolated Unity UI workspace, extract templates, match and capture."""
from contextlib import contextmanager
from pathlib import Path
from tempfile import TemporaryDirectory
import argparse,concurrent.futures,datetime,hashlib,json,os,re,shutil,subprocess,sys,time

SKILL=Path(__file__).resolve().parent.parent
PROFILES={
 'auto':{'cases':[], 'removePackages':[], 'quarantine':[]},
 'trash-dash':{'scene':'Assets/Scenes/Shop.unity','cases':['items','characters','themes'],
  'removePackages':['com.unity.purchasing'],'quarantine':['Packages/com.unity.asset-store-tools','Assets/MobileDependencyResolver']},
 'survivors-kit':{'scene':'Assets/RGame/RoguelikeKit/Scenes/Menus/MainMenu.unity','cases':['character','powerup','settings'],
  'removePackages':[],'quarantine':[]}}
def now():return datetime.datetime.now(datetime.timezone.utc).isoformat()
def append(p,obj):
 p.parent.mkdir(parents=True,exist_ok=True)
 with p.open('a',encoding='utf-8') as f:f.write(json.dumps(obj,ensure_ascii=False)+'\n')
def editor(path=None):
 if path:
  p=Path(path).resolve()
  if not p.is_file():raise FileNotFoundError(p)
  return p
 base=Path(os.environ.get('ProgramFiles','C:/Program Files'))/'Unity/Hub/Editor'
 matches=sorted(base.glob('6000.*/Editor/Unity.exe'),key=lambda p:[int(v) for v in re.findall(r'\d+',p.parts[-3])])
 if not matches:raise RuntimeError('Unity 6000 editor not found; specify --editor. Profiles were tested with 6000.3.21f1.')
 return matches[-1]
def state(w):return json.loads((w/'workspace.json').read_text(encoding='utf-8-sig'))
def checked_child(root,rel):
 p=(root/rel).resolve()
 if not p.is_relative_to(root.resolve()):raise ValueError('Path escapes workspace: '+str(p))
 return p
def hashes(root,include_settings=False):
 def generated(p):
  rel=p.relative_to(root).as_posix()
  return (rel.startswith('Assets/MotionMatchGenerated/') or rel=='Assets/MotionMatchGenerated.meta') if include_settings else 'MotionMatchGenerated' in p.parts
 return {str(p.relative_to(root)).replace('\\','/'):hashlib.sha256(p.read_bytes()).hexdigest()
         for folder in (['Assets','Packages','ProjectSettings'] if include_settings else ['Assets'])
         for p in (root/folder).rglob('*') if p.is_file() and not generated(p)}
def initialize(a):
 w=Path(a.workspace).resolve();source=Path(a.source).resolve();profile=PROFILES[a.profile];exe=editor(a.editor)
 if source==w or w.is_relative_to(source):raise ValueError('Workspace must be outside the source project')
 if (w/'workspace.json').exists():
  old=state(w)
  if old['source']!=str(source) or old['profile']!=a.profile:raise ValueError('Workspace belongs to another project/profile')
  if a.editor and Path(old['editor']).resolve()!=exe:raise ValueError('Workspace uses a different Unity Editor; use a fresh workspace to change versions')
  if a.profile=='auto' and not (w/'asset-baseline.json').exists():
   shutil.copy2(SKILL/'assets/auto/AutoBridge.cs',Path(old['project'])/'Assets/MotionMatchGenerated/Editor/MotionMatchBridge.cs')
  return w
 if w.exists() and any(w.iterdir()):raise ValueError('New workspace must be empty; existing content will not be overwritten')
 for d in ['Assets','Packages','ProjectSettings']:
  if not (source/d).is_dir():raise ValueError('Not a Unity project: '+str(source/d))
 if profile.get('scene') and not (source/profile['scene']).is_file():raise ValueError('Profile scene is missing: '+profile['scene'])
 if (source/'Assets/MotionMatchGenerated').exists():raise ValueError('Source already contains MotionMatchGenerated; use the original project snapshot')
 w.mkdir(parents=True,exist_ok=True);start=now();clock=time.perf_counter();project=w/'unity'
 for d in ['Assets','Packages','ProjectSettings']:shutil.copytree(source/d,project/d)
 append(w/'events.jsonl',dict(stage='copy',startUtc=start,endUtc=now(),seconds=time.perf_counter()-clock,projectCacheCopied=False))
 changes=[]
 for rel in profile['quarantine']:
  p=checked_child(project,rel)
  if p.exists():
   q=checked_child(w,'quarantine/'+rel);q.parent.mkdir(parents=True,exist_ok=True);shutil.move(str(p),str(q));changes.append(rel)
   meta=Path(str(p)+'.meta')
   if meta.exists():shutil.move(str(meta),str(q)+'.meta')
 if profile['removePackages']:
  manifest=project/'Packages/manifest.json';data=json.loads(manifest.read_text(encoding='utf-8-sig'))
  for name in profile['removePackages']:
   if name in data['dependencies']:del data['dependencies'][name];changes.append('package:'+name)
  manifest.write_text(json.dumps(data,indent=2))
 bridge=project/'Assets/MotionMatchGenerated/Editor/MotionMatchBridge.cs';bridge.parent.mkdir(parents=True,exist_ok=True)
 shutil.copy2(SKILL/'assets'/('auto/AutoBridge.cs' if a.profile=='auto' else f'profiles/{a.profile}.cs'),bridge)
 d=dict(source=str(source),project=str(project),profile=a.profile,editor=str(exe),createdUtc=start,isolatedChanges=changes,cases=profile['cases'])
 (w/'workspace.json').write_text(json.dumps(d,indent=2));append(w/'events.jsonl',dict(stage='initialize',endUtc=now(),isolatedChanges=changes))
 return w
def launch(w,mode,revision='r0'):
 d=state(w);log=w/'logs'/f'{mode}-{revision}-{time.time_ns()}.log';log.parent.mkdir(exist_ok=True)
 if d['profile']=='auto' and (w/'auto-error.json').is_file():
  shutil.copy2(w/'auto-error.json',w/'logs'/f'auto-error-{time.time_ns()}.json')
  (w/'auto-error.json').unlink()
 cmd=[d['editor'],'-batchmode','-disable-assembly-updater','-projectPath',d['project'],
      '-executeMethod',('UnityMotionMatch.Automatic.AutoBridge.'+('Discover' if mode=='discover' else 'Run') if d['profile']=='auto' else 'UnityMotionMatch.MotionMatchBridge.Run'),'-motionWorkspace',str(w),
      '-motionMode',mode,'-motionRevision',revision,'-logFile',str(log)]
 t=time.perf_counter();start=now()
 try:
  p=subprocess.run(cmd,stdout=subprocess.PIPE,stderr=subprocess.STDOUT,encoding='utf-8',errors='replace',timeout=900)
  code=p.returncode
 except subprocess.TimeoutExpired:
  append(w/'events.jsonl',dict(stage=mode,revision=revision,startUtc=start,endUtc=now(),seconds=time.perf_counter()-t,returncode='timeout',log=str(log)))
  raise RuntimeError('Unity timed out; inspect '+str(log))
 row=dict(stage=mode,revision=revision,startUtc=start,endUtc=now(),seconds=time.perf_counter()-t,returncode=code,log=str(log));append(w/'events.jsonl',row);print(json.dumps(row),flush=True)
 if code:
  lines=log.read_text(encoding='utf-8-sig',errors='replace').splitlines() if log.exists() else []
  print('\n'.join(x[:250] for x in lines if 'error CS' in x or 'Exception:' in x)[:3500])
  raise RuntimeError('Native Unity run failed; no automatic repeated retry. '+str(log))
def prepare(a):
 if a.profile=='auto':
  from automatic import prepare as automatic_prepare
  return automatic_prepare(a)
 w=initialize(a);start=now();clock=time.perf_counter();previous=None;project=Path(state(w)['project'])
 # A fresh Unity import can finish package/meta migration on the next launch.
 # Include this stabilization in preparation instead of silently resetting an
 # integrity baseline after the user's first animation operation.
 for attempt in range(3):
  launch(w,'prepare');current=hashes(project)
  if previous is not None:
   changed=sorted(k for k in set(previous)|set(current) if previous.get(k)!=current.get(k))
   append(w/'events.jsonl',dict(stage='importStability',attempt=attempt+1,endUtc=now(),changed=changed))
   if not changed:break
  previous=current
 else:raise RuntimeError('Unity asset migration did not stabilize in three launches; inspect events.jsonl')
 p=w/'destination.json';d=json.loads(p.read_text(encoding='utf-8-sig'))
 for c in d['cases']:
  for i,e in enumerate(c['elements']):
   e['template']=f"fixtures/{c['caseName']}/template-{i}.png"
   if not (w/e['template']).is_file():raise RuntimeError('Template missing: '+e['template'])
   if e['layoutDriven']:raise RuntimeError('Selected target is directly controlled by a layout group: '+e['path'])
 p.write_text(json.dumps(d,indent=2));(w/'asset-baseline.json').write_text(json.dumps(current,indent=2))
 append(w/'events.jsonl',dict(stage='prepareComplete',startUtc=start,endUtc=now(),seconds=time.perf_counter()-clock,templates=sum(len(c['elements']) for c in d['cases'])))
 print(json.dumps(dict(workspace=str(w),destination=str(p),cases=[c['caseName'] for c in d['cases']]),ensure_ascii=False))
@contextmanager
def frames_for(w,reference,case,revision):
 source=Path(reference).resolve()/f'{case}-{revision}'
 frames=sorted(source.glob('[0-9][0-9][0-9][0-9].png'))
 if frames:
  if len(frames) not in (60,61):raise ValueError('This capture profile expects 60 or 61 frames at 30fps: '+str(source))
  yield source
  return
 video=source/'reference.mp4'
 if not video.exists():raise FileNotFoundError('Expected PNG frames or '+str(video))
 # The last frame calibrates the rest pose. Never share a decode directory
 # between matches, even if the user replaces an MP4 under the same revision.
 scratch=checked_child(w,'frames');scratch.mkdir(parents=True,exist_ok=True)
 with TemporaryDirectory(dir=scratch,prefix='decode-') as tmp:
  out=Path(tmp)
  # One extra frame is enough to reject an overlong recording.
  subprocess.run(['ffmpeg','-nostdin','-xerror','-loglevel','error','-i',str(video),'-vf','fps=30','-frames:v','62',str(out/'%04d.png')],check=True)
  count=len(list(out.glob('*.png')))
  if count not in (60,61):
   decoded='at least 62' if count==62 else str(count)
   raise ValueError(f'Expected 60 or 61 frames at 30fps: {video} (decoded {decoded})')
  yield out
def audit(w):
 expected=json.loads((w/'asset-baseline.json').read_text(encoding='utf-8-sig'));current=hashes(Path(state(w)['project']),include_settings=state(w)['profile']=='auto')
 changed=[k for k,v in expected.items() if current.get(k)!=v]
 added=sorted(set(current)-set(expected)) if state(w)['profile']=='auto' else []
 result=dict(checked=len(expected),changedOrMissing=changed,unexpectedAdded=added,pass_=not changed and not added)
 (w/'asset-integrity.json').write_text(json.dumps(result,indent=2))
 if changed or added:raise RuntimeError('Project integrity changed: '+str((changed+added)[:8]))
 return result
def match(a):
 w=Path(a.workspace).resolve();d=state(w);revision=a.revision
 if not re.fullmatch(r'[A-Za-z0-9_-]+',revision):raise ValueError('Invalid revision')
 audit(w)
 start=now();clock=time.perf_counter();out=w/'solutions'/revision;out.mkdir(parents=True,exist_ok=True)
 def one(case):
  with frames_for(w,a.reference,case,revision) as frames:
   cmd=[sys.executable,str(SKILL/'scripts/motion.py'),'extract-textured','--frames',str(frames),'--destination',str(w/'destination.json'),'--case',case,'--out',str(out/(case+'.json'))]
   if frames != Path(a.reference).resolve()/f'{case}-{revision}':
    cmd.append('--video-input')
   p=subprocess.run(cmd,capture_output=True,text=True,encoding='utf-8')
  (w/'logs'/f'fit-{case}-{revision}.log').write_text(p.stdout+'\n'+p.stderr,encoding='utf-8')
  if p.returncode:raise RuntimeError('Matching failed for '+case+'; inspect its fit log')
 with concurrent.futures.ThreadPoolExecutor(max_workers=min(3,len(d['cases']))) as pool:list(pool.map(one,d['cases']))
 append(w/'events.jsonl',dict(stage='extract',revision=revision,startUtc=start,endUtc=now(),seconds=time.perf_counter()-clock))
 launch(w,'capture',revision);integrity=audit(w)
 checks=[]
 for c in d['cases']:
  result=json.loads((w/f'evidence/{revision}/{c}.json').read_text(encoding='utf-8-sig'))
  native_ok=result.get('nativeCapture',False) if d['profile']=='auto' else result['enteredPlayMode']
  checks.append(dict(case=c,pass_=native_ok and all(result[k] for k in ['layoutPreserved','listenerPreserved','referencesPreserved']) and all(x['pass'] for x in result['checks'])))
 print(json.dumps(dict(revision=revision,integrity=integrity,cases=checks)))
 if not all(c['pass_'] for c in checks):raise RuntimeError('Native integration checks failed')
def main():
 p=argparse.ArgumentParser(description=__doc__);sub=p.add_subparsers(dest='command',required=True)
 for command in ['prepare','scan']:
  a=sub.add_parser(command);a.add_argument('--source',required=True);a.add_argument('--workspace',required=True);a.add_argument('--profile',choices=PROFILES,default='auto');a.add_argument('--editor')
  a.add_argument('--scene');a.add_argument('--canvas');a.add_argument('--target',action='append');a.add_argument('--execution',choices=['play','edit'],default='play');a.add_argument('--width',type=int,default=0);a.add_argument('--height',type=int,default=0);a.add_argument('--settle',type=float,default=3);a.add_argument('--max-targets',type=int,default=6);a.add_argument('--max-cases',type=int,default=1);a.add_argument('--include-inactive',action='store_true')
 a=sub.add_parser('match');a.add_argument('--workspace',required=True);a.add_argument('--reference',required=True);a.add_argument('--revision',required=True)
 a=sub.add_parser('capture');a.add_argument('--workspace',required=True);a.add_argument('--revision',required=True)
 a=sub.add_parser('audit');a.add_argument('--workspace',required=True)
 a=p.parse_args()
 if a.command=='scan':
  if a.profile!='auto':raise ValueError('scan is for automatic discovery; omit --profile')
  from automatic import scan
  scan(a)
 elif a.command=='prepare':prepare(a)
 elif a.command=='match':match(a)
 elif a.command=='capture':
  if not re.fullmatch(r'[A-Za-z0-9_-]+',a.revision):raise ValueError('Invalid revision')
  w=Path(a.workspace).resolve();audit(w);launch(w,'capture',a.revision);print(json.dumps(audit(w)))
 else:print(json.dumps(audit(Path(a.workspace).resolve())))
if __name__=='__main__':main()
