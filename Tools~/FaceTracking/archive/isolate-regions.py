"""Make non-destructive, reachable-subtree controller copies for regional A/B tests.

Supports the project's single-state static-pose controller. Does not change the
source, its clips, or the selected controller/profile in the debug settings.
"""
from pathlib import Path
import argparse
import copy
import hashlib
import json
import re
import uuid

REUSE_IDENTICAL=False

def parse(text):
    blocks={}
    for m in re.finditer(r'(?ms)^--- !u!(\d+) &(-?\d+)\n(.*?)(?=^--- !u!|\Z)',text):
        name=re.search(r'(?m)^  m_Name: (.*)$',m[3])
        value=name[1] if name else ''
        if value.startswith('"'):value=json.loads(value)
        blocks[m[2]]={'kind':int(m[1]),'name':value,'body':m[3]}
    return blocks

def local_refs(body):
    return [m[1] for m in re.finditer(r'\{fileID: (-?\d+)\}',body) if m[1]!='0']

def tree_body(name,parameter,children):
    s=f'BlendTree:\n  m_ObjectHideFlags: 0\n  m_CorrespondingSourceObject: {{fileID: 0}}\n  m_PrefabInstance: {{fileID: 0}}\n  m_PrefabAsset: {{fileID: 0}}\n  m_Name: {name}\n  m_Childs:\n'
    for ident,threshold in children:
        s+=f'  - serializedVersion: 2\n    m_Motion: {{fileID: {ident}}}\n    m_Threshold: {threshold:g}\n    m_Position: {{x: 0, y: 0}}\n    m_TimeScale: 1\n    m_CycleOffset: 0\n    m_DirectBlendParameter: Ho/Drive/W/One\n    m_Mirror: 0\n'
    return s+f'  m_BlendParameter: {parameter}\n  m_BlendParameterY: Blend\n  m_MinThreshold: {min(x[1] for x in children):g}\n  m_MaxThreshold: {max(x[1] for x in children):g}\n  m_UseAutomaticThresholds: 0\n  m_NormalizedBlendValues: 0\n  m_BlendType: 0\n'

def write_new(path,text):
    if path.exists():
        if REUSE_IDENTICAL and path.read_text(encoding='utf-8-sig')==text:return
        raise RuntimeError('Refusing to overwrite different content: '+str(path))
    path.write_text(text,encoding='utf-8',newline='\n')
    path.with_name(path.name+'.meta').write_text('fileFormatVersion: 2\nguid: '+uuid.uuid4().hex+'\n',encoding='ascii')

def main():
    global REUSE_IDENTICAL
    ap=argparse.ArgumentParser(description=__doc__)
    ap.add_argument('controller',type=Path);ap.add_argument('profile',type=Path);ap.add_argument('output',type=Path)
    ap.add_argument('--append-identical',action='store_true',help='Add missing diagnostics to this tool\'s folder; existing assets must have identical content.')
    args=ap.parse_args()
    original=args.controller.read_bytes();text=original.decode('utf-8-sig').replace('\r\n','\n')
    old_manifest=args.output/'isolation-manifest.json'
    if args.output.exists():
        if not args.append_identical or not old_manifest.exists():raise RuntimeError('Choose a new output directory, or use --append-identical on an existing diagnostic folder.')
        prior=json.loads(old_manifest.read_text(encoding='utf-8-sig'))
        if prior.get('sha256')!=hashlib.sha256(original).hexdigest():raise RuntimeError('Source changed; choose a new output directory.')
    REUSE_IDENTICAL=args.append_identical
    blocks=parse(text);states=[k for k,b in blocks.items() if b['kind']==1102]
    if len(states)!=1:raise RuntimeError('Expected exactly one static-pose state; refusing to guess a state machine rewrite.')
    byname={b['name']:k for k,b in blocks.items() if b['kind']==206}
    rootid=byname['MouthCore']
    points=[]
    for m in re.finditer(r'(?s)m_Motion: \{fileID: (\d+)\}.*?m_Position: \{x: ([^,]+), y: ([^}]+)\}',blocks[rootid]['body']):
        points.append((m[1],float(m[2]),float(m[3]),blocks[m[1]]['name']))
    if len(points)!=11 or not all('闭' in p[3] for p in points[:4]):
        raise RuntimeError('Row experiment expects the audited 11-point layout: 4 closed, 4 half-open, 3 full-open.')
    args.output.mkdir(parents=True,exist_ok=True)
    folder_meta=args.output.with_name(args.output.name+'.meta')
    if not folder_meta.exists():folder_meta.write_text('fileFormatVersion: 2\nguid: '+uuid.uuid4().hex+'\nfolderAsset: yes\n',encoding='ascii')
    records=[]
    variants=[('MouthCore_Original','MouthCore','original'),('MouthCore_FlatClosed','MouthCore','flat'),('MouthCore_Rows','MouthCore','rows'),
              ('MouthCore_Aligned11','MouthCore','aligned11'),('MouthCore_Aligned9','MouthCore','aligned9'),('MouthCore_Aligned6','MouthCore','aligned6'),
              ('VTS_Full_RowsCandidate','Ho/00 Drive Tree','fullrows')]
    variants += [(n,n,'original') for n in byname if n.endswith('Region')]
    for name,subtree,mode in variants:
        b=copy.deepcopy(blocks);selected=byname[subtree]
        if mode=='flat':
            def flatten(m):
                ident=m[1];y='0' if '闭' in b[ident]['name'] else m[3]
                return m[0][:m[0].rfind('m_Position:')]+f'm_Position: {{x: {m[2]}, y: {y}}}'
            b[selected]['body']=re.sub(r'(?s)m_Motion: \{fileID: (\d+)\}.*?m_Position: \{x: ([^,]+), y: ([^}]+)\}',flatten,b[selected]['body'])
        elif mode.startswith('aligned'):
            prefix,remainder=b[selected]['body'].split('  m_Childs:\n',1)
            childtext,tail=remainder.split('  m_BlendParameter:',1)
            children=['  - serializedVersion: '+x for x in childtext.split('  - serializedVersion: ')[1:]]
            if len(children)!=11:raise RuntimeError('Unexpected 2D children')
            columns=[0,1,2,3,0,1,2,3,0,1,3]
            indices=list(range(11)) if mode=='aligned11' else [0,1,3,4,5,7,8,9,10] if mode=='aligned9' else [0,1,3,8,9,10]
            kept=[]
            for i in indices:
                x=points[columns[i]][1];y=0 if i<4 else points[i][2]
                kept.append(re.sub(r'm_Position: \{x: [^,]+, y: [^}]+\}',f'm_Position: {{x: {x:g}, y: {y:g}}}',children[i]))
            b[selected]['body']=prefix+'  m_Childs:\n'+''.join(kept)+'  m_BlendParameter:'+tail
        elif mode in ('rows','fullrows'):
            targets=[selected] if mode=='rows' else [byname[n] for n in ['MouthCore','MouthCoreRoll'] if n in byname]
            for target in targets:
                target_points=[]
                for m in re.finditer(r'(?s)m_Motion: \{fileID: (\d+)\}.*?m_Position: \{x: ([^,]+), y: ([^}]+)\}',b[target]['body']):
                    target_points.append((m[1],float(m[2])))
                if len(target_points)!=11:raise RuntimeError('Expected 11 points in '+b[target]['name'])
                nextid=max(int(k) for k in b)+1;rows=[]
                for row,indices,threshold in [('Closed',range(4),0),('Half',range(4,8),.48),('Full',range(8,11),.75)]:
                    ident=str(nextid);nextid+=1
                    rowname=('Diagnostic' if mode=='rows' else b[target]['name']+'_')+row
                    b[ident]={'kind':206,'name':rowname,'body':tree_body(rowname,'Ho/Drive/Mouth/Form',[target_points[i] for i in indices])}
                    rows.append((ident,threshold))
                b[target]['body']=tree_body(b[target]['name']+'Rows','Ho/Drive/Mouth/Open',rows)
        b[states[0]]['body'],count=re.subn(r'(?m)^  m_Motion: \{fileID: -?\d+\}$',f'  m_Motion: {{fileID: {selected}}}',b[states[0]]['body'])
        if count!=1:raise RuntimeError('Could not identify the unique state motion')
        keep={k for k,v in b.items() if v['kind']!=206};queue=list(keep)
        while queue:
            k=queue.pop()
            for ref in local_refs(b[k]['body']):
                if ref not in b:raise RuntimeError('Unresolved local fileID: '+ref)
                if ref not in keep:keep.add(ref);queue.append(ref)
        for k in keep:
            if b[k]['kind']==91:
                b[k]['body']=re.sub(r'(?m)^  m_Name: .*$','  m_Name: DIAG_'+name,b[k]['body'])
        out='%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n'+''.join(f'--- !u!{v["kind"]} &{k}\n{v["body"]}' for k,v in b.items() if k in keep)
        dest=args.output/('DIAG_'+name+'.controller');write_new(dest,out)
        records.append({'file':dest.name,'source_subtree':subtree,'mode':mode,'trees':sum(b[k]['kind']==206 for k in keep)})

    source_profile=json.loads(args.profile.read_text(encoding='utf-8-sig'))
    outputs={r['parameter']:r for r in source_profile['outputs']}
    smile='(mouthSmileLeft + mouthSmileRight) / 2'
    frown='(mouthFrownLeft + mouthFrownRight) / 2'
    dimple='(mouthDimpleLeft + mouthDimpleRight) / 4'
    residual='clamp(((mouthStretchLeft + mouthStretchRight) / 2 - (0.42 * jawOpen + 0.05)) * 1.5, 0, 1)'
    vb_form=f'({smile}) - ({frown}) + ({dimple}) - mouthPucker / 2'
    vb_open='jawOpen - mouthClose - .2 * (mouthRollUpper + mouthRollLower) + .2 * mouthFunnel'
    terms={'SmileMean':smile,'FrownMean':frown,'DimpleContribution':dimple,'PuckerContribution':'mouthPucker / 2',
           'StretchSadCandidate':residual,'StretchExtraPenalty':f'max(({residual}) - ({frown}), 0)',
           'FormMinimal':f'({smile}) - ({frown})','FormVBFormula':vb_form,
           'FormCurrentFormula':outputs['Ho/Drive/Mouth/Form']['expression'],
           'OpenJaw':'jawOpen','OpenJawClose':'jawOpen-mouthClose','OpenVBFormula':vb_open}
    identity={'keys':[{'t':-2,'v':-2,'inT':1,'outT':1},{'t':2,'v':2,'inT':1,'outT':1}]}
    choices={'Minimal':(f'({smile}) - ({frown})','clamp(jawOpen-mouthClose,0,1)'),
             'VBFormula':(vb_form,vb_open),
             'Current':(outputs['Ho/Drive/Mouth/Form']['expression'],outputs['Ho/Drive/Mouth/Open']['expression'])}
    for mode,(form,opening) in choices.items():
        p={k:copy.deepcopy(v) for k,v in source_profile.items() if k!='outputs'}
        p['displayName']='DIAG Mouth '+mode
        p['notes']='Isolated MouthCore comparison. Keeps the SAME source input calibration, Form/Open output curves and smoothing. VBFormula is formula-only, not complete VBridger/VTS behavior. Does not change production assets.'
        p['outputs']=[{'parameter':'Debug/Mouth/'+k,'expression':v,'defaultValue':0,'curve':copy.deepcopy(identity),'modifiers':[]} for k,v in terms.items()]
        for key,eq in [('Ho/Drive/Mouth/Form',form),('Ho/Drive/Mouth/Open',opening)]:
            r=copy.deepcopy(outputs[key]);r['expression']=eq;r['notes']='Diagnostic equation; original output transfer and modifiers retained for comparison.';p['outputs'].append(r)
        p['outputs'].append({'parameter':'Ho/Drive/W/One','expression':'','defaultValue':1,'curve':copy.deepcopy(identity),'modifiers':[]})
        write_new(args.output/('DIAG_Mouth_'+mode+'.hoface.json'),json.dumps(p,ensure_ascii=False,indent=2)+'\n')
    manual={k:copy.deepcopy(v) for k,v in source_profile.items() if k not in ['inputs','outputs']}
    manual['displayName']='DIAG Mouth Manual'
    manual['notes']='No camera required. Edit the constant defaultValue of Form/Open in the profile editor. Open=0 is the closure-boundary test.'
    manual['inputs']=[]
    manual['outputs']=[{'parameter':n,'expression':'','defaultValue':v,'curve':copy.deepcopy(identity),'modifiers':[]}
                       for n,v in [('Ho/Drive/Mouth/Form',0),('Ho/Drive/Mouth/Open',0),('Ho/Drive/W/One',1)]]
    write_new(args.output/'DIAG_Mouth_Manual.hoface.json',json.dumps(manual,ensure_ascii=False,indent=2)+'\n')
    assert hashlib.sha256(args.controller.read_bytes()).digest()==hashlib.sha256(original).digest()
    manifest={'source':str(args.controller),'sha256':hashlib.sha256(original).hexdigest(),'controllers':records,
              'row_open_thresholds':[0,.48,.75],'warning':'Rows changes the coordinate model; re-calibrate before production use. FlatClosed is a diagnostic, not guaranteed to prevent open-pose weights.'}
    manifest_text=json.dumps(manifest,ensure_ascii=False,indent=2)+'\n'
    if args.append_identical and old_manifest.exists():old_manifest.write_text(manifest_text,encoding='utf-8',newline='\n')
    else:write_new(old_manifest,manifest_text)
    print(json.dumps({'created_controllers':records,'profiles':list(choices)+['Manual'],'source_unchanged':True},ensure_ascii=True,indent=2))

if __name__=='__main__':main()
