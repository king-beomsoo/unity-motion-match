"""Measured real-UI extension: register provided rendered templates to frames.

This uses reference pixels, the rest/background captures, and public destination
metadata only. It does not recover source animation or read evaluation curves.
"""
import json
from pathlib import Path
import cv2
import numpy as np
from scipy.optimize import minimize
from flat_opacity import refine_flat_opacity


def extract_textured(args):
    root = Path(args.destination).parent
    data = json.loads(Path(args.destination).read_text(encoding='utf-8-sig'))
    def pixels(path, flags=cv2.IMREAD_COLOR):
        img = cv2.imdecode(np.fromfile(path, dtype=np.uint8), flags)
        if img is None or img.shape[:2] != (data['height'], data['width']):
            raise ValueError('Unreadable image or mismatched reference resolution: '+str(path))
        return img.astype(np.float32)/255
    case = next(c for c in data['cases'] if c['caseName'] == args.case)
    files = sorted((p for p in Path(args.frames).glob('*.png') if p.stem.isdigit()), key=lambda p:int(p.stem))
    frames = [pixels(f) for f in files]
    bg = pixels(root/'fixtures'/args.case/'background.png')
    rest = pixels(root/'fixtures'/args.case/'rest.png')
    # Unity's Linear projects blend in linear light but PNGs contain sRGB RGB.
    # Keep alpha unchanged. Legacy metadata without a colorSpace field keeps
    # its previous behavior, preserving existing benchmark/project workspaces.
    linear = data.get('colorSpace') == 'Linear'
    def color(rgb):
        return np.where(rgb <= .04045, rgb/12.92, ((rgb+.055)/1.055)**2.4) if linear else rgb
    # Native text/one-pixel rules are rasterized afresh at each subpixel pose.
    # Register full-resolution, lightly filtered signals rather than sampling
    # every other pixel (which aliases those features into apparent opacity).
    def smooth(a): return cv2.GaussianBlur(a, (0,0), 1.5)
    frames = [smooth(color(frame)) for frame in frames]
    original_bg, original_rest = color(bg), color(rest)
    bg = smooth(original_bg)
    result = {'caseName':args.case, 'tracks':[]}
    diagnostics = []
    fitted = []
    for element in case['elements']:
        tex = pixels(root/element['template'],cv2.IMREAD_UNCHANGED)
        tex[:,:,:3] = color(tex[:,:,:3])
        yy, xx = np.nonzero(tex[:,:,3]>.001)
        # Stored transparent RenderTexture alpha is not always straight alpha.
        # Calibrate effective coverage against the actual rest/background pair.
        coverage = np.clip(np.sum(original_bg*(tex[:,:,:3]+original_bg-original_rest),axis=2)/(np.sum(original_bg*original_bg,axis=2)+1e-7),0,1)
        coverage[tex[:,:,3]<.001] = 0
        fg, coverage = smooth(tex[:,:,:3]), smooth(coverage)
        # Use only this element's area; other simultaneous targets must not enter
        # the source texture even though rest contains all of them.
        # Include the filter halo instead of clipping its integrated coverage.
        x0,x1 = max(0,int(xx.min())-6), min(data['width'],int(xx.max()+1)+6)
        y0,y1 = max(0,int(yy.min())-6), min(data['height'],int(yy.max()+1)+6)
        fg=fg[y0:y1,x0:x1].copy(); coverage=coverage[y0:y1,x0:x1].copy()
        pad_x,pad_y=145,160
        rx0,rx1=max(0,x0-pad_x),min(data['width'],x1+pad_x)
        ry0,ry1=max(0,y0-pad_y),min(data['height'],y1+pad_y)
        gy,gx=np.mgrid[ry0:ry1,rx0:rx1].astype(np.float32)
        backdrop=bg[ry0:ry1,rx0:rx1]
        pivot=np.array([element['pivotX'],element['pivotY']],float)
        source=np.dstack([fg,coverage])
        previous=np.array([0.,0.,1.]); rows=[None]*len(files); scores=[]
        tail_start=len(files)-1
        while tail_start>0 and np.mean(abs(frames[tail_start-1][y0:y1,x0:x1]-frames[-1][y0:y1,x0:x1]))<=.0015:
            tail_start-=1
        for index in range(len(files)-1,-1,-1):
            observed=frames[index][ry0:ry1,rx0:rx1]-backdrop
            # Similar adjacent counters can masquerade as a faint target.
            # Remove already registered elements using their measured pixels
            # and curves before matching the next independent element.
            for prior in fitted:
                key=prior['rows'][index]
                if key['alpha'] == 0: continue
                pp=prior['pivot']; px,py=prior['origin']
                sx=(gx-pp[0]-key['x'])/key['scale']+pp[0]-px
                sy=(gy-pp[1]-key['y'])/key['scale']+pp[1]-py
                sample=cv2.remap(prior['source'],sx.astype(np.float32),sy.astype(np.float32),
                                 cv2.INTER_LINEAR,borderMode=cv2.BORDER_CONSTANT)
                observed=observed-key['alpha']*(sample[:,:,:3]-backdrop*sample[:,:,3:4])
            def render(q):
                sx=(gx-pivot[0]-q[0])/q[2]+pivot[0]-x0
                sy=(gy-pivot[1]-q[1])/q[2]+pivot[1]-y0
                sample=cv2.remap(source,sx.astype(np.float32),sy.astype(np.float32),cv2.INTER_LINEAR,borderMode=cv2.BORDER_CONSTANT)
                return sample[:,:,:3]-backdrop*sample[:,:,3:4]
            def metric(q, details=False):
                signal=render(q)
                power=float(np.sum(signal*signal))+1e-9
                dot=float(np.sum(signal*observed))
                opacity=float(np.clip(dot/power,0,1.15))
                # Least-squares improvement over the static background.
                loss=opacity*opacity*power-2*opacity*dot
                return (loss,opacity,power) if details else loss
            if index == len(files)-1:
                # The documented input ends at the original rest pose. Use
                # that observed boundary to calibrate small template/raster
                # offsets and effective coverage, never evaluation curves.
                calibration=minimize(metric,previous,method='Powell',
                    bounds=[(-4,4),(-4,4),(.98,1.02)],
                    options={'maxiter':12,'xtol':.01,'ftol':1e-8})
                q=min((previous,calibration.x),key=metric).copy()
                rest_q=q.copy();rest_opacity=metric(q,True)[1]
                if not .75 <= rest_opacity <= 1.15:
                    raise ValueError('Reference final pose/appearance does not match the template: '+element['name'])
            elif index >= tail_start:
                q=rest_q.copy()
            else:
                # Repeated glyphs and tab buttons create competing local
                # minima. Seed a small pixel-derived search before refining;
                # never accept an optimizer result worse than its seed.
                seeds=[previous+np.array([dx,dy,ds])
                       for dx in (-6.,0.,6.) for dy in (-8.,0.,8.)
                       for ds in (-.015,0.,.015)]
                seeds=[s for s in seeds if .55 <= s[2] <= 1.3]
                seed=min(seeds,key=metric)
                bounds=[(max(-220,seed[0]-5),min(220,seed[0]+5)),
                        (max(-200,seed[1]-6),min(200,seed[1]+6)),
                        (max(.55,seed[2]-.025),min(1.3,seed[2]+.025))]
                fit=minimize(metric,seed,method='Powell',bounds=bounds,
                    options={'maxiter':8,'xtol':.025,'ftol':1e-7})
                q=min((previous,seed,fit.x),key=metric).copy()
            loss,opacity,power=metric(q,True)
            if opacity<.012:
                opacity=0.;q=previous.copy()
            else:
                previous=q.copy()
            rows[index]={'t':index/data['fps'],'x':float(q[0]),'y':float(q[1]),
                         'scale':float(q[2]),'alpha':opacity}
            scores.append({'frame':index,'loss':loss,'alpha':opacity})
        # Invisible coordinates are unobservable. Retain the nearest visible
        # transform instead of inventing movement. Exact final pose is required.
        visible=[i for i,r in enumerate(rows) if r['alpha']>0]
        if not visible: raise ValueError('No visible template match: '+element['name'])
        for i in range(visible[0]):
            for k in ('x','y','scale'): rows[i][k]=rows[visible[0]][k]
        # Preserve raw image-space fits for subtracting other targets. Export
        # motion relative to the calibrated reference rest, with opacity=1
        # there. Composing inverse rest scale also removes its translation.
        for i in range(tail_start,len(rows)):
            rows[i].update(x=float(rest_q[0]),y=float(rest_q[1]),scale=float(rest_q[2]),alpha=rest_opacity)
        keys=[]
        for row in rows:
            scale=row['scale']/rest_q[2]
            keys.append(dict(t=row['t'],x=float(row['x']-scale*rest_q[0]),
                y=float(row['y']-scale*rest_q[1]),scale=float(scale),alpha=float(np.clip(row['alpha']/rest_opacity,0,1))))
        result['tracks'].append({'name':element['name'],'keys':keys})
        fitted.append({'rows':rows,'source':source,'pivot':pivot,'origin':(x0,y0)})
        diagnostics.append({'name':element['name'],'sourceBounds':[x0,y0,x1,y1],
            'referenceRestCalibration':dict(x=float(rest_q[0]),y=float(rest_q[1]),scale=float(rest_q[2]),opacity=rest_opacity),
            'staticTailStart':tail_start,'scores':scores})
    def read_template(element):
        rgba = pixels(root/element['template'], cv2.IMREAD_UNCHANGED)
        rgba[:, :, :3] = color(rgba[:, :, :3])
        return rgba

    refine_flat_opacity(files, lambda path: color(pixels(path)), original_bg,
                        case['elements'], result['tracks'], diagnostics, read_template)
    if getattr(args, 'video_input', False):
        # Check the stationary background for a change from native RGB before
        # applying a noise model to the decoded recording (it may be lossless).
        backdrop_mask = np.ones(rest.shape[:2], dtype=bool)
        for info in diagnostics:
            x, y, r, b = info['sourceBounds']
            backdrop_mask[y:b, x:r] = False
        if np.any(backdrop_mask):
            difference = np.abs(pixels(files[-1])[backdrop_mask] - rest[backdrop_mask])
            if np.median(np.max(difference, axis=1)) > .5 / 255:
                from video_opacity import refine_video_opacity
                refine_video_opacity(files, lambda path: color(pixels(path)), original_bg,
                                     case['elements'], result['tracks'], diagnostics, read_template, data['fps'])
    for track in result['tracks']:
        visible_start = next(key for key in track['keys'] if key['alpha'] > 0)
        print(json.dumps({'case': args.case, 'track': track['name'], 'visibleStart': visible_start}), flush=True)
    out=Path(args.out);out.parent.mkdir(parents=True,exist_ok=True)
    out.write_text(json.dumps(result,indent=2))
    out.with_suffix('.fit.json').write_text(json.dumps(diagnostics,indent=2))
    print(json.dumps({'output':str(out),'frames':len(files),'tracks':len(result['tracks'])}),flush=True)
