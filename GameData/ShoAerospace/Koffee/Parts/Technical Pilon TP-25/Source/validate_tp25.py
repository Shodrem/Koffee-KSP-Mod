"""Independent runtime-MU checks and conservative convex-envelope clearance.
Not a KSP editor, physics, shielding or flight test. Doors sampled at 241 instants.
"""
from pathlib import Path
import sys, math, itertools, json, hashlib
import numpy as np
from mathutils import Vector, Matrix
sys.dont_write_bytecode=True
SRC=Path(__file__).resolve().parent; sys.path.insert(0,str(SRC))
import build_tp25 as b
OUT=b.OUT; ROOT=b.ROOT

def points(o,c,collider=False):
    me=o.collider.mesh if collider else o.shared_mesh
    return np.array([b.world(c,v) for v in me.verts])

def unique_directions(axes):
    axes=np.array(axes).reshape(-1,3); lengths=np.linalg.norm(axes,axis=1); axes=axes[lengths>1e-8]/lengths[lengths>1e-8,None]
    for row in axes:
        for value in row:
            if abs(value)>1e-6:
                if value<0: row*=-1
                break
    return np.unique(np.round(axes,6),axis=0)

def hull(verts,faces):
    edges=[]; normals=[]
    for face in faces:
        p=verts[list(face)]; normals.append(np.cross(p[1]-p[0],p[2]-p[0]))
        edges.extend(p[(i+1)%len(p)]-p[i] for i in range(len(p)))
    return (verts,unique_directions(normals),unique_directions(edges))

def sat(a,b):
    va,na,ea=a; vb,nb,eb=b
    cross=np.cross(ea[:,None,:],eb[None,:,:]).reshape(-1,3)
    axes=unique_directions(np.concatenate((na,nb,cross)))
    pa=va@axes.T; pb=vb@axes.T
    gaps=np.maximum(pb.min(axis=0)-pa.max(axis=0),pa.min(axis=0)-pb.max(axis=0))
    return float(gaps.max())

BOXF=[(0,1,3,2),(4,6,7,5),(0,4,5,1),(2,3,7,6),(0,2,6,4),(1,5,7,3)]
def box(lo,hi,placement=Matrix.Identity(4)):
    # Bounds are KSP XYZ; placement matrices use the same coordinate space here.
    v=np.array([tuple(placement@Vector(p)) for p in itertools.product(*zip(lo,hi))])
    return hull(v,BOXF)

def main():
    cfg=b.parse_cfg(OUT/'TechnicalPilonTP25.cfg'); values=cfg['values']
    assert values['name']=='SHOTechnicalPilonTP25' and values['rescaleFactor']=='1' and values['scale']=='1'
    assert values['manufacturer']=='#LOC_Koffee_Manufacturer' and values['attachRules']=='1,0,1,1,0'
    assert values['category']=='Utility'
    from check_tp25_nodes import check_nodes
    node_report=check_nodes()
    model=next(n for n in cfg['nodes'] if n['name']=='MODEL')['values']['model']
    assert ROOT/'GameData'/(model+'.mu')==OUT/'SHO_Technical_Pilon_TP25.mu'
    mu=b.Mu().read(str(OUT/'SHO_Technical_Pilon_TP25.mu')); entries=list(b.objects(mu.obj))
    colliders=[]; renders=[]
    for o,c in entries:
        assert not hasattr(o,'animation')
        if hasattr(o,'renderer'):
            renders.append(points(o,c)); me=o.shared_mesh
            assert len(me.verts)==len(me.uvs)==len(me.normals)==len(me.tangents)
            assert np.isfinite(np.array(me.tangents)).all()
        if hasattr(o,'collider'):
            assert o.collider.isTrigger==0 and o.collider.convex==1
            assert o.transform.name.startswith('TP25_')
            v=points(o,c,True); fs=[t for sm in o.collider.mesh.submeshes for t in sm]
            colliders.append((o.transform.name,hull(v,fs)))
    assert len(colliders)==26,len(colliders)
    allv=np.concatenate(renders); lo=allv.min(axis=0); hi=allv.max(axis=0)
    assert abs(lo[1]+.5)<1e-6 and abs(hi[1]-.5)<1e-6
    assert len(mu.materials)==1 and mu.materials[0].shaderName=='KSP/Bumped Specular'
    for tex in mu.textures: assert (OUT/tex.name).is_file(),tex.name
    assert {t.name for t in mu.textures}=={'TP25_AlbedoSpecular.png','TP25_Normal.png'}
    loc=b.parse_cfg(OUT/'Localization.cfg'); assert {n['name'] for n in loc['nodes']}=={'es-es','en-us'}
    expected='Para todas tus necesidades de conectar cosas dentro de una bahía, incluso los experimentos de SHO Aerospace. En su interior incluye una matriz hipertrónica multidimensional cableada de modo afásico para permitir la circulación eléctrica en ambos sentidos a la velocidad de la luz. Advertencia, se pierde la garantía si se expone al vacío.'
    for n in loc['nodes']: assert n['values']['#LOC_Koffee_TP25_Title']=='SHO Technical Pilon TP-25'
    assert loc['nodes'][0]['values']['#LOC_Koffee_TP25_Description']==expected+' Incluye una batería de 200 EC y una reserva de 20 unidades de monopropelente.'
    from check_tp25_resources import check_resources
    check_resources()
    # Verify new IDs against every active CFG, without changing unrelated files.
    occurrences=[]
    for path in (ROOT/'GameData').rglob('*.cfg'):
        if any(p.lower()=='old' for p in path.parts): continue
        text=path.read_bytes().decode('utf-8',errors='strict')
        if 'name = SHOTechnicalPilonTP25' in text: occurrences.append(path)
    assert occurrences==[OUT/'TechnicalPilonTP25.cfg'],occurrences
    for p in OUT.rglob('*'):
        if p.suffix in ('.cfg','.py','.md','.json','.log'):
            raw=p.read_bytes(); assert not raw.startswith((b'\xef\xbb\xbf',b'\xff\xfe',b'\xfe\xff')),str(p)
            raw.decode('utf-8',errors='strict')
    audit=json.loads((SRC/'source_audit.json').read_text(encoding='utf-8'))
    bay=b.clean_mu(Path(audit['bay']['path'])); ed=b.clean_mu(Path(audit['ed01']['path']))
    edcfg=b.parse_cfg(Path(audit['ed01']['path']).with_suffix('.cfg'))['values']; scale=float(edcfg['rescaleFactor']); assert scale==.75
    ep=np.concatenate([points(o,c)*scale for o,c in b.objects(ed.obj) if hasattr(o,'renderer')])
    elo=ep.min(axis=0); ehi=ep.max(axis=0)
    # Envelope transformed from the actual current ED-01 rendered bounds and attach node.
    edboxes=[]
    for i in range(6):
        # Blender horizontal rotation corresponds to KSP Y with opposite sign.
        ang=-i*math.pi/3
        place=Matrix.Rotation(ang,4,'Y')@Matrix.Translation((0,-.02,-.7205))@Matrix.Rotation(math.pi,4,'Y')
        edboxes.append(box(elo,ehi,place))
    edgaps=[sat(a,c) for a,c in itertools.combinations(edboxes,2)]; assert min(edgaps)>0
    # Exact piece collider hulls + conservative ED rendered boxes; exclude no stock collider.
    assemblies=[h for _,h in colliders]+edboxes
    # Also include one enclosing hexagonal prism for all rendered cables, bevels and accessories.
    radius=.595/math.cos(math.pi/6)
    hv=np.array([(radius*math.cos(-math.pi/3+i*math.pi/3),y,radius*math.sin(-math.pi/3+i*math.pi/3)) for y in (-.5,.5) for i in range(6)])
    hfaces=[tuple(range(5,-1,-1)),tuple(range(6,12))]+[(i,(i+1)%6,(i+1)%6+6,i+6) for i in range(6)]
    assemblies.append(hull(hv,hfaces))
    offsets=(-.05,.05); results=[]
    for offset in offsets:
        shifted=[(v+np.array([0,offset,0]),n,e) for v,n,e in assemblies]
        minimum=1e9; radial_min=1e9; culprit=None
        for t in np.linspace(0,1,241):
            b.animate_bay(bay,float(t))
            for o,c in b.objects(bay.obj):
                if not hasattr(o,'collider') or o.collider.isTrigger: continue
                assert hasattr(o.collider,'mesh') and o.collider.convex
                v=points(o,c,True); fs=[t for sm in o.collider.mesh.submeshes for t in sm]; h=hull(v,fs)
                for j,a in enumerate(shifted):
                    gap=sat(a,h)
                    assert gap>1e-6,('collision',offset,float(t),o.transform.name,j,gap)
                    if gap<minimum: minimum=gap; culprit=[float(t),o.transform.name,j]
                    if o.transform.name not in ('COL_BOT','COL_TOP'): radial_min=min(radial_min,gap)
        results.append({'rack_y_offset_m':offset,'sample_count':241,'minimum_separating_axis_gap_m':minimum,'limiting_pair':culprit,'minimum_wall_door_separating_axis_gap_m':radial_min})
    # ED visual envelope against the rack colliders; existing rear node leaves a small real visual gap.
    rackgaps=[sat(e,c) for e in edboxes for _,c in colliders]; assert min(rackgaps)>0
    report={'result':'PASS','scope':'Offline geometry and configuration only; no KSP execution','node_configurations':node_report,
            'runtime_bounds_ksp_xyz_m':[lo.tolist(),hi.tolist()],'render_triangles':sum(len(s) for o,c in entries if hasattr(o,'renderer') for s in o.shared_mesh.submeshes),
            'convex_nontrigger_colliders':len(colliders),'ed01_runtime_dimensions_m':(ehi-elo).tolist(),
            'ed01_pair_min_separating_axis_gap_m':min(edgaps),'ed01_rack_min_separating_axis_gap_m':min(rackgaps),
            'bay_door_sweep':results,'sweep_interpolation':'241 cubic Hermite curve samples, normalized quaternions; finite sampling, not continuous proof',
            'clearance_method':'SAT, exact convex stock colliders and rack colliders, conservative current ED renderer OBBs and rack enclosing hex prism; reported gap is a conservative projected separation, not exact surface distance',
            'ksp_editor_test':False,'ksp_flight_test':False,'aerodynamic_shielding_test':False}
    (SRC/'validation.json').write_text(json.dumps(report,indent=2)+'\n',encoding='utf-8')
    print(json.dumps(report,indent=2),flush=True)

if __name__=='__main__': main()
