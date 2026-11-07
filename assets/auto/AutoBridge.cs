// Common uGUI bridge. Deliberately has no project classes, scene names or private game calls.
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;
using UnityEditor;
using UnityEditor.SceneManagement;
using Object = UnityEngine.Object;

namespace UnityMotionMatch.Automatic {
[Serializable] public class Request {
 public string scene="",canvas="",execution="play"; public string[] targets=new string[0];
 public int width,height,fps=30,maxTargets=6,maxCases=1,maxScenes=64;
 public float duration=2,settleSeconds=3,timeoutSeconds=60; public bool includeInactive;
}
[Serializable] public class Binding {
 public string id,scene,indexPath,path,globalId,name,origin; public bool runtimeOnly;
}
[Serializable] public class NodeInfo {
 public Binding binding; public string parent,driver; public bool active,activeSelf,layoutDriven;
 public int graphics,selectables; public string[] components; public float width,height;
}
[Serializable] public class CanvasInfo {
 public Binding binding; public string renderMode; public bool active;
 public int graphics,selectables,missingScripts; public float referenceWidth,referenceHeight;
 public List<NodeInfo> nodes=new List<NodeInfo>();
}
[Serializable] public class SceneInfo {
 public string path,guid,error; public bool enabledInBuild; public int uiToolkitDocuments;
 public List<CanvasInfo> canvases=new List<CanvasInfo>();
}
[Serializable] public class Inventory {
 public string entryScene,unityVersion; public int availableScenes; public bool truncated;
 public List<SceneInfo> scenes=new List<SceneInfo>();
}
[Serializable] public class Target {
 public Binding binding; public string reason; public float baseAlpha=1;
}
[Serializable] public class CasePlan {
 public string caseName; public Binding canvas; public List<Target> targets=new List<Target>();
}
[Serializable] public class Plan {
 public string entryScene,execution; public int width,height,fps=30; public float duration=2;
 public List<CasePlan> cases=new List<CasePlan>(); public List<string> warnings=new List<string>();
}
[Serializable] public class MetaItem {
 public string name,displayName,path,template; public bool layoutDriven;
 public float x,y,w,h,pivotX,pivotY,baseAlpha=1;
}
[Serializable] public class MetaCase {
 public string caseName; public int transforms,buttons,graphics,layouts;
 public List<MetaItem> elements=new List<MetaItem>();
}
[Serializable] public class Metadata {
 public int width,height,fps; public float duration; public string colorSpace;public List<MetaCase> cases=new List<MetaCase>();
}
[Serializable] public class Key {public float t,x,y,scale=1,alpha=1;}
[Serializable] public class Track {public string name;public Key[] keys;}
[Serializable] public class Solution {public string caseName;public Track[] tracks;}
[Serializable] public class Item {public string name;public float x,y,scale,alpha;}
[Serializable] public class Sample {public float t;public List<Item> items=new List<Item>();}
[Serializable] public class Check {public string name,detail;public bool pass;}
[Serializable] public class Result {
 public string caseName,revision,execution,semanticValidation="not performed: generic binding does not infer game outcomes";
 public bool nativeCapture,enteredPlayMode,layoutPreserved,listenerPreserved,referencesPreserved;
 public int raycastProbes,transformCount,buttonCount;
 public List<Sample> samples=new List<Sample>();public List<Check> checks=new List<Check>();
}
[Serializable] public class AutoReport {
 public string execution,entryScene; public int cases,targets;
 public bool adapterCodeRequired=false,gameSpecificSemanticsVerified=false;
 public List<string> warnings=new List<string>();
}
[Serializable] public class Failure {public string mode,message,type;}

[InitializeOnLoad] public static class AutoBridge {
 const string Active="motion.auto.active", Started="motion.auto.started";
 static string Arg(string key,string fallback="") {var a=Environment.GetCommandLineArgs();int i=Array.IndexOf(a,key);return i>=0&&i+1<a.Length?a[i+1]:fallback;}
 static string Root=>Arg("-motionWorkspace");
 static string Mode=>Arg("-motionMode","prepare");
 static string Revision=>Arg("-motionRevision","r0");
 static Request request;
 static HashSet<string> serializedIds=new HashSet<string>(),serializedPaths=new HashSet<string>(),scannedScenes=new HashSet<string>();
 static double stableSince;static string stableShape;
 static AutoBridge(){if(SessionState.GetBool(Active,false))EditorApplication.update+=Tick;}
 static void Write(string path,object data){Directory.CreateDirectory(Path.GetDirectoryName(path));File.WriteAllText(path,JsonUtility.ToJson(data,true),new System.Text.UTF8Encoding(false));}
 static T Read<T>(string file)=>JsonUtility.FromJson<T>(File.ReadAllText(Path.Combine(Root,file)));
 static void LoadRequest(){
  if(!Path.IsPathRooted(Root)||!Directory.Exists(Root))throw new ArgumentException("Absolute existing -motionWorkspace required");request=Read<Request>("auto-request.json");
  serializedIds.Clear();serializedPaths.Clear();scannedScenes.Clear();if(File.Exists(Root+"/scene-inventory.json")){var inv=Read<Inventory>("scene-inventory.json");foreach(var scene in inv.scenes){if(!string.IsNullOrEmpty(scene.error))continue;scannedScenes.Add(scene.path);foreach(var c in scene.canvases){serializedIds.Add(c.binding.globalId);serializedPaths.Add(c.binding.id);foreach(var n in c.nodes){serializedIds.Add(n.binding.globalId);serializedPaths.Add(n.binding.id);}}}}
 }
 static void Fail(Exception e){Debug.LogException(e);if(Directory.Exists(Root))Write(Root+"/auto-error.json",new Failure{mode=Mode,message=e.Message,type=e.GetType().Name});SessionState.SetBool(Active,false);EditorApplication.update-=Tick;EditorApplication.Exit(2);}
 static void Finish(){SessionState.SetBool(Active,false);EditorApplication.update-=Tick;EditorApplication.Exit(0);}
 static string SceneKey(Scene s)=>string.IsNullOrEmpty(s.path)?"runtime:"+s.name:s.path;
 static string IndexPath(Transform t){var a=new List<int>();for(var p=t;p!=null;p=p.parent)a.Add(p.GetSiblingIndex());a.Reverse();return string.Join("/",a);}
 static string DisplayPath(Transform t){var a=new List<string>();for(var p=t;p!=null;p=p.parent)a.Add(p.name);a.Reverse();return string.Join("/",a);}
 static Binding Bind(Transform t){var scene=SceneKey(t.gameObject.scene);var path=DisplayPath(t);var index=IndexPath(t);var g=GlobalObjectId.GetGlobalObjectIdSlow(t.gameObject).ToString();var id="t_"+Hash128.Compute(scene+"|"+index+"|"+path).ToString().Substring(0,12);string origin=!Application.isPlaying?"serialized":serializedIds.Contains(g)?"serialized":serializedPaths.Contains(id)?"serialized-hierarchy":"runtime-unclassified";return new Binding{id=id,scene=scene,indexPath=index,path=path,name=t.name,globalId=g,origin=origin,runtimeOnly=origin=="runtime-unclassified"&&scannedScenes.Contains(scene)};}
 static Canvas[] Canvases()=>Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include,FindObjectsSortMode.None).Where(c=>c.gameObject.scene.IsValid()&&c.isRootCanvas).ToArray();
 static Transform Resolve(Binding b){
  var scenes=Enumerable.Range(0,SceneManager.sceneCount).Select(SceneManager.GetSceneAt).Concat(Canvases().Select(c=>c.gameObject.scene)).Distinct().Where(s=>SceneKey(s)==b.scene).ToArray();
  foreach(var scene in scenes){
   var indices=b.indexPath.Split('/').Select(int.Parse).ToArray();var roots=scene.GetRootGameObjects();var root=roots.FirstOrDefault(g=>g.transform.GetSiblingIndex()==indices[0]);if(root==null)continue;
   Transform t=root.transform;for(int i=1;i<indices.Length&&t!=null;i++)t=indices[i]<t.childCount?t.GetChild(indices[i]):null;
   if(t!=null&&DisplayPath(t)==b.path)return t;
  }
  throw new InvalidOperationException("Stale or missing UI binding: "+b.scene+" :: "+b.path+" ["+b.indexPath+"]. Prepare a new workspace after hierarchy changes.");
 }
 static bool Matches(Binding b,string query)=>b.id==query||b.name==query||b.path==query;
 static bool Driven(RectTransform t){
  if(t.drivenByObject!=null)return true;
  var group=t.parent==null?null:t.parent.GetComponent<LayoutGroup>();var elem=t.GetComponent<LayoutElement>();
  return group!=null&&group.isActiveAndEnabled&&(elem==null||!elem.ignoreLayout);
 }
 static bool Visible(Graphic g){
  if(!g.isActiveAndEnabled||g.color.a<.001f||g.canvasRenderer.GetAlpha()<.001f)return false;
  foreach(var a in g.GetComponentsInParent<CanvasGroup>(true)){if(a.alpha<.001f)return false;if(a.ignoreParentGroups)break;}
  return true;
 }
 static CanvasInfo Describe(Canvas canvas){
  Canvas.ForceUpdateCanvases();var scaler=canvas.GetComponent<CanvasScaler>();
  var info=new CanvasInfo{binding=Bind(canvas.transform),renderMode=canvas.renderMode.ToString(),active=canvas.gameObject.activeInHierarchy,graphics=canvas.GetComponentsInChildren<Graphic>(true).Length,selectables=canvas.GetComponentsInChildren<Selectable>(true).Length,referenceWidth=scaler?scaler.referenceResolution.x:0,referenceHeight=scaler?scaler.referenceResolution.y:0};
  foreach(var t in canvas.GetComponentsInChildren<RectTransform>(true)){
   info.missingScripts+=GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject);
   info.nodes.Add(new NodeInfo{binding=Bind(t),parent=t.parent?IndexPath(t.parent):"",active=t.gameObject.activeInHierarchy,activeSelf=t.gameObject.activeSelf,layoutDriven=Driven(t),driver=t.drivenByObject?t.drivenByObject.GetType().FullName:"",graphics=t.GetComponentsInChildren<Graphic>(true).Length,selectables=t.GetComponentsInChildren<Selectable>(true).Length,components=t.GetComponents<Component>().Where(x=>x!=null).Select(x=>x.GetType().FullName).ToArray(),width=t.rect.width,height=t.rect.height});
  }
  return info;
 }
 public static void Discover(){try{
  LoadRequest();var assets=AssetDatabase.FindAssets("t:Scene",new[]{"Assets"}).Select(AssetDatabase.GUIDToAssetPath).OrderBy(p=>p).ToArray();var build=EditorBuildSettings.scenes.Where(s=>s.enabled).Select(s=>s.path).ToArray();
  var selected=string.IsNullOrEmpty(request.scene)?assets:assets.Where(p=>p==request.scene).ToArray();
  // Build scenes take priority when a very large project needs a bounded scan.
  selected=selected.OrderBy(p=>Array.IndexOf(build,p)<0?int.MaxValue:Array.IndexOf(build,p)).ToArray();
  var inventory=new Inventory{entryScene=build.FirstOrDefault()??"",unityVersion=Application.unityVersion,availableScenes=selected.Length,truncated=selected.Length>request.maxScenes};
  foreach(var path in selected.Take(request.maxScenes)){
   var row=new SceneInfo{path=path,guid=AssetDatabase.AssetPathToGUID(path),enabledInBuild=build.Contains(path)};inventory.scenes.Add(row);
   try{var scene=EditorSceneManager.OpenScene(path,OpenSceneMode.Single);foreach(var root in scene.GetRootGameObjects()){
    row.uiToolkitDocuments+=root.GetComponentsInChildren<Component>(true).Count(c=>c!=null&&c.GetType().FullName=="UnityEngine.UIElements.UIDocument");
    foreach(var c in root.GetComponentsInChildren<Canvas>(true).Where(c=>c.isRootCanvas))row.canvases.Add(Describe(c));
   }}catch(Exception e){row.error=e.Message;}
  }
  Write(Root+"/scene-inventory.json",inventory);Finish();
 }catch(Exception e){Fail(e);}}
 public static void Run(){try{
  LoadRequest();if(string.IsNullOrEmpty(request.scene))throw new ArgumentException("No startup scene was selected");
  PlayerSettings.companyName="UnityMotionMatch";PlayerSettings.productName="Auto_"+Hash128.Compute(Root).ToString().Substring(0,16);
  EditorSceneManager.OpenScene(request.scene,OpenSceneMode.Single);
  if(request.execution=="play"){
   SessionState.SetBool(Active,true);SessionState.SetString(Started,DateTime.UtcNow.ToString("O"));EditorApplication.update+=Tick;EditorApplication.isPlaying=true;
  }else{EditorApplication.delayCall+=()=>{try{Work();Finish();}catch(Exception e){Fail(e);}};}
 }catch(Exception e){Fail(e);}}
 static void Tick(){try{
  if(!SessionState.GetBool(Active,false))return;if(request==null)LoadRequest();
  var elapsed=(DateTime.UtcNow-DateTime.Parse(SessionState.GetString(Started,DateTime.UtcNow.ToString("O")),null,System.Globalization.DateTimeStyles.RoundtripKind)).TotalSeconds;
  if(elapsed>request.timeoutSeconds)throw new TimeoutException("Normal scene startup did not produce stable screen-space uGUI. See the inventory; choose a startup --scene, allow --settle, or explicitly use --execution edit for a serialized preview.");
  if(!EditorApplication.isPlaying||EditorApplication.isCompiling||EditorApplication.isUpdating||Time.unscaledTime<request.settleSeconds)return;
  var canvases=Canvases().Where(c=>(c.gameObject.activeInHierarchy||request.includeInactive)&&c.renderMode!=RenderMode.WorldSpace).ToArray();
  if(!canvases.Any(c=>c.GetComponentsInChildren<Graphic>(request.includeInactive).Any(g=>request.includeInactive?g.enabled&&g.color.a>.001f:Visible(g))))return;
  // Loading overlays often appear before an asynchronously loaded menu. Give
  // passive-only startup UI a bounded grace period, without calling game code.
  if(!canvases.Any(c=>c.GetComponentsInChildren<Selectable>(request.includeInactive).Any(s=>request.includeInactive?s.enabled:s.isActiveAndEnabled))&&Time.unscaledTime<Math.Max(12,request.settleSeconds))return;
  if(Mode=="capture"){
   var expected=Read<Plan>("auto-bindings.json");
   try{foreach(var c in expected.cases){Resolve(c.canvas);foreach(var t in c.targets)Resolve(t.binding);}}
   catch(InvalidOperationException){return;}
  }
  string shape=string.Join("|",canvases.Select(c=>SceneKey(c.gameObject.scene)+DisplayPath(c.transform)+":"+c.GetComponentsInChildren<Transform>().Length).OrderBy(x=>x));
  if(shape!=stableShape){stableShape=shape;stableSince=EditorApplication.timeSinceStartup;return;}
  if(EditorApplication.timeSinceStartup-stableSince<Math.Max(1,request.settleSeconds))return;
  EditorApplication.update-=Tick;float old=Time.timeScale;try{Time.timeScale=0;Work();}finally{Time.timeScale=old;}Finish();
 }catch(Exception e){Fail(e);}}

 sealed class CaptureView:IDisposable {
  public Canvas canvas; public Camera camera;public RenderTexture texture;
  RenderMode originalMode;Camera originalCamera;float originalDistance;bool originalEnabled;RenderPipelineAsset pipeline,qualityPipeline;
  List<Tuple<Canvas,bool>> other=new List<Tuple<Canvas,bool>>();List<Tuple<Renderer,bool>> renderers=new List<Tuple<Renderer,bool>>();
  List<Tuple<GameObject,bool>> active=new List<Tuple<GameObject,bool>>();
  public CaptureView(Canvas c,int width,int height,bool activate){
   canvas=c;originalMode=c.renderMode;originalCamera=c.worldCamera;originalDistance=c.planeDistance;originalEnabled=c.enabled;if(activate)c.enabled=true;
   if(activate)for(var t=c.transform;t!=null;t=t.parent)if(!t.gameObject.activeSelf){active.Add(Tuple.Create(t.gameObject,false));t.gameObject.SetActive(true);}
   pipeline=GraphicsSettings.defaultRenderPipeline;qualityPipeline=QualitySettings.renderPipeline;GraphicsSettings.defaultRenderPipeline=null;QualitySettings.renderPipeline=null;
   foreach(var x in Canvases())if(x!=c){other.Add(Tuple.Create(x,x.enabled));x.enabled=false;}
   foreach(var r in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include,FindObjectsSortMode.None)){renderers.Add(Tuple.Create(r,r.enabled));r.enabled=false;}
   camera=new GameObject("__MotionMatchCapture",typeof(Camera)).GetComponent<Camera>();camera.orthographic=true;camera.transform.position=new Vector3(0,0,-100);camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.12f,.15f,.2f,1);camera.allowHDR=false;camera.allowMSAA=false;
   c.renderMode=RenderMode.ScreenSpaceCamera;c.worldCamera=camera;c.planeDistance=10;Resize(width,height);
  }
  public void Resize(int w,int h){if(texture!=null){camera.targetTexture=null;texture.Release();Object.DestroyImmediate(texture);}texture=new RenderTexture(w,h,24,RenderTextureFormat.ARGB32);texture.antiAliasing=1;texture.Create();camera.targetTexture=texture;camera.orthographicSize=h*.5f;Refresh();}
  public void Refresh(){Canvas.ForceUpdateCanvases();LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)canvas.transform);Canvas.ForceUpdateCanvases();}
  public Vector2 Screen(Vector3 p){var s=camera.WorldToScreenPoint(p);return new Vector2(s.x,texture.height-s.y);}
  public int Png(string path){
   Refresh();camera.Render();var old=RenderTexture.active;RenderTexture.active=texture;var p=new Texture2D(texture.width,texture.height,TextureFormat.RGBA32,false);
   try{p.ReadPixels(new Rect(0,0,texture.width,texture.height),0,0);p.Apply();Directory.CreateDirectory(Path.GetDirectoryName(path));File.WriteAllBytes(path,p.EncodeToPNG());return p.GetPixels32().Count(x=>x.a>2);}finally{Object.DestroyImmediate(p);RenderTexture.active=old;}
  }
  public void Dispose(){canvas.renderMode=originalMode;canvas.worldCamera=originalCamera;canvas.planeDistance=originalDistance;canvas.enabled=originalEnabled;foreach(var x in other)if(x.Item1)x.Item1.enabled=x.Item2;foreach(var x in renderers)if(x.Item1)x.Item1.enabled=x.Item2;foreach(var x in active)if(x.Item1)x.Item1.SetActive(x.Item2);if(camera){camera.targetTexture=null;Object.DestroyImmediate(camera.gameObject);}if(texture){texture.Release();Object.DestroyImmediate(texture);}GraphicsSettings.defaultRenderPipeline=pipeline;QualitySettings.renderPipeline=qualityPipeline;}
 }
 static bool Safe(RectTransform t,Canvas root,out string reason){
  reason="";if(t==root.transform){reason="canvas root";return false;}
  if(Driven(t)){reason="layout-driven";return false;}
  for(Transform p=t;p!=null&&p!=root.transform.parent;p=p.parent){if(Quaternion.Angle(p.localRotation,Quaternion.identity)>.05f){reason="rotated UI hierarchy";return false;}}
  if(Mathf.Abs(t.localScale.x)<.0001f||Mathf.Abs(t.localScale.y)<.0001f){reason="zero scale";return false;}
  if(!t.GetComponentsInChildren<Graphic>().Any(Visible)){reason="no visible graphics";return false;}
  return true;
 }
 static List<Target> Select(Canvas canvas,CaptureView view,List<string> warnings){
  var nodes=canvas.GetComponentsInChildren<RectTransform>().Where(t=>t!=canvas.transform).ToArray();var chosen=new List<RectTransform>();
  if(request.targets!=null&&request.targets.Length>0){
   foreach(var query in request.targets){var found=nodes.Where(t=>Matches(Bind(t),query)).ToArray();if(found.Length!=1)throw new InvalidOperationException("Target selector must identify exactly one active RectTransform: "+query+" (matches "+found.Length+")");string why;if(!Safe(found[0],canvas,out why))throw new InvalidOperationException("Unsafe target "+query+": "+why);chosen.Add(found[0]);}
  }else{
   Action<Transform> visit=null;visit=p=>{
    foreach(Transform child in p){var t=child as RectTransform;if(t==null||!t.gameObject.activeInHierarchy)continue;string why;
     var corners=new Vector3[4];t.GetWorldCorners(corners);var a=view.Screen(corners[0]);var b=view.Screen(corners[2]);float area=Mathf.Abs((b.x-a.x)*(b.y-a.y));
     bool broad=area>view.texture.width*view.texture.height*.82f&&t.GetComponent<Selectable>()==null;
     if(!broad&&Safe(t,canvas,out why))chosen.Add(t);else{if(Driven(t))warnings.Add("Skipped directly driven node: "+DisplayPath(t));visit(t);}
    }
   };visit(canvas.transform);
   chosen=chosen.OrderByDescending(t=>t.GetComponentsInChildren<Selectable>().Length*10+Math.Min(9,t.GetComponentsInChildren<Graphic>().Length)).ThenBy(t=>IndexPath(t)).Take(request.maxTargets).ToList();
  }
  if(chosen.Distinct().Count()!=chosen.Count||chosen.Any(t=>chosen.Any(o=>o!=t&&t.IsChildOf(o))))throw new InvalidOperationException("Targets overlap in the hierarchy; select independent groups");
  return chosen.Select(t=>new Target{binding=Bind(t),reason=request.targets.Length>0?"explicit data selector":"independent visible UI group; no direct layout driver",baseAlpha=t.GetComponent<CanvasGroup>()?t.GetComponent<CanvasGroup>().alpha:1}).ToList();
 }
 static int[] Dimensions(Canvas c){if(request.width>0&&request.height>0)return new[]{request.width,request.height};var s=c.GetComponent<CanvasScaler>();Vector2 v=s!=null&&s.uiScaleMode==CanvasScaler.ScaleMode.ScaleWithScreenSize?s.referenceResolution:new Vector2(PlayerSettings.defaultScreenWidth,PlayerSettings.defaultScreenHeight);if(v.x<64||v.y<64)v=new Vector2(960,540);float f=Mathf.Min(1,960/Mathf.Max(v.x,v.y));return new[]{Mathf.Max(64,Mathf.RoundToInt(v.x*f)),Mathf.Max(64,Mathf.RoundToInt(v.y*f))};}
 static Plan BuildPlan(){
  var plan=new Plan{entryScene=request.scene,execution=request.execution};var all=Canvases();
  foreach(var c in all.Where(c=>c.renderMode==RenderMode.WorldSpace))plan.warnings.Add("Unsupported world-space Canvas: "+DisplayPath(c.transform));
  var candidates=all.Where(c=>c.renderMode!=RenderMode.WorldSpace&&(c.isActiveAndEnabled||request.includeInactive)&&c.GetComponentsInChildren<Graphic>(true).Length>0).OrderByDescending(c=>c.GetComponentsInChildren<Selectable>().Length).ThenBy(c=>SceneKey(c.gameObject.scene)+IndexPath(c.transform)).ToArray();
  if(!string.IsNullOrEmpty(request.canvas)){candidates=candidates.Where(c=>Matches(Bind(c.transform),request.canvas)).ToArray();if(candidates.Length!=1)throw new InvalidOperationException("--canvas must identify exactly one Canvas; use the inventory binding id/path");}
  if(candidates.Length==0)throw new InvalidOperationException("No usable screen-space Canvas found in the loaded scene(s)");
  if(request.targets.Length>0&&candidates.Length>1)throw new InvalidOperationException("Choose --canvas when selecting explicit targets in a multi-Canvas scene");
  var wh=Dimensions(candidates[0]);plan.width=wh[0];plan.height=wh[1];plan.fps=request.fps;plan.duration=request.duration;
  foreach(var canvas in candidates.Take(request.maxCases))using(var view=new CaptureView(canvas,plan.width,plan.height,request.includeInactive)){
   var targets=Select(canvas,view,plan.warnings);if(targets.Count==0){plan.warnings.Add("No independent visible targets: "+DisplayPath(canvas.transform));continue;}
   var cb=Bind(canvas.transform);plan.cases.Add(new CasePlan{caseName="c_"+cb.id.Substring(2),canvas=cb,targets=targets});
  }
  if(candidates.Length>request.maxCases)plan.warnings.Add("Canvas case limit reached; use --canvas to select another discovered Canvas");
  if(plan.cases.Count==0)throw new InvalidOperationException("No safe independent UI targets. Layout-controlled or invisible elements require choosing an existing parent group, not reparenting.");
  return plan;
 }
 sealed class Pose:IDisposable {
  public RectTransform target;public CanvasGroup group;public Vector2 position;public Vector3 scale;public float alpha,xUnit,yUnit;bool added;
  public Pose(RectTransform t,CaptureView view){target=t;position=t.anchoredPosition;scale=t.localScale;group=t.GetComponent<CanvasGroup>();added=group==null;if(added)group=t.gameObject.AddComponent<CanvasGroup>();alpha=group.alpha;var p=view.Screen(t.position);var x=view.Screen(t.position+t.parent.TransformVector(Vector3.right))-p;var y=view.Screen(t.position+t.parent.TransformVector(Vector3.up))-p;xUnit=x.x;yUnit=-y.y;if(xUnit<=0||yUnit<=0||Mathf.Abs(x.y)>.01f*xUnit||Mathf.Abs(y.x)>.01f*yUnit)throw new InvalidOperationException("Unsupported rotated/reflected screen basis: "+DisplayPath(t));}
  public void Restore(){target.anchoredPosition=position;target.localScale=scale;group.alpha=alpha;}
  public void Dispose(){Restore();if(added)Object.DestroyImmediate(group);}
 }
 static Metadata Extract(Plan plan){
  var meta=new Metadata{width=plan.width,height=plan.height,fps=plan.fps,duration=plan.duration,colorSpace=QualitySettings.activeColorSpace.ToString()};
  foreach(var c in plan.cases){var canvas=Resolve(c.canvas).GetComponent<Canvas>();using(var view=new CaptureView(canvas,plan.width,plan.height,request.includeInactive)){
   var poses=c.targets.Select(t=>new Pose((RectTransform)Resolve(t.binding),view)).ToArray();try{
    string dir=Root+"/fixtures/"+c.caseName;view.Png(dir+"/rest.png");foreach(var p in poses)p.group.alpha=0;view.Png(dir+"/background.png");foreach(var p in poses)p.Restore();
    var cm=new MetaCase{caseName=c.caseName,transforms=canvas.GetComponentsInChildren<Transform>(true).Length,buttons=canvas.GetComponentsInChildren<Button>(true).Length,graphics=canvas.GetComponentsInChildren<Graphic>(true).Length,layouts=canvas.GetComponentsInChildren<LayoutGroup>(true).Length};
    for(int i=0;i<poses.Length;i++){
     var p=poses[i];var corners=new Vector3[4];p.target.GetWorldCorners(corners);var a=view.Screen(corners[0]);var b=view.Screen(corners[2]);var pivot=view.Screen(p.target.position);
     var graphics=canvas.GetComponentsInChildren<Graphic>(true);var colors=graphics.Select(g=>g.color).ToArray();var masks=canvas.GetComponentsInChildren<Mask>(true);var show=masks.Select(m=>m.showMaskGraphic).ToArray();var color=view.camera.backgroundColor;
     // Disabling Text/TMP changes ILayoutElement preferred sizes. Keep every
     // component/layout active and hide unrelated pixels using vertex alpha.
     try{for(int j=0;j<graphics.Length;j++){var g=graphics[j];bool ancestorMask=p.target.IsChildOf(g.transform)&&g.GetComponent<Mask>()!=null;if(!g.transform.IsChildOf(p.target)&&!ancestorMask){var hidden=colors[j];hidden.a=0;g.color=hidden;}}foreach(var m in masks)if(p.target.IsChildOf(m.transform)&&m.transform!=p.target)m.showMaskGraphic=false;view.camera.backgroundColor=Color.clear;
      int pixels=view.Png(dir+"/template-"+i+".png");if(pixels<12)throw new InvalidOperationException("Selected target has no visible rendered pixels: "+c.targets[i].binding.path);
     }finally{view.camera.backgroundColor=color;for(int j=0;j<graphics.Length;j++)graphics[j].color=colors[j];for(int j=0;j<masks.Length;j++)masks[j].showMaskGraphic=show[j];view.Refresh();}
     cm.elements.Add(new MetaItem{name=c.targets[i].binding.id,displayName=p.target.name,path=c.targets[i].binding.path,template="fixtures/"+c.caseName+"/template-"+i+".png",x=(a.x+b.x)/2,y=(a.y+b.y)/2,w=Mathf.Abs(b.x-a.x),h=Mathf.Abs(b.y-a.y),pivotX=pivot.x,pivotY=pivot.y,layoutDriven=Driven(p.target),baseAlpha=p.alpha});
    }
    meta.cases.Add(cm);
   }finally{foreach(var p in poses)p.Dispose();}
  }}return meta;
 }
 static string[] Layout(Canvas canvas)=>canvas.GetComponentsInChildren<RectTransform>(true).Select(t=>IndexPath(t)+"|"+t.anchorMin.ToString("F5")+t.anchorMax.ToString("F5")+t.pivot.ToString("F5")+t.sizeDelta.ToString("F4")+t.localRotation.ToString("F5")).Concat(canvas.GetComponentsInChildren<LayoutGroup>(true).Select(g=>IndexPath(g.transform)+EditorJsonUtility.ToJson(g))).ToArray();
 static string[] SerializedLinks(Canvas canvas,bool events){
  var rows=new List<string>();foreach(var component in canvas.GetComponentsInChildren<Component>(true).Where(c=>c!=null)){
   using(var so=new SerializedObject(component)){var p=so.GetIterator();while(p.NextVisible(true)){
    string prefix=IndexPath(component.transform)+"|"+component.GetType().FullName+"|"+p.propertyPath+"|";
    if(!events&&p.propertyType==SerializedPropertyType.ObjectReference)rows.Add(prefix+p.objectReferenceInstanceIDValue);
    if(events&&p.propertyPath.Contains("m_PersistentCalls")){
     if(p.propertyType==SerializedPropertyType.String)rows.Add(prefix+p.stringValue);
     else if(p.propertyType==SerializedPropertyType.ObjectReference)rows.Add(prefix+p.objectReferenceInstanceIDValue);
     else if(p.propertyType==SerializedPropertyType.Integer||p.propertyType==SerializedPropertyType.Enum)rows.Add(prefix+p.intValue);
     else if(p.propertyType==SerializedPropertyType.Boolean)rows.Add(prefix+p.boolValue);
    }
   }}
  }return rows.ToArray();
 }
 static string[] Raycasts(Canvas canvas,CaptureView view){
  if(EventSystem.current==null)return new string[0];var rows=new List<string>();
  foreach(var button in canvas.GetComponentsInChildren<Button>().Where(b=>b.isActiveAndEnabled&&b.IsInteractable())){
   var r=(RectTransform)button.transform;var p=view.camera.WorldToScreenPoint(r.TransformPoint(r.rect.center));var data=new PointerEventData(EventSystem.current){position=new Vector2(p.x,p.y)};var hits=new List<RaycastResult>();EventSystem.current.RaycastAll(data,hits);
   var first=hits.FirstOrDefault();var handler=first.gameObject?ExecuteEvents.GetEventHandler<IPointerClickHandler>(first.gameObject):null;
   rows.Add(IndexPath(r)+"=>"+(handler?DisplayPath(handler.transform)+"@"+IndexPath(handler.transform):"no hit"));
  }return rows.ToArray();
 }
 static void Curve(AnimationClip clip,Type type,string prop,Key[] keys,Func<Key,float> value){var curve=new AnimationCurve(keys.Select(k=>new Keyframe(k.t,value(k))).ToArray());for(int i=0;i<curve.length;i++){AnimationUtility.SetKeyLeftTangentMode(curve,i,AnimationUtility.TangentMode.Linear);AnimationUtility.SetKeyRightTangentMode(curve,i,AnimationUtility.TangentMode.Linear);}AnimationUtility.SetEditorCurve(clip,EditorCurveBinding.FloatCurve("",type,prop),curve);}
 static AnimationClip MakeClip(Track tr,Pose p,int fps){var clip=new AnimationClip{frameRate=fps};Curve(clip,typeof(RectTransform),"m_AnchoredPosition.x",tr.keys,k=>p.position.x+k.x/p.xUnit);Curve(clip,typeof(RectTransform),"m_AnchoredPosition.y",tr.keys,k=>p.position.y-k.y/p.yUnit);Curve(clip,typeof(Transform),"m_LocalScale.x",tr.keys,k=>p.scale.x*k.scale);Curve(clip,typeof(Transform),"m_LocalScale.y",tr.keys,k=>p.scale.y*k.scale);Curve(clip,typeof(CanvasGroup),"m_Alpha",tr.keys,k=>p.alpha*k.alpha);return clip;}
 static void Validate(Solution s,CasePlan c,Plan plan){
  if(s==null||s.caseName!=c.caseName||s.tracks==null||s.tracks.Length!=c.targets.Count||s.tracks.Select(t=>t.name).Distinct().Count()!=s.tracks.Length||s.tracks.Any(t=>!c.targets.Any(x=>x.binding.id==t.name)))throw new InvalidOperationException("Solution tracks do not match the automatic bindings");
  foreach(var tr in s.tracks){if(tr.keys==null||tr.keys.Length<2)throw new InvalidOperationException("At least two keys are required");float previous=-1;foreach(var k in tr.keys){if(new[]{k.t,k.x,k.y,k.scale,k.alpha}.Any(v=>float.IsNaN(v)||float.IsInfinity(v))||k.t<=previous||k.scale<=0||k.alpha<0||k.alpha>1||k.t<0||k.t>plan.duration+.001f)throw new InvalidOperationException("Invalid/non-monotonic animation keys");previous=k.t;}var end=tr.keys.Last();if(Mathf.Abs(end.t-plan.duration)>.04f||Mathf.Abs(end.x)>.001f||Mathf.Abs(end.y)>.001f||Mathf.Abs(end.scale-1)>.001f||Mathf.Abs(end.alpha-1)>.001f)throw new InvalidOperationException("This workflow requires the final key to restore the original pose");}
 }
 static float[] Geometry(Canvas canvas)=>canvas.GetComponentsInChildren<RectTransform>(true).SelectMany(t=>new[]{t.anchoredPosition.x,t.anchoredPosition.y,t.rect.width,t.rect.height,t.localScale.x,t.localScale.y}).ToArray();
 static void Capture(Plan plan){
  var metadata=Read<Metadata>("destination.json");
  foreach(var c in plan.cases){var solution=Read<Solution>("solutions/"+Revision+"/"+c.caseName+".json");Validate(solution,c,plan);var canvas=Resolve(c.canvas).GetComponent<Canvas>();
   using(var view=new CaptureView(canvas,plan.width,plan.height,request.includeInactive)){
    var poses=c.targets.Select(t=>new Pose((RectTransform)Resolve(t.binding),view)).ToArray();try{
     var cm=metadata.cases.Single(x=>x.caseName==c.caseName);foreach(var p in poses){var e=cm.elements.Single(x=>x.name==Bind(p.target).id);var pivot=view.Screen(p.target.position);if(Vector2.Distance(pivot,new Vector2(e.pivotX,e.pivotY))>1||Mathf.Abs(p.alpha-e.baseAlpha)>.001f)throw new InvalidOperationException("Rest geometry changed since extraction: "+DisplayPath(p.target)+". Re-prepare instead of applying stale templates.");}
     var before=Layout(canvas);var links=SerializedLinks(canvas,false);var events=SerializedLinks(canvas,true);var rays=Raycasts(canvas,view);
     var clips=poses.Select(p=>MakeClip(solution.tracks.Single(t=>t.name==Bind(p.target).id),p,plan.fps)).ToArray();var result=new Result{caseName=c.caseName,revision=Revision,execution=request.execution,nativeCapture=true,enteredPlayMode=Application.isPlaying,raycastProbes=rays.Length,transformCount=canvas.GetComponentsInChildren<Transform>(true).Length,buttonCount=canvas.GetComponentsInChildren<Button>(true).Length};
     string output=Root+"/evidence/"+Revision+"/"+c.caseName;int count=Mathf.RoundToInt(plan.duration*plan.fps);
     for(int i=0;i<=count;i++){float t=(float)i/plan.fps;for(int j=0;j<poses.Length;j++)clips[j].SampleAnimation(poses[j].target.gameObject,t);view.Refresh();view.Png(output+"/"+(i+1).ToString("D4")+".png");var sample=new Sample{t=t};foreach(var p in poses)sample.items.Add(new Item{name=Bind(p.target).id,x=(p.target.anchoredPosition.x-p.position.x)*p.xUnit,y=-(p.target.anchoredPosition.y-p.position.y)*p.yUnit,scale=p.target.localScale.x/p.scale.x,alpha=p.alpha>0?p.group.alpha/p.alpha:0});result.samples.Add(sample);}
     result.layoutPreserved=before.SequenceEqual(Layout(canvas));result.listenerPreserved=events.SequenceEqual(SerializedLinks(canvas,true));result.referencesPreserved=links.SequenceEqual(SerializedLinks(canvas,false));
     result.checks.Add(new Check{name="raycast_destinations",pass=rays.SequenceEqual(Raycasts(canvas,view)),detail=rays.Length+" button destinations compared; callbacks were not invoked"});
     foreach(var p in poses)result.checks.Add(new Check{name="restore:"+Bind(p.target).id,pass=Vector2.Distance(p.position,p.target.anchoredPosition)<.01f&&Vector3.Distance(p.scale,p.target.localScale)<.001f&&Mathf.Abs(p.group.alpha-p.alpha)<.001f,detail="Original anchored position, scale and group opacity"});
     foreach(var wh in new[]{new[]{plan.width,plan.height},new[]{Mathf.Max(64,plan.width*4/5),Mathf.Max(64,plan.height*6/5)}}){
      foreach(var p in poses)p.Restore();view.Resize(wh[0],wh[1]);var expected=Geometry(canvas);
      for(int j=0;j<poses.Length;j++)clips[j].SampleAnimation(poses[j].target.gameObject,plan.duration*.35f);view.Refresh();
      for(int j=0;j<poses.Length;j++)clips[j].SampleAnimation(poses[j].target.gameObject,plan.duration);view.Refresh();var actual=Geometry(canvas);float max=expected.Length==actual.Length?expected.Zip(actual,(a,b)=>Mathf.Abs(a-b)).DefaultIfEmpty(0).Max():float.PositiveInfinity;
      result.checks.Add(new Check{name="resize:"+wh[0]+"x"+wh[1],pass=max<.1f,detail="Maximum geometry difference after animation/restore: "+max});view.Png(output+"/rest-"+wh[0]+"x"+wh[1]+".png");
     }
     for(int j=0;j<clips.Length;j++){string path="Assets/MotionMatchGenerated/"+c.caseName+"-"+c.targets[j].binding.id+"-"+Revision+".anim";if(AssetDatabase.LoadAssetAtPath<AnimationClip>(path))AssetDatabase.DeleteAsset(path);AssetDatabase.CreateAsset(clips[j],path);}
     Write(Root+"/evidence/"+Revision+"/"+c.caseName+".json",result);
     if(!result.layoutPreserved||!result.listenerPreserved||!result.referencesPreserved||result.checks.Any(x=>!x.pass))throw new InvalidOperationException("Native preservation check failed: "+c.caseName);
    }finally{foreach(var p in poses)p.Dispose();}
   }
  }
 }
 static void Work(){
  if(Mode=="prepare"){
   var plan=BuildPlan();var metadata=Extract(plan);Write(Root+"/auto-bindings.json",plan);Write(Root+"/destination.json",metadata);
   Write(Root+"/runtime-inventory.json",new SceneInfo{path=request.scene,canvases=Canvases().Select(Describe).ToList()});
   var report=new AutoReport{execution=request.execution,entryScene=request.scene,cases=plan.cases.Count,targets=plan.cases.Sum(c=>c.targets.Count),warnings=plan.warnings};
   report.warnings.Add("Selection is based on visible UI structure, not inferred user intent. Inspect the template captures before matching.");
   report.warnings.Add("Game-specific outcomes (purchases, navigation, login) are not automatically verified. Persistent bindings, object references and raycast destinations are checked without invoking callbacks.");
   if(request.execution=="edit")report.warnings.Add("Serialized scene preview only: runtime-created UI and game startup behavior were not exercised.");
   Write(Root+"/auto-report.json",report);
  }else if(Mode=="capture")Capture(Read<Plan>("auto-bindings.json"));else throw new ArgumentException("Unsupported auto mode: "+Mode);
 }
}
}
