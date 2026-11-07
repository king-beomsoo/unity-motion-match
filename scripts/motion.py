import argparse,json
from pathlib import Path
import cv2
import numpy as np
from textured import extract_textured

def extract(a):
 d=json.loads(Path(a.destination).read_text(encoding='utf-8-sig')); elements=d['cases'][a.case];bg=np.array(d['background'],float)
 cap=cv2.VideoCapture(a.video);fps=cap.get(cv2.CAP_PROP_FPS);tracks={e['name']:[] for e in elements};i=0
 while True:
  ok,bgr=cap.read()
  if not ok:break
  rgb=cv2.cvtColor(bgr,cv2.COLOR_BGR2RGB).astype(float);p=rgb-bg
  for e in elements:
   v=np.array(e['color'])-bg;alpha=np.sum(p*v,axis=2)/np.sum(v*v);err=np.linalg.norm(p-alpha[:,:,None]*v,axis=2)
   mask=((alpha>.055)&(alpha<1.12)&(err<9)).astype(np.uint8)
   n,labels,stats,centroids=cv2.connectedComponentsWithStats(mask,8)
   valid=[j for j in range(1,n) if stats[j,4]>80]
   if not valid:tracks[e['name']].append(None);continue
   j=max(valid,key=lambda j:stats[j,4]);x,y,w,h,_=stats[j];s=max(w/e['w'],h/e['h'])
   cx=x+w/2;cy=y+h/2
   if x+w>=rgb.shape[1]-1:cx=x+e['w']*s/2
   if x<=1:cx=x+w-e['w']*s/2
   opacity=float(np.median(alpha[labels==j]));tracks[e['name']].append(dict(t=i/fps,x=float(cx-e['x']),y=float(cy-e['y']),scale=float(s),alpha=min(1,max(0,opacity))))
  i+=1
 cap.release();result={'caseName':a.case,'tracks':[]}
 for e in elements:
  rows=tracks[e['name']];valid=[j for j,r in enumerate(rows) if r is not None]
  if not valid:raise ValueError('No track found for '+e['name'])
  keys=[]
  for j,r in enumerate(rows):
   if r is None:
    nearest=min(valid,key=lambda k:abs(k-j));r=dict(rows[nearest],t=j/fps,alpha=0)
   keys.append(r)
  result['tracks'].append(dict(name=e['name'],keys=keys))
 Path(a.out).parent.mkdir(parents=True,exist_ok=True);Path(a.out).write_text(json.dumps(result,indent=2));print(json.dumps({'output':a.out,'frames':i,'fps':fps,'tracks':len(elements)}))
def clean(a):
 # Benchmark-local repair: an isolated one-frame colour detection surrounded
 # by invisibility is a false positive in these smooth entrance recordings.
 # Operate on retained extracted curves; do not decode an unchanged video.
 d=json.loads(Path(a.input).read_text(encoding='utf-8-sig'));removed=[]
 for tr in d['tracks']:
  rows=tr['keys'];visible=[k['alpha']>0 for k in rows]
  for i,k in enumerate(rows):
   if visible[i] and not (i>0 and visible[i-1]) and not (i+1<len(rows) and visible[i+1]):
    removed.append({'track':tr['name'],'t':k['t']});k['alpha']=0
  valid=[i for i,k in enumerate(rows) if k['alpha']>0]
  for i,k in enumerate(rows):
   if k['alpha']==0 and valid:
    nearest=min(valid,key=lambda j:abs(j-i))
    for prop in ('x','y','scale'):k[prop]=rows[nearest][prop]
 Path(a.output).parent.mkdir(parents=True,exist_ok=True);Path(a.output).write_text(json.dumps(d,indent=2));print(json.dumps({'output':a.output,'removed':removed}))
def revise(a):
 d=json.loads(Path(a.input).read_text(encoding='utf-8-sig'));starts=[]
 for tr in d['tracks']:
  starts.append(next((k['t'] for k in tr['keys'] if k['alpha']>.06),0))
 for i,tr in enumerate(d['tracks']):
  delta=(starts[i]-starts[0])*(a.stagger_ratio-1)
  keys=[]
  for k in tr['keys']:
   k['t']=max(0,(k['t']+delta)/a.speed);k['x']*=a.distance if a.distance_x is None else a.distance_x;k['y']*=a.distance if a.distance_y is None else a.distance_y
   if keys and abs(keys[-1]['t']-k['t'])<1e-7:keys[-1]=k
   else:keys.append(k)
  tr['keys']=keys
 Path(a.output).parent.mkdir(parents=True,exist_ok=True);Path(a.output).write_text(json.dumps(d,indent=2));print(a.output)
ap=argparse.ArgumentParser();sub=ap.add_subparsers(dest='command',required=True)
x=sub.add_parser('extract');x.add_argument('--video',required=True);x.add_argument('--destination',required=True);x.add_argument('--case',required=True);x.add_argument('--out',required=True)
x=sub.add_parser('extract-textured');x.add_argument('--frames',required=True);x.add_argument('--destination',required=True);x.add_argument('--case',required=True);x.add_argument('--out',required=True)
x.add_argument('--video-input',action='store_true',help='Input frames were decoded from a video')
x=sub.add_parser('revise');x.add_argument('--input',required=True);x.add_argument('--output',required=True);x.add_argument('--speed',type=float,default=1);x.add_argument('--distance',type=float,default=1);x.add_argument('--distance-x',type=float);x.add_argument('--distance-y',type=float);x.add_argument('--stagger-ratio',type=float,default=1)
x=sub.add_parser('clean');x.add_argument('--input',required=True);x.add_argument('--output',required=True)
a=ap.parse_args();{'extract':extract,'extract-textured':extract_textured,'revise':revise,'clean':clean}[a.command](a)
