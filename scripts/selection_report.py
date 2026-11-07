"""A local, script-free preview of automatically selected existing UI groups."""
from pathlib import Path
import html,json

def write_selection_report(workspace):
 w=Path(workspace);d=json.loads((w/'destination.json').read_text(encoding='utf-8'))
 plan=json.loads((w/'auto-bindings.json').read_text(encoding='utf-8'))
 body=['<!doctype html><html lang="en"><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1"><title>Selected Unity UI</title><style>body{font:16px/1.6 system-ui;background:#121b28;color:#e8eff7;max-width:1100px;margin:32px auto;padding:0 20px}svg{width:100%;background:#202a38}table{border-collapse:collapse;width:100%;font-size:14px}td,th{padding:8px;text-align:left;border-bottom:1px solid #425169}code{overflow-wrap:anywhere}a{color:#8fcbff}section{margin:36px 0}</style><h1>Automatically selected Unity UI</h1>',f'<p>Execution: <b>{html.escape(plan["execution"])}</b>. Selection follows UI structure; review the selected groups before matching. Game outcomes have not been inferred or verified.</p>']
 for c in d['cases']:
  cp=next(x for x in plan['cases'] if x['caseName']==c['caseName'])
  body.append(f'<section><h2>{html.escape(cp["canvas"]["path"])}</h2><p><code>{html.escape(c["caseName"])}</code> · {html.escape(cp["canvas"]["scene"])}</p><svg viewBox="0 0 {d["width"]} {d["height"]}" role="img" aria-label="Selected UI target bounds"><image href="fixtures/{c["caseName"]}/rest.png" width="{d["width"]}" height="{d["height"]}"/>')
  for i,e in enumerate(c['elements'],1):
   x=e['x']-e['w']/2;y=e['y']-e['h']/2
   body.append(f'<rect x="{x}" y="{y}" width="{e["w"]}" height="{e["h"]}" fill="none" stroke="#52e2cf" stroke-width="2"/><text x="{max(3,x+4)}" y="{max(18,y+18)}" fill="#52e2cf" stroke="#122233" stroke-width=".5" font-size="20">{i}</text>')
  body.append('</svg><table><tr><th>#</th><th>Existing UI</th><th>Selector ID</th></tr>')
  for i,e in enumerate(c['elements'],1):body.append(f'<tr><td>{i}</td><td>{html.escape(e.get("displayName",e["name"]))}<br><small>{html.escape(e["path"])}</small></td><td><code>{html.escape(e["name"])}</code></td></tr>')
  body.append('</table></section>')
 body.append('<p>Details: <a href="auto-report.json">discovery report</a> · <a href="auto-bindings.json">bindings</a> · <a href="scene-inventory.json">scene inventory</a></p></html>')
 (w/'selection.html').write_text(''.join(body),encoding='utf-8')
