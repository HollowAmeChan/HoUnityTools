"""Transfer a user's six mouth anchors into the isolated 9/11-point tables.

Only MouthCore child positions change. Intermediate samples are interpolated
initial guesses, not measured values. Keeps controller GUIDs, clips and topology.
"""
from pathlib import Path
import argparse
import hashlib
import json
import re
from datetime import datetime

def parse(path):
    raw=path.read_bytes()
    text=raw.decode('utf-8-sig').replace('\r\n','\n')
    names={}
    blocks={}
    for m in re.finditer(r'(?ms)^--- !u!(\d+) &(-?\d+)\n(.*?)(?=^--- !u!|\Z)',text):
        n=re.search(r'(?m)^  m_Name: (.*)$',m[3])
        name=n[1] if n else ''
        if name.startswith('"'):name=json.loads(name)
        names[m[2]]=name;blocks[m[2]]=m
    target=next(m for k,m in blocks.items() if names[k]=='MouthCore')
    rx=r'(?s)m_Motion: \{fileID: (\d+)\}.*?m_Position: \{x: ([^,]+), y: ([^}]+)\}'
    points={names[m[1]]:(float(m[2]),float(m[3])) for m in re.finditer(rx,target[3])}
    return raw,text,names,target,points,rx

def lerp(a,b,t=.5):return tuple(x+(y-x)*t for x,y in zip(a,b))

def main():
    ap=argparse.ArgumentParser(description=__doc__)
    ap.add_argument('anchors',type=Path)
    ap.add_argument('targets',type=Path,nargs='+')
    ap.add_argument('--backup-dir',type=Path,required=True)
    args=ap.parse_args()
    src,_,_,_,anchors,_=parse(args.anchors)
    expected={'嘴苦闭','嘴平闭','嘴大笑闭','嘴苦满','嘴平满','嘴大笑满'}
    if set(anchors)!=expected:raise RuntimeError('Expected six named source anchors; source is never modified.')
    points=dict(anchors)
    for prefix in ['嘴苦','嘴平','嘴大笑']:
        points[prefix+'半张']=lerp(anchors[prefix+'闭'],anchors[prefix+'满'])
    points['嘴笑闭']=lerp(anchors['嘴平闭'],anchors['嘴大笑闭'])
    points['嘴笑半张']=lerp(points['嘴平半张'],points['嘴大笑半张'])
    pending=[]
    for path in args.targets:
        if path.resolve()==args.anchors.resolve():raise RuntimeError('Cannot write the anchor controller')
        raw,text,names,block,old,rx=parse(path)
        if len(old) not in [9,11] or not set(old)<=set(points):raise RuntimeError('Expected known 9/11-point table: '+str(path))
        count=0
        def replace(m):
            nonlocal count
            count+=1;x,y=points[names[m[1]]]
            return m[0][:m[0].rfind('m_Position:')]+f'm_Position: {{x: {x:.8g}, y: {y:.8g}}}'
        body=re.sub(rx,replace,block[3])
        if count!=len(old):raise RuntimeError('Position count mismatch')
        result=text[:block.start(3)]+body+text[block.end(3):]
        mask=lambda s:re.sub(r'm_Position: \{x: [^,]+, y: [^}]+\}','m_Position: {masked}',s)
        if mask(result)!=mask(text):raise RuntimeError('Change outside point positions')
        if b'\r\n' in raw:result=result.replace('\n','\r\n')
        encoded=result.encode('utf-8-sig' if raw.startswith(b'\xef\xbb\xbf') else 'utf-8')
        meta=path.with_name(path.name+'.meta')
        pending.append((path,raw,encoded,meta.read_bytes(),old))
    backup=args.backup_dir/datetime.now().strftime('%Y%m%d-%H%M%S-%f')
    backup.mkdir(parents=True,exist_ok=False)
    report={'source':str(args.anchors),'source_sha256':hashlib.sha256(src).hexdigest(),
            'measured_anchors':anchors,'interpolated_unmeasured':{k:v for k,v in points.items() if k not in anchors},'targets':[]}
    for path,raw,new,meta,old in pending:
        if path.read_bytes()!=raw:raise RuntimeError('Target changed during preparation: '+str(path))
        (backup/path.name).write_bytes(raw)
        path.write_bytes(new)
        if path.with_name(path.name+'.meta').read_bytes()!=meta:raise RuntimeError('GUID/meta changed')
        actual=parse(path)[4]
        if any(actual[k]!=tuple(v) for k,v in anchors.items()):raise RuntimeError('Measured anchor mismatch')
        report['targets'].append({'path':str(path),'point_count':len(actual),'before':old,'after':actual,'changed_only_positions':True})
    if args.anchors.read_bytes()!=src:raise RuntimeError('Source changed unexpectedly')
    (backup/'point-sync.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8')
    print(json.dumps({'backup':str(backup),'targets':[{'file':r['path'],'points':r['point_count']} for r in report['targets']],
                      'interpolated_unmeasured':report['interpolated_unmeasured'],'source_unchanged':True},ensure_ascii=True,indent=2))

if __name__=='__main__':main()
