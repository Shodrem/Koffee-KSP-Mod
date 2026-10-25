"""Read the actual stock and ED-01 runtime MU; never save source references."""
from pathlib import Path
import sys, json, hashlib
sys.dont_write_bytecode = True
OUT = Path(__file__).resolve().parent
ROOT = OUT.parents[5]
sys.path.insert(0, str(ROOT/'Utils'))
sys.path.insert(0, 'D:/Juegos/blender-mu/io_object_mu')
from check_crew_chute import objects, world
from mu import Mu
Mu.read_string = lambda m: m.file.read(m.read_7int()).decode('utf-8', errors='strict')
SOURCES = {
    'bay': Path('D:/Juegos/KSP/GameData/Squad/Parts/Utility/ServiceBay_v2/Assets/ServiceBay_250_v2.mu'),
    'ed01': ROOT/'GameData/ShoAerospace/Koffee/Parts/Experiments_Device_ED-01/Experiment_Device_ED01.mu'}
report = {}
for key, path in SOURCES.items():
    mu = Mu().read(str(path)); rows = []; allpoints = []
    saved_root = vars(mu.obj.transform).copy()
    # PartLoader resets the MODEL root transform; source export offsets are not part dimensions.
    mu.obj.transform.localPosition = (0,0,0)
    mu.obj.transform.localRotation = (1,0,0,0)
    mu.obj.transform.localScale = (1,1,1)
    for o,c in objects(mu.obj):
        row = {'name':o.transform.name, 'position':o.transform.localPosition, 'rotation':o.transform.localRotation}
        if hasattr(o,'renderer'):
            pts = [world(c,v) for v in o.shared_mesh.verts]; allpoints += pts
            row['bounds'] = [[min(v[i] for v in pts), max(v[i] for v in pts)] for i in range(3)]
        if hasattr(o,'collider'):
            col=o.collider; row['collider']={k:v for k,v in vars(col).items() if k!='mesh'}
            if hasattr(col,'mesh'):
                pts=[world(c,v) for v in col.mesh.verts]
                row['collider']['bounds']=[[min(v[i] for v in pts),max(v[i] for v in pts)] for i in range(3)]
                row['collider']['vertices']=len(pts)
        if hasattr(o,'animation'):
            row['clips']=[{'name':cl.name,'curves':[{'path':cv.path,'property':cv.property,'keys':[vars(k) for k in cv.keys]} for cv in cl.curves]} for cl in o.animation.clips]
        rows.append(row)
    report[key]={'path':str(path),'sha256':hashlib.sha256(path.read_bytes()).hexdigest(),'source_root':saved_root,'objects':rows,
                 'renderer_bounds_unscaled':[[min(v[i] for v in allpoints),max(v[i] for v in allpoints)] for i in range(3)]}
(OUT/'source_audit.json').write_text(json.dumps(report,indent=2)+'\n',encoding='utf-8')
for key,item in report.items():
    print(key,item['renderer_bounds_unscaled'])
    for row in item['objects']:
        if 'collider' in row: print(row['name'],row['collider'].get('bounds'))
        if 'clips' in row: print('clips',[(cl['name'],len(cl['curves'])) for cl in row['clips']])
