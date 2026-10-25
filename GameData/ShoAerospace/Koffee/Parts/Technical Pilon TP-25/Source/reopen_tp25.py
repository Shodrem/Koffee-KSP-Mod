"""Reopen the delivered Blender file and verify re-export and external texture channels."""
from pathlib import Path
import sys,json
import bpy, numpy as np
sys.dont_write_bytecode=True
SRC=Path(__file__).resolve().parent; sys.path.insert(0,str(SRC))
import build_tp25 as b
from io_object_mu.export_mu.export import export_object
OUT=b.OUT
bpy.ops.wm.open_mainfile(filepath=str(OUT/'SHO_Technical_Pilon_TP25.blend'),load_ui=False,use_scripts=False)
root=bpy.data.objects['SHOTechnicalPilonTP25']
assert tuple(root.location)==(0,0,0) and tuple(root.scale)==(1,1,1)
assert len(root.children)==31,len(root.children)
assert len(bpy.data.objects['TP25_VisibleStructure'].vertex_groups)>60
report={'result':'PASS','editable_component_vertex_groups':len(bpy.data.objects['TP25_VisibleStructure'].vertex_groups)}
for im in bpy.data.images:
    if im.source=='FILE' and not im.packed_file:
        path=Path(bpy.path.abspath(im.filepath)).resolve(); assert path.is_file() and path.is_relative_to(OUT),str(path)
        assert im.filepath.startswith('//'),im.filepath
tmp=SRC/'TP25_Roundtrip.mu'; mu=export_object(root,str(tmp)); assert not mu.messages,mu.messages
a=b.Mu().read(str(OUT/'SHO_Technical_Pilon_TP25.mu')); c=b.Mu().read(str(tmp))
deltas=[]
def compare(x,y,path=''):
    assert type(x)==type(y),(path,type(x),type(y))
    if isinstance(x,(tuple,list)):
        assert len(x)==len(y),path
        for i,(v,w) in enumerate(zip(x,y)): compare(v,w,path+'/'+str(i))
    elif isinstance(x,dict):
        assert x.keys()==y.keys(),path
        for key in x: compare(x[key],y[key],path+'/'+key)
    elif hasattr(x,'__dict__'):
        assert vars(x).keys()==vars(y).keys(),path
        for key in vars(x): compare(getattr(x,key),getattr(y,key),path+'/'+key)
    elif x!=y:
        assert '/tangents/' in path and isinstance(x,float) and abs(x-y)<=1e-6,(path,x,y)
        deltas.append(abs(x-y))
for field in ('obj','textures','materials'): compare(getattr(a,field),getattr(c,field),field)
report['reexport_identical_bytes']=tmp.read_bytes()==(OUT/'SHO_Technical_Pilon_TP25.mu').read_bytes()
report['geometry_colliders_materials_exact']=True; report['tangent_max_float_delta']=max(deltas,default=0)
tmp.unlink()
def pixels(path):
    im=bpy.data.images.load(str(path),check_existing=False); im.colorspace_settings.name='Non-Color'
    w,h=im.size; return np.array(im.pixels[:]).reshape(h,w,4)
rgb=pixels(SRC/'TP25_Normal_RGB.png'); ag=pixels(OUT/'TP25_Normal.png')
assert np.max(abs(rgb[...,0]-ag[...,3]))<1e-6 and np.max(abs(rgb[...,1]-ag[...,1]))<1e-6
assert np.min(ag[...,:1])>.999 and np.min(ag[...,2])>.999
normal=rgb[...,:3]*2-1; error=float(np.max(abs(np.linalg.norm(normal,axis=2)-1)))
assert error<.014,error
al=pixels(OUT/'TP25_AlbedoSpecular.png'); assert al[...,3].min()<.1 and al[...,3].max()<.5
report['normal_max_quantized_length_error']=error; report['normal_packing']='A=X, G=Y, R=B=255, +Y tangent convention'
report['specular_alpha_range']=[float(al[...,3].min()),float(al[...,3].max())]
# Review collections cannot be descendants of the exported part.
assert not any(o.name.startswith('REVIEW_') for o in root.children_recursive)
review=bpy.data.collections['REVIEW_ONLY_STOCK_BAY_AND_SIX_ED01']
assert len([o for o in review.objects if o.name.startswith('REVIEW_ED01_')])>6
report['review_not_exported']=True
(SRC/'reopen_validation.json').write_text(json.dumps(report,indent=2)+'\n',encoding='utf-8')
print(json.dumps(report,indent=2),flush=True)
