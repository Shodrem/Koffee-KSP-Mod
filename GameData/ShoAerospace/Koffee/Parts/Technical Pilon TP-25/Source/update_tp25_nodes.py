"""Update only attachment empties in the delivered Blender and re-export its MU."""
from pathlib import Path
import sys,json
import bpy
sys.dont_write_bytecode=True
SRC=Path(__file__).resolve().parent; sys.path.insert(0,str(SRC))
import build_tp25 as b
OUT=b.OUT
old=b.Mu().read(str(OUT/'SHO_Technical_Pilon_TP25.mu'))
bpy.ops.wm.open_mainfile(filepath=str(OUT/'SHO_Technical_Pilon_TP25.blend'),load_ui=False,use_scripts=False)
bpy.context.preferences.filepaths.save_version=0
root=bpy.data.objects['SHOTechnicalPilonTP25']; collection=bpy.data.collections['TP25_ASSET_ONLY']
names={'TP25_attach_top','TP25_attach_bottom','attach_North','attach_South','TP25_SurfaceConfiguration','TP25_FaceNodes'}
for obj in list(root.children_recursive):
    if obj.name.startswith('attach_Face') or obj.name in names: bpy.data.objects.remove(obj,do_unlink=True)
b.add_attachment_transforms(root,collection)
bpy.context.view_layer.update()
from io_object_mu.export_mu.export import export_object
exported=export_object(root,str(OUT/'SHO_Technical_Pilon_TP25.mu')); assert not exported.messages,exported.messages
new=b.Mu().read(str(OUT/'SHO_Technical_Pilon_TP25.mu'))
old.obj.children=[o for o in old.obj.children if o.transform.name not in names]
new.obj.children=[o for o in new.obj.children if o.transform.name not in names]
deltas=[]
def compare(a,c,path=''):
    assert type(a)==type(c),(path,type(a),type(c))
    if isinstance(a,(list,tuple)):
        assert len(a)==len(c),path
        for i,(x,y) in enumerate(zip(a,c)): compare(x,y,path+'/'+str(i))
    elif isinstance(a,dict):
        assert a.keys()==c.keys(),path
        for key in a: compare(a[key],c[key],path+'/'+key)
    elif hasattr(a,'__dict__'):
        assert vars(a).keys()==vars(c).keys(),path
        for key in vars(a): compare(vars(a)[key],vars(c)[key],path+'/'+key)
    elif a!=c:
        assert '/tangents/' in path and isinstance(a,float) and abs(a-c)<=1e-6,(path,a,c)
        deltas.append(abs(a-c))
for field in ('obj','textures','materials'): compare(getattr(old,field),getattr(new,field),field)
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'SHO_Technical_Pilon_TP25.blend'))
report={'result':'PASS','only_attachment_transforms_changed':True,'render_geometry_colliders_materials_textures_exact':True,'tangent_max_float_delta':max(deltas,default=0)}
(SRC/'node_update_validation.json').write_text(json.dumps(report,indent=2)+'\n',encoding='utf-8')
print(json.dumps(report,indent=2),flush=True)
