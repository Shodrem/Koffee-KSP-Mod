"""Check exported node transforms and simulate the verified stock set-selection algorithm."""
from pathlib import Path
import sys,math,json
sys.dont_write_bytecode=True
SRC=Path(__file__).resolve().parent; OUT=SRC.parent; ROOT=SRC.parents[5]
sys.path.insert(0,str(ROOT/'Utils')); sys.path.insert(0,'D:/Juegos/blender-mu/io_object_mu')
from check_crew_chute import parse_cfg,objects,world,close
from mu import Mu
Mu.read_string=lambda m:m.file.read(m.read_7int()).decode('utf-8',errors='strict')

def check_nodes():
    cfg=parse_cfg(OUT/'TechnicalPilonTP25.cfg'); mu=Mu().read(str(OUT/'SHO_Technical_Pilon_TP25.mu'))
    entries={o.transform.name:(o,c) for o,c in objects(mu.obj)}
    nodes={n['values']['name']:n['values'] for n in cfg['nodes'] if n['name']=='NODE'}
    assert set(nodes)=={'top','bottom'}|{'face_'+str(i) for i in range(1,7)}
    assert not any(k.startswith('node_stack_') for k in cfg['values'])
    report={'result':'PASS','nodes':{},'sets':[]}
    for node_id,n in nodes.items():
        assert n['size']=='0' and n['method']=='FIXED_JOINT'
        obj,chain=entries[n['transform']]
        origin=world(chain,(0,0,0)); forward=tuple(a-c for a,c in zip(world(chain,(0,1,0)),origin))
        if node_id in ('top','bottom'):
            sign=1 if node_id=='top' else -1
            assert n['transform']==('attach_North' if sign>0 else 'attach_South')
            expected=(0,sign*.5,0); direction=(0,sign,0)
        else:
            i=int(node_id.split('_')[1])-1; a=i*math.pi/3
            expected=(.590*math.sin(a),-.005,-.590*math.cos(a)); direction=(math.sin(a),0,-math.cos(a))
            assert n['transform']=='attach_Face'+str(i+1).zfill(2)
            assert chain[-2].name=='TP25_FaceNodes'
            # The centre is on the actual flat white mounting panel, not the hex corner.
            assert abs(math.hypot(origin[0],origin[2])-.59)<1e-6
        close(origin,expected); close(forward,direction)
        report['nodes'][node_id]={'transform':n['transform'],'position_ksp_m':origin,'direction_ksp':forward}
    modules=[n for n in cfg['nodes'] if n['name']=='MODULE']; assert len(modules)==7
    selector=modules[0]
    assert selector['values']=={'name':'ModulePartVariants','baseVariant':'Surface','useMultipleDragCubes':'false'}
    variants=[n for n in selector['nodes'] if n['name']=='VARIANT']; assert len(variants)==2
    assert [v['values']['name'] for v in variants]==['Surface','FaceNodes']
    controlled=[]
    for controller in modules[1:]:
        c=controller['values']
        assert c['name']=='ModuleKoffeeDockingVisibility'
        assert c['visibilityTransform']=='TP25_FaceNodes' and c['controlDockingPorts']=='false'
        controlled.append(c['controlledAttachNode'])
    assert set(controlled)=={'face_'+str(i) for i in range(1,7)} and len(controlled)==6
    loc=parse_cfg(OUT/'Localization.cfg')
    for index,v in enumerate(variants):
        s=v['values']; assert s['mass']=='0' and s['cost']=='0'
        for lang in loc['nodes']: assert s['displayName'] in lang['values']
        objects_state=next(n['values'] for n in v['nodes'] if n['name']=='GAMEOBJECTS')
        assert objects_state=={'TP25_SurfaceConfiguration':'true' if index==0 else 'false',
                               'TP25_FaceNodes':'true' if index==1 else 'false'}
        assert set(objects_state)<=set(entries)
        assert all(n['name']=='GAMEOBJECTS' for n in v['nodes'])
    # Existing Koffee controller responds to onVariantApplied and LateUpdate;
    # it hides/restores type, radius and icon, retaining node and attached-part references.
    for target in (0,1,0,1,0):
        states={key:True for key in nodes}
        for node in controlled: states[node]=bool(target)
        active={key for key,value in states.items() if value}
        expected={'top','bottom'}|({'face_'+str(i) for i in range(1,7)} if target else set())
        assert active==expected,(target,active)
    for i,v in enumerate(variants):
        report['sets'].append({'variant':v['values']['name'],'localization':v['values']['displayName'],'active_nodes':2 if i==0 else 8})
    report['selector']='ModulePartVariants'
    report['node_controller']='Existing ModuleKoffeeDockingVisibility, six node-only instances'
    report['configuration_simulation']='PASS, Surface -> FaceNodes -> Surface -> FaceNodes -> Surface'
    report['ksp_tests_performed_by_this_script']=False
    report['separate_runtime_evidence']='paw_selector_validation.json'
    return report

if __name__=='__main__':
    report=check_nodes(); (SRC/'node_validation.json').write_bytes((json.dumps(report,indent=2)+'\n').encode('utf-8'))
    print(json.dumps(report,indent=2))
