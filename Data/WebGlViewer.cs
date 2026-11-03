namespace Kh1Import;

internal static class WebGlViewer
{
    internal const string Html = """
<!doctype html>
<html lang="es">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1">
<title>KH1 ARD Viewer - __ARD_TITLE__</title>
<style>
:root{color-scheme:dark;font:14px "Segoe UI",sans-serif;background:#101319;color:#e5e9f0}
*{box-sizing:border-box}body{margin:0;height:100vh;display:grid;grid-template-columns:300px 1fr;overflow:hidden}
aside{padding:18px;background:#181d25;border-right:1px solid #303846;display:flex;flex-direction:column;min-height:0;overflow:hidden}
h1{font-size:18px;margin:0 0 6px}p{color:#aab4c3;line-height:1.45;margin:6px 0 16px}
button{font:inherit;background:#252d39;border:1px solid #3c4656;color:#edf2f8;border-radius:5px;cursor:pointer}
button:hover{border-color:#7ba7e1}
button:disabled{opacity:.55;cursor:wait}
select{font:inherit;background:#181d25;border:1px solid #4b5668;color:#edf2f8;border-radius:5px}
.file-actions{display:grid;grid-template-columns:1fr 1fr;gap:7px;margin:14px 0;flex:0 0 auto}
.file-actions button{padding:9px 6px;font-size:12px}
.section-title{font-size:12px;color:#aab4c3;margin:10px 0 7px;flex:0 0 auto}
#archives{border-top:1px solid #303846;flex:1 1 auto;min-height:60px;overflow-y:auto;overflow-x:hidden;scrollbar-gutter:stable}
.archive-row{display:flex;align-items:center;min-height:31px;border-bottom:1px solid #242b35}
.archive-toggle{width:27px;height:28px;border:0;background:transparent;color:#aab4c3;font-size:13px}
.archive-name,.model-name{flex:1;min-width:0;text-align:left;border:0;background:transparent;padding:6px 5px;overflow:hidden;text-overflow:ellipsis;white-space:nowrap}
.archive-name{font-weight:600}.model-list{padding-left:17px}
.model-name{display:block;width:100%;font-size:12px;color:#bcc6d4}.model-name.active{color:#fff;background:#354357;border-radius:3px}
.model-empty{font-size:12px;color:#7e8998;padding:6px 8px}
#texture-gallery{height:150px;flex:0 0 150px;margin-top:8px;border-top:1px solid #303846;display:flex;flex-direction:column;min-height:0}
#texture-grid{display:grid;grid-template-columns:repeat(auto-fill,48px);align-content:start;gap:8px;padding:8px 2px;overflow:auto}
.texture-thumb{width:48px;height:48px;padding:2px;background:#11161e;border:1px solid #4b5668;border-radius:6px;overflow:hidden}
.texture-thumb:hover{border-color:#9bbff2}
.texture-thumb img{display:block;width:100%;height:100%;object-fit:contain;image-rendering:pixelated}
.texture-empty{padding:10px 4px;color:#7e8998;font-size:12px}
#stats{position:absolute;top:16px;right:16px;padding:10px 12px;background:#111d;border:1px solid #303846;border-radius:5px;color:#c5cfdd;line-height:1.65;font-size:12px;white-space:pre-line;pointer-events:none}
main{position:relative;min-width:0;min-height:0}canvas{width:100%;height:100%;display:block;touch-action:none}
#hint{position:absolute;bottom:15px;left:16px;background:#111b;color:#c5cfdd;padding:8px 11px;border-radius:5px;pointer-events:none}
#export,#reset,#mset-pose,#mset-play,#bones{position:absolute;z-index:2;display:grid;place-items:center;background:#181d25dd;border:1px solid #4b5668;box-shadow:0 4px 16px #0006}
#export{top:16px;left:16px;padding:9px 14px}
#export:hover,#reset:hover,#mset-pose:hover,#mset-play:hover,#bones:hover{border-color:#9bbff2;background:#252d39}
#mset-pose{bottom:15px;right:192px;padding:9px 14px}
#mset-play{bottom:15px;right:360px;width:88px;padding:9px 14px}
#bones{bottom:15px;right:456px;padding:9px 14px}
#reset{right:16px;bottom:15px;padding:9px 14px}
#texture-modal{position:fixed;z-index:5;inset:0;display:grid;place-items:center;background:#05070bcc;padding:24px}
#texture-modal[hidden]{display:none}
#texture-preview{width:min(68vmin,620px);height:min(68vmin,620px);max-width:calc(100vw - 32px);max-height:calc(100vh - 32px);display:flex;flex-direction:column;align-items:center;gap:10px;padding:12px;background:#181d25;border:1px solid #485466;border-radius:8px;box-shadow:0 16px 48px #000a}
#texture-preview img{display:block;flex:1;min-height:0;width:100%;height:auto;max-width:100%;max-height:calc(100% - 24px);object-fit:contain;image-rendering:pixelated}
#texture-caption{color:#c5cfdd;font-size:12px}
#error{color:#ffb4a9;margin-top:12px;white-space:pre-wrap}
</style>
</head>
<body>
<aside>
<h1>KH1 ARD Viewer</h1>
<p>__ARD_TITLE__ · GPU-accelerated WebGL. Drag to orbit; use the wheel to zoom.</p>
<div class="file-actions">
<button id="open-ard">Open .ard</button>
<button id="open-folder">Open folder...</button>
</div>
<input id="ard-file" type="file" accept=".ard" hidden>
<input id="ard-folder" type="file" accept=".ard" webkitdirectory multiple hidden>
<div class="section-title">Loaded ARDs</div>
<div id="archives"></div>
<section id="texture-gallery">
<div class="section-title">Textures</div>
<div id="texture-grid"></div>
</section>
<div id="error"></div>
</aside>
<main><canvas id="view"></canvas><button id="export" title="Export the selected model to glTF" hidden disabled>Export</button><button id="bones" title="Show or hide the skeleton bones" hidden>Show bones</button><button id="mset-pose" title="Apply a decoded MSET pose" hidden>Apply MSET pose</button><button id="mset-play" title="Play the decoded MSET keyframe animation" hidden>Play</button><button id="reset" title="Reset camera">Reset camera</button><div id="stats"></div><div id="hint">Drag: orbit · Wheel: zoom · Shift or middle-drag: pan</div><div id="texture-modal" hidden><div id="texture-preview"><img id="texture-large" alt=""><div id="texture-caption"></div></div></div></main>
<script>
const models=__ARD_DATA__;
const canvas=document.getElementById('view'),gl=canvas.getContext('webgl2',{antialias:true,alpha:false});
const status=document.getElementById('error');
if(!gl){status.textContent='Could not create a WebGL 2 context. Check that hardware acceleration is enabled.';throw new Error('WebGL 2 unavailable');}
const vertexSource=`#version 300 es
in vec3 aPosition; in vec4 aColor; in vec2 aUv;
uniform mat4 uMvp; out vec4 vColor; out vec2 vUv;
void main(){gl_Position=uMvp*vec4(aPosition,1.0);vColor=aColor;vUv=aUv;}`;
const fragmentSource=`#version 300 es
precision mediump float;
in vec4 vColor; in vec2 vUv; uniform sampler2D uTexture; uniform int uHasTexture;
out vec4 outColor;
void main(){outColor=vColor*(uHasTexture==1?texture(uTexture,vUv):vec4(1.0));}`;
function shader(type,source){const s=gl.createShader(type);gl.shaderSource(s,source);gl.compileShader(s);if(!gl.getShaderParameter(s,gl.COMPILE_STATUS))throw new Error(gl.getShaderInfoLog(s));return s;}
const program=gl.createProgram();gl.attachShader(program,shader(gl.VERTEX_SHADER,vertexSource));gl.attachShader(program,shader(gl.FRAGMENT_SHADER,fragmentSource));gl.linkProgram(program);
if(!gl.getProgramParameter(program,gl.LINK_STATUS))throw new Error(gl.getProgramInfoLog(program));
gl.useProgram(program);
const loc={position:gl.getAttribLocation(program,'aPosition'),color:gl.getAttribLocation(program,'aColor'),uv:gl.getAttribLocation(program,'aUv'),mvp:gl.getUniformLocation(program,'uMvp'),tex:gl.getUniformLocation(program,'uTexture'),has:gl.getUniformLocation(program,'uHasTexture')};
gl.enable(gl.DEPTH_TEST);gl.enable(gl.BLEND);gl.blendFunc(gl.SRC_ALPHA,gl.ONE_MINUS_SRC_ALPHA);gl.enable(gl.CULL_FACE);gl.cullFace(gl.BACK);gl.frontFace(gl.CCW);gl.clearColor(.055,.067,.086,1);
const stats=document.getElementById('stats'),archiveList=document.getElementById('archives'),textureGrid=document.getElementById('texture-grid'),textureModal=document.getElementById('texture-modal'),textureLarge=document.getElementById('texture-large'),textureCaption=document.getElementById('texture-caption');
const archives=new Map(),expandedArchives=new Set();
const initialArchive='__ARD_TITLE__';
if(models.length)archives.set(initialArchive,models);
let gpu=[],current=-1,currentMsetSection=-1,currentMsetFrame=0,center=[0,0,0],radius=1,theta=.65,phi=1.05,distance=4,pan=[0,0,0],drag=null,grid=null,msetPlaybackTimer=null,msetPlaybackSection=-1,msetPlaybackStart=null,showBones=false,bonePositionBuffer=null,boneColorBuffer=null,boneUvBuffer=null;
function stopMsetPlayback(){if(msetPlaybackTimer!==null){cancelAnimationFrame(msetPlaybackTimer);msetPlaybackTimer=null;}msetPlaybackSection=-1;msetPlaybackStart=null;document.getElementById('mset-play').textContent='Play';}
function buffer(target,data){const b=gl.createBuffer();gl.bindBuffer(target,b);gl.bufferData(target,data,gl.STATIC_DRAW);return b;}
function release(){for(const g of gpu){gl.deleteBuffer(g.p);gl.deleteBuffer(g.c);gl.deleteBuffer(g.uv);gl.deleteBuffer(g.i);if(g.texture)gl.deleteTexture(g.texture);}gpu=[];}
const texturePreviewCache=new WeakMap();
function texturePreviewUrl(texture){
 let url=texturePreviewCache.get(texture);if(url)return url;
 const pixels=Uint8Array.from(atob(texture.rgba),c=>c.charCodeAt(0)),preview=document.createElement('canvas');
 preview.width=texture.width;preview.height=texture.height;
 const context=preview.getContext('2d');if(!context)throw new Error('Could not create a 2D context to preview textures.');
 context.putImageData(new ImageData(new Uint8ClampedArray(pixels),texture.width,texture.height),0,0);
 url=preview.toDataURL('image/png');texturePreviewCache.set(texture,url);return url;
}
function renderTextures(model){
 textureGrid.replaceChildren();
 if(!model.textures.length){const empty=document.createElement('div');empty.className='texture-empty';empty.textContent='This model has no textures';textureGrid.append(empty);return;}
 for(let i=0;i<model.textures.length;i++){
  const texture=model.textures[i],button=document.createElement('button'),image=document.createElement('img');
  const textureName=texture.name||model.name+'_texture_'+String(i+1).padStart(2,'0');
  button.className='texture-thumb';button.title='Preview '+textureName;button.setAttribute('aria-label',button.title);
  image.src=texturePreviewUrl(texture);image.alt=textureName;button.append(image);
  button.addEventListener('click',()=>{textureLarge.src=image.src;textureLarge.alt=textureName;textureCaption.textContent=texture.width+' × '+texture.height+' · '+textureName;textureModal.hidden=false;});
  textureGrid.append(button);
 }
}
function rebuildGrid(floorY){
 if(grid){gl.deleteBuffer(grid.p);gl.deleteBuffer(grid.c);gl.deleteBuffer(grid.uv);}
 const halfTiles=16,step=radius*.5,extent=step*halfTiles,positions=[],colors=[];
 for(let i=-halfTiles;i<=halfTiles;i++){
  const offset=i*step,axis=i===0;
  const cX=axis?[235,82,82,230]:[91,108,129,105],cZ=axis?[86,151,255,230]:[91,108,129,105];
  positions.push(center[0]-extent,floorY,center[2]+offset,center[0]+extent,floorY,center[2]+offset);
  positions.push(center[0]+offset,floorY,center[2]-extent,center[0]+offset,floorY,center[2]+extent);
  for(let n=0;n<2;n++)colors.push(...cX);
  for(let n=0;n<2;n++)colors.push(...cZ);
 }
 grid={p:buffer(gl.ARRAY_BUFFER,new Float32Array(positions)),c:buffer(gl.ARRAY_BUFFER,new Uint8Array(colors)),uv:buffer(gl.ARRAY_BUFFER,new Float32Array(positions.length/3*2)),count:positions.length/3};
}
function loadModel(index){
 stopMsetPlayback();release();current=index;const model=models[index];if(!model)return;
 let lo=[Infinity,Infinity,Infinity],hi=[-Infinity,-Infinity,-Infinity],vertices=0,triangles=0,textures=[];
 for(const t of model.textures){const raw=Uint8Array.from(atob(t.rgba),c=>c.charCodeAt(0)),tex=gl.createTexture();gl.bindTexture(gl.TEXTURE_2D,tex);gl.pixelStorei(gl.UNPACK_FLIP_Y_WEBGL,false);gl.texImage2D(gl.TEXTURE_2D,0,gl.RGBA,t.width,t.height,0,gl.RGBA,gl.UNSIGNED_BYTE,raw);gl.texParameteri(gl.TEXTURE_2D,gl.TEXTURE_MIN_FILTER,gl.LINEAR_MIPMAP_LINEAR);gl.texParameteri(gl.TEXTURE_2D,gl.TEXTURE_MAG_FILTER,gl.LINEAR);gl.texParameteri(gl.TEXTURE_2D,gl.TEXTURE_WRAP_S,t.repeat?gl.REPEAT:gl.CLAMP_TO_EDGE);gl.texParameteri(gl.TEXTURE_2D,gl.TEXTURE_WRAP_T,t.repeat?gl.REPEAT:gl.CLAMP_TO_EDGE);gl.generateMipmap(gl.TEXTURE_2D);textures.push(tex);}
 for(const m of model.meshes){
  const pos=new Float32Array(m.positions),uv=new Float32Array(m.uvs),col=Uint8Array.from(atob(m.colors),c=>c.charCodeAt(0)),ind=new Uint32Array(m.indices);
  if(model.format==='MDLS')for(let i=1;i<uv.length;i+=2)uv[i]=1-uv[i];
  for(let i=0;i<pos.length;i+=3){for(let a=0;a<3;a++){lo[a]=Math.min(lo[a],pos[i+a]);hi[a]=Math.max(hi[a],pos[i+a]);}}
  vertices+=pos.length/3;triangles+=ind.length/3;
  gpu.push({p:buffer(gl.ARRAY_BUFFER,pos),positions:pos,basePositions:new Float32Array(pos),jointIds:m.jointIds||[],c:buffer(gl.ARRAY_BUFFER,col),uv:buffer(gl.ARRAY_BUFFER,uv),i:buffer(gl.ELEMENT_ARRAY_BUFFER,ind),count:ind.length,texture:m.texture>=0?textures[m.texture]:null});
 }
 if(!Number.isFinite(lo[0]))lo=[-1,-1,-1],hi=[1,1,1];
 center=lo.map((v,i)=>(v+hi[i])/2);radius=Math.max(.01,Math.hypot(hi[0]-lo[0],hi[1]-lo[1],hi[2]-lo[2])/2);distance=radius*2.8;pan=[0,0,0];
 rebuildGrid(lo[1]-radius*.015);
 currentMsetSection=-1;currentMsetFrame=0;
 const usableMsets=(model.msetSections||[]).filter(section=>(section.channels.length||section.curves.length)&&section.jointLocalMatrices?.length===model.joints.length);
 const playableMsets=usableMsets.filter(section=>section.curves.length&&section.frameEnd>0&&section.framesPerSecond>0);
 const msetChannels=(model.msetSections||[]).reduce((total,section)=>total+section.channels.length,0);
 const msetCurves=(model.msetSections||[]).reduce((total,section)=>total+section.curves.length,0);
 stats.textContent='Meshes: '+model.meshes.length+'\nVertices: '+vertices.toLocaleString()+'\nTriangles: '+triangles.toLocaleString()+'\nTextures: '+model.textures.length+'\nMSET data: '+(model.msetSections||[]).length+' sections / '+msetChannels+' channels / '+msetCurves+' curves';
 const poseButton=document.getElementById('mset-pose');poseButton.hidden=usableMsets.length===0;poseButton.textContent='Apply MSET pose';
 document.getElementById('mset-play').hidden=playableMsets.length===0;
 const bonesButton=document.getElementById('bones');bonesButton.hidden=model.joints.length<2;bonesButton.textContent=showBones?'Hide bones':'Show bones';
 status.textContent='';const exportButton=document.getElementById('export');exportButton.disabled=false;exportButton.hidden=false;textureModal.hidden=true;renderTextures(model);renderArchives();
 draw();
}
function multiplyRowMajor(a,b){
 const result=new Array(16).fill(0);
 for(let row=0;row<4;row++)for(let col=0;col<4;col++)for(let k=0;k<4;k++)result[row*4+col]+=a[row*4+k]*b[k*4+col];
 return result;
}
function transformPoint(point,matrix){
 return [
  point[0]*matrix[0]+point[1]*matrix[4]+point[2]*matrix[8]+matrix[12],
  point[0]*matrix[1]+point[1]*matrix[5]+point[2]*matrix[9]+matrix[13],
  point[0]*matrix[2]+point[1]*matrix[6]+point[2]*matrix[10]+matrix[14]
 ];
}
function setMsetPose(sectionIndex){
 setMsetAnimationFrame(sectionIndex,0);
}
function sampleMsetCurve(curve,frame){
 const keys=curve.keys;if(!keys?.length)return 0;
 if(frame<=keys[0].frame)return keys[0].value;
 for(let i=1;i<keys.length;i++){
  const previous=keys[i-1],next=keys[i];if(frame>next.frame)continue;
  const duration=next.frame-previous.frame;if(duration<=0)return next.value;
  const t=Math.max(0,Math.min(1,(frame-previous.frame)/duration)),t2=t*t,t3=t2*t;
  return (2*t3-3*t2+1)*previous.value+(t3-2*t2+t)*duration*previous.outTangent+
   (-2*t3+3*t2)*next.value+(t3-t2)*duration*next.inTangent;
 }
 return keys[keys.length-1].value;
}
function buildMsetLocals(model,section,frame){
 const values=model.joints.map(joint=>joint.transformChannels.slice());
 for(const channel of section.channels)values[channel.joint][channel.channel-1]=channel.value;
 for(const curve of section.curves)values[curve.joint][curve.channel-1]=sampleMsetCurve(curve,frame);
 const basis=[-.01,0,0,0,0,.01,0,0,0,0,.01,0,0,0,0,1];
 const inverseBasis=[-100,0,0,0,0,100,0,0,0,0,100,0,0,0,0,1];
 return values.map(v=>{
  const [sx,sy,sz,rx,ry,rz,tx,ty,tz]=v,cx=Math.cos(rx),sxR=Math.sin(rx),cy=Math.cos(ry),syR=Math.sin(ry),cz=Math.cos(rz),szR=Math.sin(rz);
  const scale=[sx,0,0,0,0,sy,0,0,0,0,sz,0,0,0,0,1];
  const rotX=[1,0,0,0,0,cx,sxR,0,0,-sxR,cx,0,0,0,0,1];
  const rotY=[cy,0,-syR,0,0,1,0,0,syR,0,cy,0,0,0,0,1];
  const rotZ=[cz,szR,0,0,-szR,cz,0,0,0,0,1,0,0,0,0,1];
  const translate=[1,0,0,0,0,1,0,0,0,0,1,0,tx,ty,tz,1];
  let local=multiplyRowMajor(scale,rotX);local=multiplyRowMajor(local,rotY);local=multiplyRowMajor(local,rotZ);
  local=multiplyRowMajor(local,translate);
  return multiplyRowMajor(multiplyRowMajor(inverseBasis,local),basis);
 });
}
function setMsetAnimationFrame(sectionIndex,frame){
 if(current<0)return;
 const model=models[current],section=sectionIndex>=0?model.msetSections[sectionIndex]:null;
 const world=section?buildMsetWorld(model,section,frame):null;
 for(const mesh of gpu){
  mesh.positions.set(mesh.basePositions);
  if(world&&mesh.jointIds.length===mesh.positions.length/3){
   for(let vertex=0;vertex<mesh.jointIds.length;vertex++){
    const joint=mesh.jointIds[vertex];if(joint<0||joint>=model.joints.length)continue;
    const base=mesh.basePositions.subarray(vertex*3,vertex*3+3);
    const posed=transformPoint(base,
      multiplyRowMajor(model.joints[joint].inverseBindMatrix,world[joint]));
    mesh.positions.set(posed,vertex*3);
   }
  }
  gl.bindBuffer(gl.ARRAY_BUFFER,mesh.p);gl.bufferData(gl.ARRAY_BUFFER,mesh.positions,gl.STATIC_DRAW);
 }
 currentMsetSection=sectionIndex;currentMsetFrame=frame;
 const button=document.getElementById('mset-pose');
 const sections=model.msetSections.map((item,index)=>({item,index}))
  .filter(entry=>(entry.item.channels.length||entry.item.curves.length)&&entry.item.jointLocalMatrices?.length===model.joints.length);
 const position=sections.findIndex(entry=>entry.index===sectionIndex);
 button.textContent=position>=0&&(position+1<sections.length||msetPlaybackTimer!==null)
  ?`MSET pose ${position+1}/${sections.length} · Next`:sectionIndex>=0?'Restore base pose':'Apply MSET pose';
 draw();
}
function buildMsetWorld(model,section,frame){
 const world=[],locals=buildMsetLocals(model,section,frame);
 for(let i=0;i<model.joints.length;i++){
  const local=locals[i],parent=model.joints[i].parentIndex;
  world[i]=parent>=0&&parent<i?multiplyRowMajor(local,world[parent]):local;
 }
 return world;
}
function buildPreviewJointWorld(model){
 const section=currentMsetSection>=0?model.msetSections[currentMsetSection]:null;
 if(section&&(section.channels.length||section.curves.length)&&section.jointLocalMatrices?.length===model.joints.length)
  return buildMsetWorld(model,section,currentMsetFrame);
 const world=[];
 for(let i=0;i<model.joints.length;i++){
  const local=model.joints[i].localMatrix,parent=model.joints[i].parentIndex;
  world[i]=parent>=0&&parent<i?multiplyRowMajor(local,world[parent]):local;
 }
 return world;
}
function drawBones(model){
 if(!bonePositionBuffer){bonePositionBuffer=gl.createBuffer();boneColorBuffer=gl.createBuffer();boneUvBuffer=gl.createBuffer();}
 const world=buildPreviewJointWorld(model),positions=[],colors=[];
 const addLine=(a,b,color)=>{
  positions.push(...a,...b);
  colors.push(...color,...color);
 };
 const pointSize=Math.max(radius*.008,.002);
 for(let i=0;i<model.joints.length;i++){
  const joint=model.joints[i],matrix=world[i],point=[matrix[12],matrix[13],matrix[14]],parent=joint.parentIndex;
  if(parent>=0&&parent<i){const parentMatrix=world[parent];addLine([parentMatrix[12],parentMatrix[13],parentMatrix[14]],point,[80,220,255,255]);}
  addLine([point[0]-pointSize,point[1],point[2]],[point[0]+pointSize,point[1],point[2]],[255,191,70,255]);
  addLine([point[0],point[1]-pointSize,point[2]],[point[0],point[1]+pointSize,point[2]],[255,191,70,255]);
  addLine([point[0],point[1],point[2]-pointSize],[point[0],point[1],point[2]+pointSize],[255,191,70,255]);
 }
 gl.disable(gl.DEPTH_TEST);
 gl.bindBuffer(gl.ARRAY_BUFFER,bonePositionBuffer);gl.bufferData(gl.ARRAY_BUFFER,new Float32Array(positions),gl.DYNAMIC_DRAW);
 gl.enableVertexAttribArray(loc.position);gl.vertexAttribPointer(loc.position,3,gl.FLOAT,false,0,0);
 gl.bindBuffer(gl.ARRAY_BUFFER,boneColorBuffer);gl.bufferData(gl.ARRAY_BUFFER,new Uint8Array(colors),gl.DYNAMIC_DRAW);
 gl.enableVertexAttribArray(loc.color);gl.vertexAttribPointer(loc.color,4,gl.UNSIGNED_BYTE,true,0,0);
 gl.bindBuffer(gl.ARRAY_BUFFER,boneUvBuffer);gl.bufferData(gl.ARRAY_BUFFER,new Float32Array(positions.length/3*2),gl.DYNAMIC_DRAW);
 gl.enableVertexAttribArray(loc.uv);gl.vertexAttribPointer(loc.uv,2,gl.FLOAT,false,0,0);
 gl.uniform1i(loc.has,0);gl.drawArrays(gl.LINES,0,positions.length/3);
 gl.enable(gl.DEPTH_TEST);
}
function getUsableMsetSections(model){return model.msetSections.map((section,index)=>({section,index}))
 .filter(item=>(item.section.channels.length||item.section.curves.length)&&item.section.jointLocalMatrices?.length===model.joints.length)
 .map(item=>item.index);}
function getUsableMsetAnimations(model){return model.msetSections.map((section,index)=>({section,index}))
 .filter(item=>item.section.curves.length&&item.section.frameEnd>0&&item.section.framesPerSecond>0&&item.section.jointLocalMatrices?.length===model.joints.length)
 .map(item=>item.index);}
function perspective(fovy,aspect,near,far){const f=1/Math.tan(fovy/2),nf=1/(near-far);return new Float32Array([f/aspect,0,0,0,0,f,0,0,0,0,(far+near)*nf,-1,0,0,2*far*near*nf,0]);}
function lookAt(eye,target){let z=norm(sub(eye,target)),x=norm(cross([0,1,0],z)),y=cross(z,x);return new Float32Array([x[0],y[0],z[0],0,x[1],y[1],z[1],0,x[2],y[2],z[2],0,-dot(x,eye),-dot(y,eye),-dot(z,eye),1]);}
function multiply(a,b){const o=new Float32Array(16);for(let c=0;c<4;c++)for(let r=0;r<4;r++)o[c*4+r]=a[r]*b[c*4]+a[4+r]*b[c*4+1]+a[8+r]*b[c*4+2]+a[12+r]*b[c*4+3];return o;}
function sub(a,b){return a.map((v,i)=>v-b[i]);}function dot(a,b){return a.reduce((s,v,i)=>s+v*b[i],0);}function cross(a,b){return[a[1]*b[2]-a[2]*b[1],a[2]*b[0]-a[0]*b[2],a[0]*b[1]-a[1]*b[0]];}function norm(a){const l=Math.hypot(...a)||1;return a.map(v=>v/l);}
function draw(){
 const dpr=Math.min(devicePixelRatio||1,2),w=Math.max(1,Math.floor(canvas.clientWidth*dpr)),h=Math.max(1,Math.floor(canvas.clientHeight*dpr));
 if(canvas.width!==w||canvas.height!==h){canvas.width=w;canvas.height=h;}
 gl.viewport(0,0,w,h);gl.clear(gl.COLOR_BUFFER_BIT|gl.DEPTH_BUFFER_BIT);if(current<0)return;
 const target=[center[0]+pan[0],center[1]+pan[1],center[2]+pan[2]],sin=Math.sin(phi),eye=[target[0]+distance*sin*Math.sin(theta),target[1]+distance*Math.cos(phi),target[2]+distance*sin*Math.cos(theta)];
 const mvp=multiply(perspective(Math.PI/4,w/h,Math.max(.001,distance/1000),distance+radius*20),lookAt(eye,target));gl.uniformMatrix4fv(loc.mvp,false,mvp);
 if(grid){
  gl.bindBuffer(gl.ARRAY_BUFFER,grid.p);gl.enableVertexAttribArray(loc.position);gl.vertexAttribPointer(loc.position,3,gl.FLOAT,false,0,0);
  gl.bindBuffer(gl.ARRAY_BUFFER,grid.c);gl.enableVertexAttribArray(loc.color);gl.vertexAttribPointer(loc.color,4,gl.UNSIGNED_BYTE,true,0,0);
  gl.bindBuffer(gl.ARRAY_BUFFER,grid.uv);gl.enableVertexAttribArray(loc.uv);gl.vertexAttribPointer(loc.uv,2,gl.FLOAT,false,0,0);
  gl.uniform1i(loc.has,0);gl.drawArrays(gl.LINES,0,grid.count);
 }
 for(const g of gpu){gl.bindBuffer(gl.ARRAY_BUFFER,g.p);gl.enableVertexAttribArray(loc.position);gl.vertexAttribPointer(loc.position,3,gl.FLOAT,false,0,0);
  gl.bindBuffer(gl.ARRAY_BUFFER,g.c);gl.enableVertexAttribArray(loc.color);gl.vertexAttribPointer(loc.color,4,gl.UNSIGNED_BYTE,true,0,0);
  gl.bindBuffer(gl.ARRAY_BUFFER,g.uv);gl.enableVertexAttribArray(loc.uv);gl.vertexAttribPointer(loc.uv,2,gl.FLOAT,false,0,0);
  gl.uniform1i(loc.has,g.texture?1:0);if(g.texture){gl.activeTexture(gl.TEXTURE0);gl.bindTexture(gl.TEXTURE_2D,g.texture);gl.uniform1i(loc.tex,0);}
  gl.bindBuffer(gl.ELEMENT_ARRAY_BUFFER,g.i);gl.drawElements(gl.TRIANGLES,g.count,gl.UNSIGNED_INT,0);
 }
  if(showBones&&models[current].joints.length>1)drawBones(models[current]);
}
function renderArchives(){
 archiveList.replaceChildren();
 for(const [name,items] of archives){
  const row=document.createElement('div');row.className='archive-row';
  const toggle=document.createElement('button');toggle.className='archive-toggle';toggle.textContent=expandedArchives.has(name)?'▾':'▸';toggle.title='Show models';toggle.addEventListener('click',()=>{if(expandedArchives.has(name))expandedArchives.delete(name);else expandedArchives.add(name);renderArchives();});
  const label=document.createElement('button');label.className='archive-name';label.textContent=name;label.title=name;label.addEventListener('dblclick',()=>{if(items.length)loadModel(models.indexOf(items[0]));});
  row.append(toggle,label);archiveList.append(row);
  if(!expandedArchives.has(name))continue;
  const children=document.createElement('div');children.className='model-list';
  if(items.length===0){const empty=document.createElement('div');empty.className='model-empty';empty.textContent='No usable MDLS/MFA models';children.append(empty);}
  for(const item of items){
   const button=document.createElement('button');button.className='model-name'+(models[current]===item?' active':'');button.textContent=item.format+' / '+item.name;button.title='Click to view '+item.name;button.dataset.modelIndex=models.indexOf(item);
   button.addEventListener('click',()=>{loadModel(models.indexOf(item));archiveList.querySelector('.model-name.active')?.focus();});
   children.append(button);
  }
  archiveList.append(children);
 }
}
archiveList.addEventListener('keydown',event=>{
 if(event.key!=='ArrowUp'&&event.key!=='ArrowDown')return;
 const buttons=Array.from(archiveList.querySelectorAll('.model-name'));
 if(!buttons.length)return;
 const focused=event.target.closest('.model-name');
 const currentButton=focused||buttons.find(button=>Number(button.dataset.modelIndex)===current);
 const currentIndex=currentButton?buttons.indexOf(currentButton):-1;
 const nextIndex=currentIndex<0?(event.key==='ArrowDown'?0:buttons.length-1):Math.max(0,Math.min(buttons.length-1,currentIndex+(event.key==='ArrowDown'?1:-1)));
 event.preventDefault();
 const next=buttons[nextIndex],modelIndex=Number(next.dataset.modelIndex);
 loadModel(modelIndex);
 const active=archiveList.querySelector('.model-name.active');
 active?.focus();
 active?.scrollIntoView({block:'nearest'});
});
async function openArchives(fileList){
 const files=Array.from(fileList).filter(f=>f.name.toLowerCase().endsWith('.ard')).sort((a,b)=>a.webkitRelativePath.localeCompare(b.webkitRelativePath)||a.name.localeCompare(b.name));
 if(!files.length){status.textContent='No .ard files were found.';return;}
 status.textContent='';
 for(const file of files){
  const name=file.webkitRelativePath||file.name;
  try{
   const response=await fetch('/load?name='+encodeURIComponent(name),{method:'POST',headers:{'Content-Type':'application/octet-stream'},body:await file.arrayBuffer()});
   if(!response.ok)throw new Error(await response.text());
   const loaded=await response.json();
   for(const model of loaded.models)models.push(model);
   archives.set(name,loaded.models);expandedArchives.add(name);
   if(loaded.models.length)loadModel(models.indexOf(loaded.models[0]));else renderArchives();
  }catch(error){status.textContent='Could not open '+name+': '+error.message;}
 }
 renderArchives();
}
function calculateNormals(positions,indices){
 const normals=new Float32Array(positions.length);
 for(let i=0;i<indices.length;i+=3){
  const a=indices[i]*3,b=indices[i+1]*3,c=indices[i+2]*3;
  const ab=[positions[b]-positions[a],positions[b+1]-positions[a+1],positions[b+2]-positions[a+2]];
  const ac=[positions[c]-positions[a],positions[c+1]-positions[a+1],positions[c+2]-positions[a+2]];
  const n=cross(ab,ac);
  for(const vertex of [a,b,c])for(let axis=0;axis<3;axis++)normals[vertex+axis]+=n[axis];
 }
 for(let i=0;i<normals.length;i+=3){const n=norm([normals[i],normals[i+1],normals[i+2]]);normals.set(n,i);}
 return normals;
}
function makeGltf(model,msetSectionIndex=-1,msetFrame=0){
 const parts=[],bufferViews=[],accessors=[];let byteLength=0;
 function addAccessor(data,componentType,type,count,target,normalized=false,min,max){
  const padding=(4-byteLength%4)%4;if(padding){parts.push(new Uint8Array(padding));byteLength+=padding;}
  const bytes=new Uint8Array(data.buffer,data.byteOffset,data.byteLength),viewIndex=bufferViews.length;
  const view={buffer:0,byteOffset:byteLength,byteLength:bytes.length};if(target)view.target=target;
  bufferViews.push(view);
  parts.push(bytes);byteLength+=bytes.length;
  const accessor={bufferView:viewIndex,componentType,type,count};
  if(normalized)accessor.normalized=true;if(min)accessor.min=min;if(max)accessor.max=max;
  accessors.push(accessor);return accessors.length-1;
 }
 const meshes=[],meshUsesSkin=[],hasSkeleton=model.joints?.length>1;
 for(const source of model.meshes){
  const useSkin=hasSkeleton&&source.jointIds?.length===source.positions.length/3;
  const sourcePositions=source.positions;
  const positions=new Float32Array(sourcePositions),indices=new Uint32Array(source.indices),uvs=new Float32Array(source.uvs),colors=Uint8Array.from(atob(source.colors),c=>c.charCodeAt(0));
  if(model.format==='MDLS')for(let i=1;i<uvs.length;i+=2)uvs[i]=1-uvs[i];
  const indexData=positions.length/3<=65535?new Uint16Array(indices):indices;
  const min=[Infinity,Infinity,Infinity],max=[-Infinity,-Infinity,-Infinity];
  for(let i=0;i<positions.length;i+=3)for(let axis=0;axis<3;axis++){min[axis]=Math.min(min[axis],positions[i+axis]);max[axis]=Math.max(max[axis],positions[i+axis]);}
  const attributes={
   POSITION:addAccessor(positions,5126,'VEC3',positions.length/3,34962,false,min,max),
   NORMAL:addAccessor(calculateNormals(positions,indices),5126,'VEC3',positions.length/3,34962)
  };
  if(useSkin){
   const jointValues=new Uint16Array(source.jointIds.length*4),weightValues=new Float32Array(source.jointIds.length*4);
   for(let i=0;i<source.jointIds.length;i++){jointValues[i*4]=source.jointIds[i];weightValues[i*4]=1;}
   if(model.joints.length<=256){
    const bytes=Uint8Array.from(jointValues);
    attributes.JOINTS_0=addAccessor(bytes,5121,'VEC4',source.jointIds.length,34962);
   }else attributes.JOINTS_0=addAccessor(jointValues,5123,'VEC4',source.jointIds.length,34962);
   attributes.WEIGHTS_0=addAccessor(weightValues,5126,'VEC4',source.jointIds.length,34962);
  }
  if(uvs.length===positions.length/3*2)attributes.TEXCOORD_0=addAccessor(uvs,5126,'VEC2',positions.length/3,34962);
  if(colors.length===positions.length/3*4)attributes.COLOR_0=addAccessor(colors,5121,'VEC4',colors.length/4,34962,true);
  const textureIndex=Number.isInteger(source.texture)&&source.texture>=0&&source.texture<model.textures.length?source.texture:-1;
  meshes.push({name:source.name,primitives:[{attributes,indices:addAccessor(indexData,indexData instanceof Uint16Array?5123:5125,'SCALAR',indexData.length,34963),material:textureIndex<0?model.textures.length:textureIndex,mode:4}]});
  meshUsesSkin.push(useSkin);
 }
 const nodes=meshes.map((mesh,i)=>({mesh:i,name:model.meshes[i].name,...(meshUsesSkin[i]?{skin:0}:{})}));
 const sceneNodes=nodes.map((_,i)=>i),skins=[];
 const msetSection=msetSectionIndex>=0?model.msetSections[msetSectionIndex]:null;
 const msetLocals=msetSection?buildMsetLocals(model,msetSection,msetFrame):null;
 if(meshUsesSkin.some(Boolean)){
  const boneOffset=nodes.length,boneNodes=model.joints.map((_,i)=>boneOffset+i);
  for(let i=0;i<model.joints.length;i++){
   const joint=model.joints[i],matrix=msetLocals?.length===model.joints.length?msetLocals[i]:joint.localMatrix;
   const children=model.joints.map((candidate,index)=>candidate.parentIndex===i?boneNodes[index]:-1).filter(index=>index>=0);
   nodes.push({name:joint.name,matrix,...(children.length?{children}:{})});
   if(joint.parentIndex<0)sceneNodes.push(boneNodes[i]);
  }
  const inverseBind=new Float32Array(model.joints.flatMap(joint=>joint.inverseBindMatrix));
  const inverseBindAccessor=addAccessor(inverseBind,5126,'MAT4',model.joints.length,0);
  const skeletonRoot=model.joints.findIndex(joint=>joint.parentIndex<0);
  skins.push({name:model.name+' armature',joints:boneNodes,skeleton:boneNodes[skeletonRoot>=0?skeletonRoot:0],inverseBindMatrices:inverseBindAccessor});
 }
 let totalLength=0;for(const part of parts)totalLength+=part.length;
 const binary=new Uint8Array(totalLength);let offset=0;for(const part of parts){binary.set(part,offset);offset+=part.length;}
 let binaryString='';for(let i=0;i<binary.length;i+=0x8000)binaryString+=String.fromCharCode(...binary.subarray(i,i+0x8000));
 const samplers=model.textures.map(texture=>({magFilter:9729,minFilter:9987,wrapS:texture.repeat?10497:33071,wrapT:texture.repeat?10497:33071}));
 const textureFiles=model.textures.map((texture,i)=>({fileName:'texture_'+String(i+1).padStart(3,'0')+'.png',data:texturePreviewUrl(texture)}));
 const images=textureFiles.map((file,i)=>({name:model.textures[i].name||file.fileName,uri:file.fileName}));
 const textures=model.textures.map((_,i)=>({sampler:i,source:i}));
 const materials=model.textures.map((_,i)=>({name:'Material '+(i+1),pbrMetallicRoughness:{baseColorTexture:{index:i},metallicFactor:0,roughnessFactor:1},alphaMode:'BLEND'}));
 materials.push({name:'Untextured',pbrMetallicRoughness:{baseColorFactor:[1,1,1,1],metallicFactor:0,roughnessFactor:1}});
 const gltf={asset:{version:'2.0',generator:'ARD Extractor'},scene:0,scenes:[{nodes:sceneNodes}],nodes,meshes,materials,images,textures,samplers,accessors,bufferViews,buffers:[{byteLength,uri:'data:application/octet-stream;base64,'+btoa(binaryString)}]};
 if(skins.length)gltf.skins=skins;
 return {gltf:JSON.stringify(gltf),textureFiles};
}
document.getElementById('export').addEventListener('click',async event=>{
 if(current<0)return;
 const button=event.currentTarget,model=models[current];button.disabled=true;status.style.color='';
 try{
  const exported=makeGltf(model,currentMsetSection,currentMsetFrame);
  const response=await fetch('/export',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({modelName:model.name,gltf:exported.gltf,textures:exported.textureFiles})});
  if(!response.ok)throw new Error(await response.text());
  const result=await response.json();status.style.color='#9be0ad';status.textContent='Exported: '+result.path;
 }catch(error){status.textContent='Could not export '+model.name+': '+error.message;}
 finally{button.disabled=false;}
});
document.getElementById('bones').addEventListener('click',event=>{
 showBones=!showBones;
 event.currentTarget.textContent=showBones?'Hide bones':'Show bones';
 draw();
});
document.getElementById('mset-pose').addEventListener('click',()=>{
 if(current<0)return;
 if(msetPlaybackTimer!==null)stopMsetPlayback();
 const sections=getUsableMsetSections(models[current]);
 if(!sections.length)return;
 const position=sections.indexOf(currentMsetSection);
 setMsetPose(position<0?sections[0]:position+1<sections.length?sections[position+1]:-1);
});
document.getElementById('mset-play').addEventListener('click',()=>{
 if(msetPlaybackTimer!==null){stopMsetPlayback();return;}
 const button=document.getElementById('mset-play'),model=models[current];
 if(!model)return;
 const sections=getUsableMsetAnimations(model);
 if(!sections.length)return;
 msetPlaybackSection=sections.includes(currentMsetSection)?currentMsetSection:sections[0];
 msetPlaybackStart=null;currentMsetFrame=0;
 button.textContent='Pause';
 const advance=timestamp=>{
  if(msetPlaybackTimer===null||current<0)return;
  if(msetPlaybackStart===null)msetPlaybackStart=timestamp;
  const section=model.msetSections[msetPlaybackSection];
  currentMsetFrame=((timestamp-msetPlaybackStart)*section.framesPerSecond/1000)%section.frameEnd;
  setMsetAnimationFrame(msetPlaybackSection,currentMsetFrame);
  msetPlaybackTimer=requestAnimationFrame(advance);
 };
 msetPlaybackTimer=requestAnimationFrame(advance);
});
document.getElementById('open-ard').addEventListener('click',()=>document.getElementById('ard-file').click());
document.getElementById('open-folder').addEventListener('click',()=>document.getElementById('ard-folder').click());
document.getElementById('ard-file').addEventListener('change',event=>{openArchives(event.target.files);event.target.value='';});
document.getElementById('ard-folder').addEventListener('change',event=>{openArchives(event.target.files);event.target.value='';});
document.getElementById('reset').addEventListener('click',()=>{if(current>=0){theta=.65;phi=1.05;distance=radius*2.8;pan=[0,0,0];draw();}});
textureModal.addEventListener('click',()=>{textureModal.hidden=true;});
document.getElementById('texture-preview').addEventListener('click',event=>event.stopPropagation());
window.addEventListener('keydown',event=>{if(event.key==='Escape'&&!textureModal.hidden)textureModal.hidden=true;});
canvas.addEventListener('pointerdown',e=>{drag={x:e.clientX,y:e.clientY,shift:e.shiftKey||e.button===1};if(e.button===1)e.preventDefault();canvas.setPointerCapture(e.pointerId);});
canvas.addEventListener('pointermove',e=>{if(!drag)return;const dx=e.clientX-drag.x,dy=e.clientY-drag.y;drag.x=e.clientX;drag.y=e.clientY;if(drag.shift){const scale=distance/canvas.clientHeight*.8,right=[Math.cos(theta),0,-Math.sin(theta)],up=[-Math.sin(theta)*Math.cos(phi),Math.sin(phi),-Math.cos(theta)*Math.cos(phi)];for(let i=0;i<3;i++)pan[i]+=(-dx*right[i]+dy*up[i])*scale;}else{theta-=dx*.008;phi=Math.max(.08,Math.min(3.06,phi-dy*.008));}draw();});
canvas.addEventListener('contextmenu',event=>event.preventDefault());
canvas.addEventListener('pointerup',()=>drag=null);canvas.addEventListener('wheel',e=>{distance=Math.max(radius*.2,Math.min(radius*100,distance*Math.exp(e.deltaY*.001)));draw();},{passive:true});
window.addEventListener('resize',draw);
if(models.length===0){status.textContent='No usable MDLS/MFA models were found in this ARD.';stats.textContent='0 models';}
else{expandedArchives.add(initialArchive);loadModel(0);}
renderArchives();
</script>
</body>
</html>
""";
}
