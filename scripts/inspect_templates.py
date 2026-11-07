import argparse,json
from pathlib import Path
import numpy as np
from PIL import Image

parser=argparse.ArgumentParser(description='Inspect extracted templates in a prepared workspace')
parser.add_argument('--workspace',required=True)
root = Path(parser.parse_args().workspace).resolve()
d = json.loads((root / 'destination.json').read_text(encoding='utf-8-sig'))
for c in d['cases']:
    bg = np.asarray(Image.open(root/'fixtures'/c['caseName']/'background.png'),float)[:,:,:3]
    rest = np.asarray(Image.open(root/'fixtures'/c['caseName']/'rest.png'),float)[:,:,:3]
    for e in c['elements']:
        t = np.asarray(Image.open(root/e['template']),float)
        a = t[:,:,3:4]/255
        mask = t[:,:,3]>0
        yy,xx = np.nonzero(mask)
        premul = bg*(1-a)+t[:,:,:3]
        straight = bg*(1-a)+t[:,:,:3]*a
        print(c['caseName'],repr(e['name']),[xx.min(),yy.min(),xx.max(),yy.max()],
              'premul',np.mean(abs(rest-premul)[mask]),'straight',np.mean(abs(rest-straight)[mask]))
