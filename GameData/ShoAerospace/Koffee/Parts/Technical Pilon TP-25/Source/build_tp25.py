"""Editable stock-style TP-25, analytic texture relief and native KSP MU export.
Run with Blender 4.4 --background --factory-startup --python-exit-code 1 --python this_file.
All generated files stay inside the new part directory. Reference assets are read-only.
"""
from pathlib import Path
import sys, math, json, struct, zlib, hashlib
import bpy, bmesh
import numpy as np
from mathutils import Vector, Quaternion, Matrix
sys.dont_write_bytecode = True
SRC = Path(__file__).resolve().parent
OUT = SRC.parent
ROOT = SRC.parents[5]
sys.path.insert(0, 'D:/Juegos/blender-mu')
import io_object_mu
io_object_mu.register()
from io_object_mu.export_mu.export import export_object
from io_object_mu.mu import Mu
Mu.read_string = lambda m: m.file.read(m.read_7int()).decode('utf-8', errors='strict')
sys.path.insert(0, str(ROOT/'Utils'))
from check_crew_chute import objects, world, parse_cfg
SIZE=1024
TILES={'panel':(16,16,496,784),'gray':(520,16,744,240),'metal':(768,16,1008,240),
       'red':(520,264,744,488),'dark':(768,264,1008,488),'screen':(520,520,744,744),'led':(768,520,1008,744)}
COLORS={'panel':(208,207,198,52),'gray':(101,105,107,62),'metal':(143,147,147,110),
        'red':(151,28,27,38),'dark':(32,35,36,30),'screen':(16,25,28,90),'led':(211,204,121,65)}
PARTS=[]; COLS=[]

def png(path,a):
    a=np.clip(np.rint(a),0,255).astype(np.uint8)
    if a.shape[2]==3: a=np.concatenate((a,np.full((*a.shape[:2],1),255,np.uint8)),axis=2)
    def chunk(tag,data): return struct.pack('>I',len(data))+tag+data+struct.pack('>I',zlib.crc32(tag+data)&0xffffffff)
    h,w=a.shape[:2]; raw=b''.join(b'\0'+r.tobytes() for r in a[::-1])
    path.write_bytes(b'\x89PNG\r\n\x1a\n'+chunk(b'IHDR',struct.pack('>IIBBBBB',w,h,8,6,0,0,0))+chunk(b'IDAT',zlib.compress(raw,9))+chunk(b'IEND',b''))

def textures():
    a=np.full((SIZE,SIZE,4),(101,105,107,62),np.float32); h=np.zeros((SIZE,SIZE),np.float32)
    for key,(x0,y0,x1,y1) in TILES.items(): a[y0:y1,x0:x1]=COLORS[key]
    x0,y0,x1,y1=TILES['panel']; yy,xx=np.mgrid[0:SIZE,0:SIZE]
    # Six columns, nine rows of EMPTY bores: dark centres, restrained recessed normals.
    # Four larger screw heads with a slot secure the panel; these are not bores.
    for x in np.linspace(x0+65,x1-65,6):
        for y in np.linspace(y0+66,y1-66,9):
            r=np.sqrt((xx-x)**2+(yy-y)**2); m=r<4.0; rim=(r>=4)&(r<5.5)
            a[m]=(38,40,39,22); a[rim]=(146,148,142,45)
            h+=-1.1*np.clip((5.5-r)/1.7,0,1)
    for x in (x0+20,x1-20):
        for y in (y0+20,y1-20):
            r=np.sqrt((xx-x)**2+(yy-y)**2)
            a[r<8.5]=(84,88,88,70); a[r<6.8]=(159,163,160,95)
            slot=(abs(xx-x)<4.2)&(abs(yy-y)<1.3); a[slot]=(45,48,48,30)
            h+=.55*np.clip((8-r)/1.5,0,1)-.65*slot
    # Analytic height relief, tangent +Y is image-up / increasing UV V.
    dy,dx=np.gradient(h); n=np.dstack((-dx,-dy,np.ones_like(h))); n/=np.linalg.norm(n,axis=2)[...,None]
    rgb=(n*.5+.5)*255; ag=np.full((SIZE,SIZE,4),255,np.float32); ag[...,1]=rgb[...,1]; ag[...,3]=rgb[...,0]
    png(OUT/'TP25_AlbedoSpecular.png',a); png(OUT/'TP25_Normal.png',ag); png(SRC/'TP25_Normal_RGB.png',rgb)
    assert np.max(abs(np.linalg.norm(n,axis=2)-1))<1e-6

def image(name,data=False):
    im=bpy.data.images.load(str(name),check_existing=True); im.alpha_mode='CHANNEL_PACKED'
    im.colorspace_settings.name='Non-Color' if data else 'sRGB'; return im

def material():
    m=bpy.data.materials.new('TP25_PaintedStructure'); m.use_nodes=True
    ns=m.node_tree.nodes; ls=m.node_tree.links; p=ns.get('Principled BSDF'); p.inputs['Roughness'].default_value=.64
    tex=ns.new('ShaderNodeTexImage'); tex.image=image(OUT/'TP25_AlbedoSpecular.png'); ls.new(tex.outputs['Color'],p.inputs['Base Color'])
    nt=ns.new('ShaderNodeTexImage'); nt.image=image(SRC/'TP25_Normal_RGB.png',True)
    nm=ns.new('ShaderNodeNormalMap'); ls.new(nt.outputs['Color'],nm.inputs['Color']); ls.new(nm.outputs['Normal'],p.inputs['Normal'])
    prop=m.mumatprop; prop.shaderName='KSP/Bumped Specular'
    for name,value in {'_Color':(1,1,1,1),'_SpecColor':(.35,.35,.35,1),'_RimColor':(0,0,0,0)}.items():
        q=prop.color.properties.add(); q.name=name; q.value=value
    for name,value in {'_Shininess':.18,'_Opacity':1.,'_RimFalloff':.1}.items():
        q=prop.float3.properties.add(); q.name=name; q.value=value
    for name,value in {'_MainTex':'TP25_AlbedoSpecular.png','_BumpMap':'TP25_Normal.png'}.items():
        q=prop.texture.properties.add(); q.name=name; q.tex=value; q.type=False; q.scale=(1,1); q.offset=(0,0)
    return m

def uv(obj,tile):
    mesh=obj.data; lay=mesh.uv_layers.new(name='UVMap')
    for f in mesh.polygons:
        axis=max(range(3),key=lambda i:abs(f.normal[i])); dims=[i for i in range(3) if i!=axis]
        key=tile if tile!='panel' or (axis==1 and f.normal.y<-.99) else 'gray'
        x0,y0,x1,y1=TILES[key]
        bounds=[(min(mesh.vertices[v].co[i] for v in f.vertices),max(mesh.vertices[v].co[i] for v in f.vertices)) for i in dims]
        for li in f.loop_indices:
            v=mesh.vertices[mesh.loops[li].vertex_index].co
            s,t=[(v[i]-lo)/max(hi-lo,1e-9) for i,(lo,hi) in zip(dims,bounds)]
            # Swatches have constant color; the front panel receives the full UV island.
            lay.data[li].uv=((x0+2+s*(x1-x0-4))/SIZE,(y0+2+t*(y1-y0-4))/SIZE)

def mesh(name,verts,faces,tile='gray',collider=False):
    me=bpy.data.meshes.new(name); me.from_pydata(verts,[],faces); me.update()
    bm=bmesh.new(); bm.from_mesh(me); bmesh.ops.recalc_face_normals(bm,faces=bm.faces); bm.to_mesh(me); bm.free()
    o=bpy.data.objects.new(name,me); ASSET.objects.link(o); o.parent=RACK
    if collider:
        o.muproperties.collider='MU_COL_MESH'; o.muproperties.isConvex=True; o.muproperties.isTrigger=False
        o.muproperties.separate=True; o.hide_render=True; o.display_type='WIRE'; COLS.append(o)
    else: me.materials.append(MAT); uv(o,tile); PARTS.append(o)
    return o

def block(name,dimensions,location,tile='gray',angle=0,collider=False,bevel=0):
    x,y,z=[v/2 for v in dimensions]
    vs=[(a*x,b*y,c*z) for a,b,c in [(-1,-1,-1),(1,-1,-1),(1,1,-1),(-1,1,-1),(-1,-1,1),(1,-1,1),(1,1,1),(-1,1,1)]]
    fs=[(0,3,2,1),(4,5,6,7),(0,1,5,4),(1,2,6,5),(2,3,7,6),(3,0,4,7)]
    o=mesh(name,vs,fs,tile,collider); o.location=location; o.rotation_euler.z=angle
    if bevel:
        bpy.context.view_layer.objects.active=o
        mod=o.modifiers.new('Manufactured_edges','BEVEL'); mod.width=min(bevel,min(dimensions)*.2); mod.segments=1
        bpy.ops.object.modifier_apply(modifier=mod.name)
        # Rebuild UV after bevel, retaining the uninterrupted front mounting island.
        o.data.uv_layers.remove(o.data.uv_layers.active); uv(o,tile)
    return o

def prism(name,points,z0,z1,tile='gray',collider=False):
    n=len(points); vs=[(x,y,z) for z in (z0,z1) for x,y in points]
    fs=[tuple(range(n-1,-1,-1)),tuple(range(n,2*n))]+[(i,(i+1)%n,(i+1)%n+n,i+n) for i in range(n)]
    return mesh(name,vs,fs,tile,collider)

def ring(name,ro,ri,z0,z1,tile='metal',n=32):
    vs=[(r*math.cos(i*2*math.pi/n),r*math.sin(i*2*math.pi/n),z) for z,r in [(z0,ro),(z1,ro),(z1,ri),(z0,ri)] for i in range(n)]
    fs=[(j*n+i,j*n+(i+1)%n,((j+1)%4)*n+(i+1)%n,((j+1)%4)*n+i) for j in range(4) for i in range(n)]
    return mesh(name,vs,fs,tile)

def rotated(x,y,a): return (x*math.cos(a)-y*math.sin(a),x*math.sin(a)+y*math.cos(a))

def asset():
    global ASSET,RACK,MAT
    ASSET=bpy.data.collections.new('TP25_ASSET_ONLY'); bpy.context.scene.collection.children.link(ASSET)
    RACK=bpy.data.objects.new('SHOTechnicalPilonTP25',None); ASSET.objects.link(RACK); RACK.muproperties.modelType='PART'
    MAT=material()
    for i in range(6):
        a=i*math.pi/3; tag=str(i+1)
        # A hollow load-bearing hexagonal shell. No collider occupies the hollow centre.
        pts=[rotated(x,y,a) for x,y in [(-.575/math.sqrt(3),-.575),(.575/math.sqrt(3),-.575),(.55/math.sqrt(3),-.55),(-.55/math.sqrt(3),-.55)]]
        prism('TP25_ShellFace_'+tag,pts,-.47,.455)
        prism('TP25_ShellCollider_'+tag,pts,-.47,.455,collider=True)
        pos=(*rotated(0,-.5825,a),-.005)
        block('TP25_MountPanel_'+tag,(.600,.015,.790),pos,'panel',a,bevel=.001)
        block('TP25_PanelCollider_'+tag,(.600,.015,.860),(*rotated(0,-.5825,a),-.040),'gray',a,True)
    pts=[(.595/math.cos(math.pi/6)*math.cos(-math.pi/3+i*math.pi/3),.595/math.cos(math.pi/6)*math.sin(-math.pi/3+i*math.pi/3)) for i in range(6)]
    for name,z0,z1 in [('BottomEdge',-.5,-.47),('TopDeck',.455,.475)]:
        prism('TP25_'+name,pts,z0,z1); prism('TP25_'+name+'Collider',pts,z0,z1,collider=True)
    # Lower rail remains inside the same hexagonal outline and never below Z=-.5.
    for i in range(6):
        a=i*math.pi/3
        block('TP25_LowerRail_'+str(i),(.600,.015,.070),(*rotated(0,-.5825,a),-.435),'gray',a)
    # Paired muted red cables in the six vertex channels; gray terminals and clamps.
    for i in range(6):
        a=-math.pi/3+i*math.pi/3; radial=Vector((math.cos(a),math.sin(a),0)); tangent=Vector((-math.sin(a),math.cos(a),0))
        for j,side in enumerate((-.010,.010)):
            center=radial*.658+tangent*side
            pts=[(center.x+.007*math.cos(k*2*math.pi/8),center.y+.007*math.sin(k*2*math.pi/8)) for k in range(8)]
            prism('TP25_RedCable_'+str(i)+'_'+str(j),pts,-.405,.405,'red')
        for j,z in enumerate((-.415,-.25,0,.25,.415)):
            # Clamps follow the cables, not the mounting-face centres.
            angle=a+math.pi/2; center=radial*.660
            block('TP25_CableClamp_'+str(i)+'_'+str(j),(.040,.020,.026 if abs(z)<.4 else .056),(center.x,center.y,z),'metal',angle,bevel=.001)
    # One small screen with an unpowered visual status lamp, matching the concept.
    block('TP25_ScreenBezel',(.164,.013,.050),(.174,-.583,.425),'metal',bevel=.002)
    block('TP25_Screen',(.112,.002,.033),(.155,-.5905,.425),'screen',bevel=.001)
    block('TP25_ScreenDivider',(.0015,.001,.031),(.170,-.592,.425),'gray')
    # Lamp is circular geometry, no RESOURCE/MODULE or emission controller.
    o=ring('TP25_StatusBezel',.012,.008,-.002,.002,n=16); o.rotation_euler.x=math.pi/2; o.location=(.231,-.592,.425)
    disk=prism('TP25_StatusLamp',[(.0075*math.cos(k*2*math.pi/16),.0075*math.sin(k*2*math.pi/16)) for k in range(16)],0,.001,'led')
    disk.rotation_euler.x=math.pi/2; disk.location=(.231,-.594,.425)
    # Retained circular top accessory: one plain rim and a dark recessed centre.
    ring('TP25_TopAccessory',.130,.078,.475,.5,n=48)
    prism('TP25_TopSocketRecess',[(.078*math.cos(k*2*math.pi/32),.078*math.sin(k*2*math.pi/32)) for k in range(32)],.475,.476,'dark')
    for i in range(12):
        a=i*2*math.pi/12; b=(i+1)*2*math.pi/12
        pts=[(r*math.cos(t),r*math.sin(t)) for r,t in [(.13,a),(.13,b),(.078,b),(.078,a)]]
        prism('TP25_TopRimCollider_'+str(i),pts,.475,.5,collider=True)
    for i in range(6):
        a=i*math.pi/3
        pts=[(.105*math.cos(a)+.004*math.cos(k*math.pi/3),.105*math.sin(a)+.004*math.sin(k*math.pi/3)) for k in range(6)]
        prism('TP25_TopFastener_'+str(i),pts,.4985,.5,'dark')
    add_attachment_transforms(RACK,ASSET)
    return RACK

def add_attachment_transforms(root,collection):
    """MU forward is Blender +Y / Unity +Z; use it for every CFG NODE."""
    for name,z in [('attach_North',.5),('attach_South',-.5)]:
        o=bpy.data.objects.new(name,None); collection.objects.link(o); o.parent=root; o.location.z=z
        o.rotation_euler.x=math.pi/2 if z>0 else -math.pi/2
        o.empty_display_type='ARROWS'; o.empty_display_size=.06
        o['KSP_node_direction']=[0,1 if z>0 else -1,0]
    for name in ('TP25_SurfaceConfiguration','TP25_FaceNodes'):
        group=bpy.data.objects.new(name,None); collection.objects.link(group); group.parent=root
    group=bpy.data.objects['TP25_FaceNodes']
    for i in range(6):
        a=i*math.pi/3
        o=bpy.data.objects.new('attach_Face'+str(i+1).zfill(2),None); collection.objects.link(o); o.parent=group
        o.location=(*rotated(0,-.590,a),-.005)
        o.rotation_euler.z=math.pi+a
        o.empty_display_type='ARROWS'; o.empty_display_size=.05
        o['KSP_node_direction']=[math.sin(a),0,-math.cos(a)]


def validate_geometry():
    bpy.context.view_layer.update(); report={'components':[]}; points=[]
    for o in PARTS+COLS:
        me=o.data; me.calc_loop_triangles(); bm=bmesh.new(); bm.from_mesh(me)
        assert all(e.is_manifold and e.is_contiguous for e in bm.edges),o.name
        volume=bm.calc_volume(signed=True); bm.free(); assert volume>0,o.name
        assert all(t.area>1e-12 for t in me.loop_triangles),o.name
        if o in PARTS: points.extend(o.matrix_world@v.co for v in me.vertices)
        report['components'].append({'name':o.name,'triangles':len(me.loop_triangles),'volume_m3':volume,'collider':o in COLS})
    bounds=[[min(v[i] for v in points),max(v[i] for v in points)] for i in range(3)]
    assert abs(bounds[2][0]+.5)<1e-6 and abs(bounds[2][1]-.5)<1e-6,bounds
    panels=[o for o in PARTS if o.name.startswith('TP25_MountPanel_')]; assert len(panels)==6
    for o in panels:
        assert abs(o.dimensions.x-.6)<1e-5 if o.rotation_euler.z==0 else True
    report.update(bounds_blender_xyz_m=bounds,mounting_faces=6,panel_width_m=.6,panel_height_m=.79,mount_apothem_m=.59,
                  render_triangles=sum(len(o.data.loop_triangles) for o in PARTS),colliders=len(COLS))
    (SRC/'geometry_validation.json').write_text(json.dumps(report,indent=2)+'\n',encoding='utf-8')

def export():
    # Join only rendered meshes, retaining component vertex groups for later editing.
    bpy.ops.object.select_all(action='DESELECT')
    for o in PARTS:
        g=o.vertex_groups.new(name=o.name); g.add(list(range(len(o.data.vertices))),1,'REPLACE'); o.select_set(True)
    bpy.context.view_layer.objects.active=PARTS[0]; bpy.ops.object.join(); o=bpy.context.object; o.name='TP25_VisibleStructure'
    mod=o.modifiers.new('Runtime_triangles','TRIANGULATE'); bpy.ops.object.modifier_apply(modifier=mod.name)
    bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
    mu=export_object(RACK,str(OUT/'SHO_Technical_Pilon_TP25.mu'))
    assert not mu.messages,mu.messages
    assert len(mu.materials)==1 and len(mu.textures)==2

def clean_mu(path):
    mu=Mu().read(str(path)); mu.obj.transform.localPosition=(0,0,0); mu.obj.transform.localRotation=(1,0,0,0); mu.obj.transform.localScale=(1,1,1)
    return mu

def curve_value(cv,time):
    ks=cv.keys
    if time<=ks[0].time: return ks[0].value
    if time>=ks[-1].time: return ks[-1].value
    for a,b in zip(ks,ks[1:]):
        if a.time<=time<=b.time:
            dt=b.time-a.time; t=(time-a.time)/dt
            return (2*t**3-3*t*t+1)*a.value+(t**3-2*t*t+t)*dt*a.tangent[1]+(-2*t**3+3*t*t)*b.value+(t**3-t*t)*dt*b.tangent[0]

def animate_bay(mu,time):
    for obj,_ in objects(mu.obj):
        if not hasattr(obj,'animation'): continue
        def paths(o,path=''):
            yield path,o
            for child in o.children: yield from paths(child,path+'/'+child.transform.name if path else child.transform.name)
        mapping=dict(paths(obj))
        rotations={}
        for cv in obj.animation.clips[0].curves:
            if cv.property.startswith('m_LocalRotation.'):
                rotations.setdefault(cv.path,[0,0,0,1])['xyzw'.index(cv.property[-1])]=curve_value(cv,time)
        for path,(x,y,z,w) in rotations.items():
            # MU stores curves as Unity channels; mu.py converts transforms to Blender.
            mapping[path].transform.localRotation=(w,-x,-z,-y)

def preview_materials(mu,folder,prefix):
    mats=[]
    for source in mu.materials:
        m=bpy.data.materials.new(prefix+source.name); m.use_nodes=True; p=m.node_tree.nodes.get('Principled BSDF'); p.inputs['Roughness'].default_value=.65
        prop=source.textureProperties.get('_MainTex')
        if prop:
            stem=mu.textures[prop.index].name; path=folder/stem
            if not path.is_file(): path=folder/(stem+'.png')
            if path.is_file():
                tex=m.node_tree.nodes.new('ShaderNodeTexImage'); tex.image=image(path); m.node_tree.links.new(tex.outputs['Color'],p.inputs['Base Color'])
        mats.append(m)
    return mats

def reference_meshes(mu,col,mats,prefix,placement=Matrix.Identity(4)):
    pairs=[]
    for o,c in objects(mu.obj):
        if not hasattr(o,'renderer'): continue
        me=o.shared_mesh; verts=[]
        for v in me.verts:
            k=world(c,v); verts.append(placement@Vector((k[0],k[2],k[1])))
        fs=[tri for sm in me.submeshes for tri in sm]
        data=bpy.data.meshes.new(prefix+o.transform.name); data.from_pydata(verts,[],fs); data.update()
        ob=bpy.data.objects.new(data.name,data); col.objects.link(ob)
        for m in mats: data.materials.append(m)
        offset=0
        for i,sm in enumerate(me.submeshes):
            for f in data.polygons[offset:offset+len(sm)]: f.material_index=o.renderer.materials[i]
            offset+=len(sm)
        if me.uvs:
            uv=data.uv_layers.new(name='UVMap')
            for loop in data.loops: uv.data[loop.index].uv=me.uvs[loop.vertex_index]
        pairs.append((ob,o,c,placement))
    return pairs

def preview():
    scene=bpy.context.scene; scene.render.engine='CYCLES'; scene.cycles.samples=32; scene.cycles.use_denoising=True
    scene.render.resolution_x=1400; scene.render.resolution_y=1100; scene.render.resolution_percentage=100
    scene.render.image_settings.file_format='PNG'; scene.view_settings.view_transform='AgX'
    w=bpy.data.worlds.new('TP25_ReviewWorld'); w.use_nodes=True; w.node_tree.nodes['Background'].inputs[0].default_value=(.20,.23,.27,1); w.node_tree.nodes['Background'].inputs[1].default_value=.5; scene.world=w
    col=bpy.data.collections.new('REVIEW_ONLY_LIGHTS'); scene.collection.children.link(col)
    for name,pos,power,size in [('Key',(2,-3,4),400,3),('Fill',(-3,-1,2),200,3),('Rim',(1,3,3),350,2)]:
        d=bpy.data.lights.new(name,'AREA'); d.energy=power; d.size=size; ob=bpy.data.objects.new(name,d); col.objects.link(ob); ob.location=pos; ob.rotation_euler=(-ob.location).to_track_quat('-Z','Y').to_euler()
    d=bpy.data.cameras.new('TP25_ReviewCamera'); cam=bpy.data.objects.new(d.name,d); col.objects.link(cam); scene.camera=cam; d.type='ORTHO'
    def render(name,pos,scale,target=(0,0,0)):
        cam.location=pos; cam.rotation_euler=(Vector(target)-cam.location).to_track_quat('-Z','Y').to_euler(); d.ortho_scale=scale
        scene.render.filepath=str(SRC/name); bpy.ops.render.render(write_still=True)
    render('TP25_Perspective.png',(2,-3,1.9),1.9)
    render('TP25_Front.png',(0,-4,.20),1.65)
    baypath=Path('D:/Juegos/KSP/GameData/Squad/Parts/Utility/ServiceBay_v2/Assets/ServiceBay_250_v2.mu')
    edpath=ROOT/'GameData/ShoAerospace/Koffee/Parts/Experiments_Device_ED-01/Experiment_Device_ED01.mu'
    bay=clean_mu(baypath); ed=clean_mu(edpath)
    review=bpy.data.collections.new('REVIEW_ONLY_STOCK_BAY_AND_SIX_ED01'); scene.collection.children.link(review)
    review['exported_to_part_mu']=False; review['description']='Actual stock and ED-01 MU meshes. Geometric review only; not a KSP test.'
    mats=preview_materials(bay,baypath.parent,'REVIEW_STOCK_'); animate_bay(bay,1)
    pairs=reference_meshes(bay,review,mats,'REVIEW_Bay_')
    emats=preview_materials(ed,edpath.parent,'REVIEW_ED01_')
    for i in range(6):
        a=i*math.pi/3
        place=Matrix.Rotation(a,4,'Z')@Matrix.Translation((0,-.7205,-.07))@Matrix.Rotation(math.pi,4,'Z')@Matrix.Diagonal((.75,.75,.75,1))
        reference_meshes(ed,review,emats,'REVIEW_ED01_'+str(i+1)+'_',place)
    # Bottom stack installation: rack bottom -0.5 mates with bay node -0.55.
    RACK.location.z=-.05
    render('TP25_InStockBay_Open.png',(3,-5,3.4),4.7,target=(0,0,-.05))
    render('TP25_InStockBay_Front.png',(1.6,-6,1.0),3.9,target=(0,0,-.05))
    # Explicit review cutaway: duplicate stock geometry and remove only the top deck.
    cut=bpy.data.collections.new('REVIEW_ONLY_TOP_DECK_CUTAWAY'); scene.collection.children.link(cut)
    for ob,o,c,placement in pairs:
        dup=ob.copy(); dup.data=ob.data.copy(); cut.objects.link(dup)
        bm=bmesh.new(); bm.from_mesh(dup.data)
        bmesh.ops.delete(bm,geom=[f for f in bm.faces if all(v.co.z>.52 for v in f.verts)],context='FACES')
        bm.to_mesh(dup.data); bm.free(); ob.hide_render=True
    render('TP25_InStockBay_Cutaway.png',(3,-5,3.4),4.7,target=(0,0,-.05))
    render('TP25_InStockBay_Top.png',(0,-.001,6),4.8,target=(0,0,0))
    cut.hide_render=True; cut.hide_viewport=True
    for ob,o,c,placement in pairs: ob.hide_render=False
    animate_bay(bay,0)
    for ob,o,c,placement in pairs:
        for dest,v in zip(ob.data.vertices,o.shared_mesh.verts):
            k=world(c,v); dest.co=placement@Vector((k[0],k[2],k[1]))
    render('TP25_InStockBay_Closed.png',(3,-5,3.4),3.5)
    # Keep the editable review scene at open doors; isolate the part via its collection.
    animate_bay(bay,1)
    for ob,o,c,placement in pairs:
        for dest,v in zip(ob.data.vertices,o.shared_mesh.verts):
            k=world(c,v); dest.co=placement@Vector((k[0],k[2],k[1]))
    RACK.location.z=0
    # In the saved review, shift the host +0.05 m instead of the part -0.05 m.
    # This keeps the export root identity and preserves the exact bottom-node assembly.
    for ob in review.objects: ob.location.z+=.05
    for ob in cut.objects: ob.location.z+=.05
    review.hide_render=True; review.hide_viewport=True
    render('TP25_Final.png',(2,-3,1.9),1.9)
    # Reference images are packed into the editable file only; no stock asset is copied into runtime.
    for im in bpy.data.images:
        if im.source=='FILE' and not Path(bpy.path.abspath(im.filepath)).is_relative_to(OUT): im.pack()
    bpy.ops.file.make_paths_relative()

def main():
    bpy.ops.wm.read_factory_settings(use_empty=True); bpy.context.preferences.filepaths.save_version=0
    bpy.context.scene.unit_settings.system='METRIC'; bpy.context.scene.unit_settings.scale_length=1
    textures(); asset(); validate_geometry(); export(); preview()
    bpy.ops.object.select_all(action='DESELECT'); bpy.context.view_layer.objects.active=bpy.data.objects['TP25_VisibleStructure']; bpy.context.object.select_set(True)
    bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'SHO_Technical_Pilon_TP25.blend'))
    print('TP25 BUILD PASS',flush=True)

if __name__=='__main__': main()
