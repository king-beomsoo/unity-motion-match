using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEditor;
using UnityEditor.SceneManagement;
using RGame.RoguelikeKit;
using UnityEngine.Playables;
using UnityEngine.Animations;

namespace UnityMotionMatch {
[Serializable] public class Key {public float t,x,y,scale=1,alpha=1;}
[Serializable] public class Track {public string name;public Key[] keys;}
[Serializable] public class Solution {public string caseName;public Track[] tracks;}
[Serializable] public class Item {public string name;public float x,y,scale,alpha;}
[Serializable] public class Sample {public float t;public List<Item> items=new List<Item>();}
[Serializable] public class MetaItem {public string name,path;public float x,y,w,h,pivotX,pivotY;public bool layoutDriven;}
[Serializable] public class MetaCase {public string caseName;public List<MetaItem> elements=new List<MetaItem>();public int transforms,buttons,graphics,layouts;}
[Serializable] public class Metadata {public int width=960,height=540,fps=30;public List<MetaCase> cases=new List<MetaCase>();}
[Serializable] public class Check {public string name;public bool pass;public string detail;}
[Serializable] public class Result {public string caseName,role,revision;public bool enteredPlayMode,layoutPreserved,listenerPreserved,referencesPreserved;public int transformCount,buttonCount,referenceCount;public List<Sample> samples=new List<Sample>();public List<Check> checks=new List<Check>();}

[InitializeOnLoad] public static class MotionMatchBridge {
 static string Project=>Directory.GetParent(Application.dataPath).FullName;
 static string Root=>Arg("-motionWorkspace", "");
 static string Role=>Path.GetFileName(Root)+"_"+Hash128.Compute(Root).ToString().Substring(0,12);
 static double started;
 static MotionMatchBridge(){if(SessionState.GetBool("real.run",false)){started=EditorApplication.timeSinceStartup;EditorApplication.update+=Tick;}}
 static string Arg(string key,string fallback){var a=Environment.GetCommandLineArgs();int i=Array.IndexOf(a,key);return i>=0?a[i+1]:fallback;}
 public static object Field(object obj,string name)=>obj.GetType().GetField(name,BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Public).GetValue(obj);
 public static void Run(){
  if(string.IsNullOrWhiteSpace(Root)||!Path.IsPathRooted(Root))throw new ArgumentException("An absolute -motionWorkspace is required");
  AssetDatabase.DisallowAutoRefresh();PlayerSettings.companyName="UnityMotionMatch";PlayerSettings.productName="Isolated_"+Role;
  EditorSceneManager.OpenScene("Assets/RGame/RoguelikeKit/Scenes/Menus/MainMenu.unity",OpenSceneMode.Single);
  var go=UnityEngine.Object.FindFirstObjectByType<UIMenuManager>().gameObject;go.name="GameUI";
  var c=Camera.main;c.gameObject.name="Camera";c.orthographic=true;c.orthographicSize=540;c.transform.position=new Vector3(0,0,-100);c.clearFlags=CameraClearFlags.SolidColor;c.backgroundColor=new Color(.03f,.03f,.03f);c.allowHDR=false;c.allowMSAA=false;
  var canvas=go.GetComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=c;canvas.planeDistance=10;
  if(UnityEngine.Object.FindFirstObjectByType<EventSystem>()==null)new GameObject("EventSystem",typeof(EventSystem));
  foreach(var panel in go.GetComponentsInChildren<MonoBehaviour>(true).Where(x=>x is CharacterSelectPanel||x is PowerUpPanel||x is SettingPanel))panel.gameObject.SetActive(true);
  SessionState.SetBool("real.run",true);started=EditorApplication.timeSinceStartup;EditorApplication.update+=Tick;EditorApplication.isPlaying=true;
 }
 static void Tick(){
  if(!EditorApplication.isPlaying||EditorApplication.isCompiling||EditorApplication.isUpdating||Time.timeSinceLevelLoad<2)return;
  if(!SessionState.GetBool("real.run",false))return;SessionState.SetBool("real.run",false);EditorApplication.update-=Tick;
  try{if(Arg("-motionMode","prepare")!="warm")Benchmark();EditorApplication.Exit(0);}catch(Exception e){Debug.LogException(e);EditorApplication.Exit(2);}
 }
 static void CaptureProbe(){
  var root=GameObject.Find("GameUI");var cam=GameObject.Find("Camera").GetComponent<Camera>();var rt=new RenderTexture(960,540,24);rt.Create();cam.targetTexture=rt;Canvas.ForceUpdateCanvases();
  Directory.CreateDirectory(Root+"/evidence/probe");
  string tree=string.Join("\n",root.GetComponentsInChildren<Transform>(true).Select(t=>AnimationUtility.CalculateTransformPath(t,root.transform)+" | "+string.Join(",",t.GetComponents<Component>().Where(x=>x!=null).Select(x=>x.GetType().Name))));File.WriteAllText(Root+"/evidence/probe/hierarchy.txt",tree);
  var panels=new MonoBehaviour[]{root.GetComponentInChildren<CharacterSelectPanel>(true),root.GetComponentInChildren<PowerUpPanel>(true),root.GetComponentInChildren<SettingPanel>(true)};
  for(int i=0;i<panels.Length;i++){
   foreach(Transform child in root.transform)child.gameObject.SetActive(child.gameObject==panels[i].gameObject);
   Canvas.ForceUpdateCanvases();cam.Render();RenderTexture.active=rt;var png=new Texture2D(960,540,TextureFormat.RGB24,false);png.ReadPixels(new Rect(0,0,960,540),0,0);png.Apply();File.WriteAllBytes(Root+"/evidence/probe/"+panels[i].GetType().Name+".png",png.EncodeToPNG());UnityEngine.Object.DestroyImmediate(png);
  }
 }
 static Camera camera;static GameObject ui;static RenderTexture texture;
 static Dictionary<string,RectTransform[]> targets;
 static Dictionary<string,MonoBehaviour> panels;
 static Dictionary<RectTransform,Vector2> positions;
 static Dictionary<RectTransform,Vector3> scales;
 static void Configure(){
  ui=GameObject.Find("GameUI");camera=GameObject.Find("Camera").GetComponent<Camera>();Resize(960,540);
  panels=new Dictionary<string,MonoBehaviour>{{"character",ui.GetComponentInChildren<CharacterSelectPanel>(true)},{"powerup",ui.GetComponentInChildren<PowerUpPanel>(true)},{"settings",ui.GetComponentInChildren<SettingPanel>(true)}};
  targets=new Dictionary<string,RectTransform[]>{{"character",new[]{(RectTransform)panels["character"].transform.Find("CharacterList"),(RectTransform)panels["character"].transform.Find("ButtonBack"),(RectTransform)panels["character"].transform.Find("ButtonConfirm")}},{"powerup",ui.GetComponentsInChildren<PowerUpAttributeUpgrade>(true).Take(3).Select(x=>(RectTransform)x.transform).ToArray()},{"settings",new[]{(RectTransform)panels["settings"].transform.Find("SettingGroup")}}};
  foreach(var t in targets.Values.SelectMany(x=>x)){if(t.GetComponent<CanvasGroup>()==null)t.gameObject.AddComponent<CanvasGroup>();}
  positions=new Dictionary<RectTransform,Vector2>();scales=new Dictionary<RectTransform,Vector3>();
  foreach(var k in panels.Keys){Show(k);foreach(var t in targets[k]){positions[t]=t.anchoredPosition;scales[t]=t.localScale;}}
 }
 static void Resize(int w,int h){if(texture!=null){camera.targetTexture=null;texture.Release();UnityEngine.Object.DestroyImmediate(texture);}texture=new RenderTexture(w,h,24,RenderTextureFormat.ARGB32);texture.antiAliasing=1;texture.Create();camera.targetTexture=texture;Canvas.ForceUpdateCanvases();}
 static void Show(string kind){foreach(Transform child in ui.transform)child.gameObject.SetActive(child.gameObject==panels[kind].gameObject);Canvas.ForceUpdateCanvases();}
 static void Png(string file){camera.Render();RenderTexture.active=texture;var p=new Texture2D(texture.width,texture.height,TextureFormat.RGBA32,false);p.ReadPixels(new Rect(0,0,texture.width,texture.height),0,0);p.Apply();Directory.CreateDirectory(Path.GetDirectoryName(file));File.WriteAllBytes(file,p.EncodeToPNG());UnityEngine.Object.DestroyImmediate(p);RenderTexture.active=null;}
 static string PathOf(Transform t)=>AnimationUtility.CalculateTransformPath(t,ui.transform);
 static string[] Layout(){return ui.GetComponentsInChildren<RectTransform>(true).Select(t=>PathOf(t)+"|"+t.anchorMin.ToString("F4")+t.anchorMax.ToString("F4")+t.pivot.ToString("F4")+t.sizeDelta.ToString("F3")+"|"+t.GetSiblingIndex()).Concat(ui.GetComponentsInChildren<LayoutGroup>(true).Select(g=>PathOf(g.transform)+EditorJsonUtility.ToJson(g))).ToArray();}
 static string[] Listeners(){return ui.GetComponentsInChildren<Button>(true).SelectMany(b=>Enumerable.Range(0,b.onClick.GetPersistentEventCount()).Select(i=>PathOf(b.transform)+"|"+b.onClick.GetPersistentTarget(i)?.GetInstanceID()+"|"+b.onClick.GetPersistentMethodName(i)+"|"+b.onClick.GetPersistentListenerState(i))).ToArray();}
 static string[] References(){var refs=new List<string>();foreach(var m in ui.GetComponentsInChildren<MonoBehaviour>(true).Where(x=>x!=null&&x.GetType().Namespace!=null&&x.GetType().Namespace.StartsWith("RGame"))){var s=new SerializedObject(m);var p=s.GetIterator();while(p.NextVisible(true)){if(p.propertyType==SerializedPropertyType.ObjectReference)refs.Add(PathOf(m.transform)+"|"+m.GetType().Name+"|"+p.propertyPath+"|"+p.objectReferenceInstanceIDValue);}}return refs.ToArray();}
 static void Bind(AnimationClip c,string path,Type type,string prop,Key[] keys,Func<Key,float> value){var curve=new AnimationCurve(keys.Select(k=>new Keyframe(k.t,value(k))).ToArray());for(int i=0;i<keys.Length;i++){AnimationUtility.SetKeyLeftTangentMode(curve,i,AnimationUtility.TangentMode.Linear);AnimationUtility.SetKeyRightTangentMode(curve,i,AnimationUtility.TangentMode.Linear);}AnimationUtility.SetEditorCurve(c,EditorCurveBinding.FloatCurve(path,type,prop),curve);}
 static AnimationClip Clip(Solution s){var clip=new AnimationClip{frameRate=30};float factor=ui.GetComponent<Canvas>().scaleFactor;foreach(var tr in s.tracks){var t=targets[s.caseName].Single(r=>r.name==tr.name);var path=PathOf(t);var pos=positions[t];var scale=scales[t];Bind(clip,path,typeof(RectTransform),"m_AnchoredPosition.x",tr.keys,k=>pos.x+k.x/factor);Bind(clip,path,typeof(RectTransform),"m_AnchoredPosition.y",tr.keys,k=>pos.y-k.y/factor);Bind(clip,path,typeof(Transform),"m_LocalScale.x",tr.keys,k=>scale.x*k.scale);Bind(clip,path,typeof(Transform),"m_LocalScale.y",tr.keys,k=>scale.y*k.scale);Bind(clip,path,typeof(CanvasGroup),"m_Alpha",tr.keys,k=>k.alpha);}return clip;}
 static void Benchmark(){
  Configure();string mode=Arg("-motionMode","capture"),revision=Arg("-motionRevision","r0");var metadata=new Metadata();
  foreach(string kind in panels.Keys){
   Show(kind);string output=mode=="prepare"?Root+"/fixtures/"+kind:Root+"/evidence/"+revision+"/"+kind;Directory.CreateDirectory(output);
   if(mode=="prepare"){
    var panel=panels[kind];var mc=new MetaCase{caseName=kind,transforms=panel.GetComponentsInChildren<Transform>(true).Length,buttons=panel.GetComponentsInChildren<Button>(true).Length,graphics=panel.GetComponentsInChildren<Graphic>(true).Length,layouts=panel.GetComponentsInChildren<LayoutGroup>(true).Length};
    Png(output+"/rest.png");foreach(var t in targets[kind])t.GetComponent<CanvasGroup>().alpha=0;Png(output+"/background.png");foreach(var t in targets[kind])t.GetComponent<CanvasGroup>().alpha=1;
    foreach(var t in targets[kind]){
     Vector3[] corners=new Vector3[4];t.GetWorldCorners(corners);var a=camera.WorldToScreenPoint(corners[0]);var b=camera.WorldToScreenPoint(corners[2]);var pivot=camera.WorldToScreenPoint(t.position);mc.elements.Add(new MetaItem{name=t.name,path=PathOf(t),x=(a.x+b.x)/2,y=540-(a.y+b.y)/2,w=b.x-a.x,h=b.y-a.y,pivotX=pivot.x,pivotY=540-pivot.y,layoutDriven=t.parent.GetComponent<LayoutGroup>()!=null});
     var all=ui.GetComponentsInChildren<Graphic>(true);var enabled=all.Select(x=>x.enabled).ToArray();for(int i=0;i<all.Length;i++)all[i].enabled=enabled[i]&&all[i].transform.IsChildOf(t);var bg=camera.backgroundColor;camera.backgroundColor=Color.clear;Png(output+"/template-"+Array.IndexOf(targets[kind],t)+".png");camera.backgroundColor=bg;for(int i=0;i<all.Length;i++)all[i].enabled=enabled[i];
    }
    metadata.cases.Add(mc);continue;
   }
   var solution=JsonUtility.FromJson<Solution>(File.ReadAllText((Root+"/solutions/"+revision)+"/"+kind+".json"));
   if(solution.tracks.Length!=targets[kind].Length)throw new Exception("Incorrect track count");
   var layout=Layout();var listeners=Listeners();var references=References();
   var result=new Result{caseName=kind,role=Role,revision=revision,enteredPlayMode=Application.isPlaying,transformCount=ui.GetComponentsInChildren<Transform>(true).Length,buttonCount=ui.GetComponentsInChildren<Button>(true).Length,referenceCount=references.Length};
   var clip=Clip(solution);var animator=ui.GetComponent<Animator>();if(animator==null)animator=ui.AddComponent<Animator>();animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;
   var graph=PlayableGraph.Create("MotionMatch");graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);var playable=AnimationClipPlayable.Create(graph,clip);var animOutput=AnimationPlayableOutput.Create(graph,"UI",animator);animOutput.SetSourcePlayable(playable);graph.Play();float factor=ui.GetComponent<Canvas>().scaleFactor;
   for(int i=0;i<=60;i++){float t=i/30f;playable.SetTime(t);graph.Evaluate(0);Canvas.ForceUpdateCanvases();Png(output+"/"+(i+1).ToString("D4")+".png");var sm=new Sample{t=t};foreach(var target in targets[kind])sm.items.Add(new Item{name=target.name,x=(target.anchoredPosition.x-positions[target].x)*factor,y=-(target.anchoredPosition.y-positions[target].y)*factor,scale=target.localScale.x/scales[target].x,alpha=target.GetComponent<CanvasGroup>().alpha});result.samples.Add(sm);}
   graph.Destroy();result.layoutPreserved=layout.SequenceEqual(Layout());result.listenerPreserved=listeners.SequenceEqual(Listeners());result.referencesPreserved=references.SequenceEqual(References());
   foreach(var target in targets[kind])result.checks.Add(new Check{name="rest:"+target.name,pass=Vector2.Distance(target.anchoredPosition,positions[target])<.1f&&Vector3.Distance(target.localScale,scales[target])<.002f&&Mathf.Abs(target.GetComponent<CanvasGroup>().alpha-1)<.002f,detail="Final position/scale/opacity equal original layout"});
   // Compare both resolutions against unanimated original geometry, without changing saved assets.
   foreach(var wh in new[]{new Vector2Int(960,540),new Vector2Int(800,600)}){Resize(wh.x,wh.y);var rects=panels[kind].GetComponentsInChildren<RectTransform>(true);var actual=rects.Select(r=>r.anchoredPosition).ToArray();foreach(var target in targets[kind]){target.anchoredPosition=positions[target];target.localScale=scales[target];target.GetComponent<CanvasGroup>().alpha=1;}LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)panels[kind].transform);Canvas.ForceUpdateCanvases();float max=rects.Select((r,i)=>Vector2.Distance(r.anchoredPosition,actual[i])).DefaultIfEmpty(0).Max();result.checks.Add(new Check{name="resize:"+wh,pass=max<.1f,detail="Maximum anchored-position change on restore/rebuild: "+max});Png(output+"/rest-"+wh.x+"x"+wh.y+".png");}Resize(960,540);
   if(mode!="reference")Interaction(kind,result);
   File.WriteAllText(Path.GetDirectoryName(output)+"/"+kind+".json",JsonUtility.ToJson(result,true));
   if(mode!="reference"){string asset="Assets/MotionMatchGenerated/"+kind+"-"+revision+".anim";AssetDatabase.DeleteAsset(asset);AssetDatabase.CreateAsset(clip,asset);} 
  }
  if(mode=="prepare")File.WriteAllText(Root+"/destination.json",JsonUtility.ToJson(metadata,true));
 }
 static bool Click(GameObject go,bool downOnly=false){
  var r=(RectTransform)go.transform;var point=camera.WorldToScreenPoint(r.TransformPoint(r.rect.center));var data=new PointerEventData(EventSystem.current){position=point,button=PointerEventData.InputButton.Left};var hits=new List<RaycastResult>();EventSystem.current.RaycastAll(data,hits);if(hits.Count==0)return false;var hit=hits[0].gameObject;
  var target=downOnly?ExecuteEvents.GetEventHandler<IPointerDownHandler>(hit):ExecuteEvents.GetEventHandler<IPointerClickHandler>(hit);if(target!=go)return false;
  ExecuteEvents.ExecuteHierarchy(hit,data,ExecuteEvents.pointerDownHandler);if(!downOnly){ExecuteEvents.ExecuteHierarchy(hit,data,ExecuteEvents.pointerUpHandler);ExecuteEvents.ExecuteHierarchy(hit,data,ExecuteEvents.pointerClickHandler);}return true;
 }
 static void Interaction(string kind,Result r){
  Show(kind);var manager=ui.GetComponent<UIMenuManager>();
  if(kind=="character"){
   var item=panels[kind].GetComponentInChildren<CharacterItem>();var so=(LevelAndCharacterSO)Field(item,"mLevelAndCharacterSO");so.SelectCharacterSo=null;bool clicked=Click(item.gameObject);r.checks.Add(new Check{name="character_selection",pass=clicked&&item.IsSelected&&so.SelectCharacterSo!=null,detail="GraphicRaycaster -> pointer handlers -> existing CharacterItem button listener -> selected character SO"});
   // Use original menu route to attach original navigation callbacks before clicking Confirm.
   manager.GetType().GetMethod("OpenCharacterSelectScreen",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(manager,null);var button=panels[kind].GetComponentsInChildren<Button>().First(x=>Enumerable.Range(0,x.onClick.GetPersistentEventCount()).Any(i=>x.onClick.GetPersistentMethodName(i)=="ConfirmButton"));bool nav=Click(button.gameObject);r.checks.Add(new Check{name="confirm_navigation",pass=nav&&ui.GetComponentInChildren<LevelSelectPanel>(true).gameObject.activeSelf,detail="Existing ConfirmButton -> UIMenuManager -> level selection activated"});
  }else if(kind=="powerup"){
   var upgrade=targets[kind][0].GetComponent<PowerUpAttributeUpgrade>();var attr=(AttributeSO)Field(upgrade,"mAttributeSo");string key=(string)Field(upgrade,"mAttributeName");int gold=attr.GoldCount,level=attr.GetAttribute(key);bool selected=Click(upgrade.gameObject,true);var button=panels[kind].GetComponentsInChildren<Button>().First(x=>Enumerable.Range(0,x.onClick.GetPersistentEventCount()).Any(i=>x.onClick.GetPersistentMethodName(i)=="OnBuyButtonOnclick"));bool buy=Click(button.gameObject);r.checks.Add(new Check{name="purchase_changes_game_state",pass=selected&&buy&&attr.GoldCount<gold&&attr.GetAttribute(key)==level+1,detail="Gold "+gold+" -> "+attr.GoldCount+"; "+key+" "+level+" -> "+attr.GetAttribute(key)});
  }else{
   var settings=panels[kind];var so=(SettingsSO)Field(settings,"_currentSettings");var item=(SettingItem)Field(settings,"masterVolumeItem");float old=so.MasterVolume;var button=(Button)Field(item,old>.9f?"leftButton":"rightButton");bool click=Click(button.gameObject);r.checks.Add(new Check{name="settings_runtime_listener",pass=click&&Mathf.Abs(so.MasterVolume-old)>.01f,detail="Master volume "+old+" -> "+so.MasterVolume+" through SettingItem runtime AddListener"});
  }
 }
}

}
