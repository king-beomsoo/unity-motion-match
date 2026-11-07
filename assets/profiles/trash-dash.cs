using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.AddressableAssets;

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
[Serializable] public class Metadata {public int width=540,height=960,fps=30;public List<MetaCase> cases=new List<MetaCase>();}
[Serializable] public class Check {public string name;public bool pass;public string detail;}
[Serializable] public class Result {public string caseName,role,revision;public bool enteredPlayMode,layoutPreserved,listenerPreserved,referencesPreserved;public int transformCount,buttonCount,referenceCount;public List<Sample> samples=new List<Sample>();public List<Check> checks=new List<Check>();}


[InitializeOnLoad] public static class MotionMatchBridge {
 static string Project=>Directory.GetParent(Application.dataPath).FullName;
 static string Root=>Arg("-motionWorkspace", "");
 static string Role=>Path.GetFileName(Root)+"_"+Hash128.Compute(Root).ToString().Substring(0,12);
 static string Mode=>Arg("-motionMode","capture");static string Revision=>Arg("-motionRevision","r0");
 static int stage=0;static double next;static bool configured;
 static MotionMatchBridge(){if(SessionState.GetBool("transfer.run",false))EditorApplication.update+=Tick;}
 static string Arg(string name,string fallback){var a=Environment.GetCommandLineArgs();int i=Array.IndexOf(a,name);return i<0?fallback:a[i+1];}
 static Camera camera;static GameObject ui;static RenderTexture texture;static ShopUI shop;
 static Dictionary<string,RectTransform[]> targets;
 static Dictionary<string,ShopList> panels;
 static Dictionary<RectTransform,Vector2> positions=new Dictionary<RectTransform,Vector2>();
 static Dictionary<RectTransform,Vector3> scales=new Dictionary<RectTransform,Vector3>();
 static Metadata metadata=new Metadata();static Dictionary<string,Result> results=new Dictionary<string,Result>();
 static string[] cases={"items","characters","themes"};
 static string Out(string k)=>Mode=="prepare"?Root+"/fixtures/"+k:Root+"/evidence/"+Revision+"/"+k;
 static void Finish(){SessionState.SetBool("transfer.run",false);EditorApplication.update-=Tick;EditorApplication.Exit(0);}
 static void Tick(){
  if(!SessionState.GetBool("transfer.run",false)||!EditorApplication.isPlaying||EditorApplication.isCompiling||EditorApplication.isUpdating||Time.timeSinceLevelLoad<3||EditorApplication.timeSinceStartup<next)return;
  try{
   if(PlayerData.instance==null){Debug.Log("MotionMatchBridge: restoring original ShopUI.Start after editor assembly reload");Seed();typeof(ShopUI).GetMethod("Start",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(UnityEngine.Object.FindFirstObjectByType<ShopUI>(),null);next=EditorApplication.timeSinceStartup+1;return;}
   if(!CharacterDatabase.loaded||!ThemeDatabase.loaded){if(Time.timeSinceLevelLoad>60)throw new Exception("Database load timeout");return;}
   if(Mode=="warm"){Finish();return;}
   if(!configured){Configure();configured=true;}
   if(stage<3){var k=cases[stage];if(panels[k].GetComponentsInChildren<ShopItemListItem>().Length==0)return;Benchmark(k);stage++;if(stage<3){Show(cases[stage]);next=EditorApplication.timeSinceStartup+1;return;}
    if(Mode=="prepare"){File.WriteAllText(Root+"/destination.json",JsonUtility.ToJson(metadata,true));Finish();return;}
    Show("items");next=EditorApplication.timeSinceStartup+1;return;
   }
   if(stage<6){string k=cases[stage-3];Interaction(k,results[k]);Save(k,results[k]);stage++;if(stage<6){Show(cases[stage-3]);next=EditorApplication.timeSinceStartup+1;return;}Finish();}
  }catch(Exception e){Debug.LogException(e);SessionState.SetBool("transfer.run",false);EditorApplication.Exit(2);}
 }
 static void Configure(){
  shop=UnityEngine.Object.FindFirstObjectByType<ShopUI>();ui=shop.gameObject;camera=GameObject.Find("TransferCaptureCamera").GetComponent<Camera>();Resize(540,960);
  panels=new Dictionary<string,ShopList>{{"items",shop.itemList},{"characters",shop.characterList},{"themes",shop.themeList}};
  Func<string,RectTransform> get=p=>(RectTransform)ui.transform.Find("Background/"+p);
  targets=new Dictionary<string,RectTransform[]>{{"items",new[]{get("ItemsList"),get("Coin"),get("Premium")}},{"characters",new[]{get("CharacterList"),get("TabsSwitch"),get("StoreTitle")}},{"themes",new[]{get("ThemeList")}}};
  foreach(var t in targets.Values.SelectMany(x=>x)){if(t.GetComponent<CanvasGroup>()==null)t.gameObject.AddComponent<CanvasGroup>();}
 }
 static void Show(string k){if(k=="items")shop.OpenItemList();else if(k=="characters")shop.OpenCharacterList();else shop.OpenThemeList();Canvas.ForceUpdateCanvases();}
 static void Resize(int w,int h){if(texture!=null){camera.targetTexture=null;texture.Release();UnityEngine.Object.DestroyImmediate(texture);}texture=new RenderTexture(w,h,24,RenderTextureFormat.ARGB32);texture.antiAliasing=1;texture.Create();camera.targetTexture=texture;Canvas.ForceUpdateCanvases();}
 static void Png(string file){camera.Render();RenderTexture.active=texture;var p=new Texture2D(texture.width,texture.height,TextureFormat.RGBA32,false);p.ReadPixels(new Rect(0,0,texture.width,texture.height),0,0);p.Apply();Directory.CreateDirectory(Path.GetDirectoryName(file));File.WriteAllBytes(file,p.EncodeToPNG());UnityEngine.Object.DestroyImmediate(p);RenderTexture.active=null;}
 static string PathOf(Transform t)=>AnimationUtility.CalculateTransformPath(t,ui.transform);
 static string[] Layout(){return ui.GetComponentsInChildren<RectTransform>(true).Select(t=>PathOf(t)+"|"+t.anchorMin.ToString("F4")+t.anchorMax.ToString("F4")+t.pivot.ToString("F4")+t.sizeDelta.ToString("F3")+"|"+t.GetSiblingIndex()).Concat(ui.GetComponentsInChildren<LayoutGroup>(true).Select(g=>PathOf(g.transform)+EditorJsonUtility.ToJson(g))).ToArray();}
 static string[] Listeners(){return ui.GetComponentsInChildren<Button>(true).SelectMany(b=>Enumerable.Range(0,b.onClick.GetPersistentEventCount()).Select(i=>PathOf(b.transform)+"|"+b.onClick.GetPersistentTarget(i)?.GetInstanceID()+"|"+b.onClick.GetPersistentMethodName(i)+"|"+b.onClick.GetPersistentListenerState(i))).ToArray();}
 static string[] References(){var refs=new List<string>();foreach(var m in ui.GetComponentsInChildren<MonoBehaviour>(true).Where(x=>x!=null&&x.GetType().Assembly.GetName().Name=="Assembly-CSharp")){var s=new SerializedObject(m);var p=s.GetIterator();while(p.NextVisible(true))if(p.propertyType==SerializedPropertyType.ObjectReference)refs.Add(PathOf(m.transform)+"|"+m.GetType().Name+"|"+p.propertyPath+"|"+p.objectReferenceInstanceIDValue);}return refs.ToArray();}
 [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]static void Seed(){
  if(!SessionState.GetBool("transfer.run",false))return;
  UnityEngine.Random.InitState(7203);
  var p=new PlayerData();typeof(PlayerData).GetField("m_Instance",BindingFlags.Static|BindingFlags.NonPublic).SetValue(null,p);
  Directory.CreateDirectory(Application.persistentDataPath);typeof(PlayerData).GetField("saveFile",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(p,Application.persistentDataPath+"/save.bin");
  p.coins=75000;p.premium=100;p.characters.Add("Trash Cat");p.themes.Add("Day");p.Save();
  UnityEngine.Analytics.Analytics.enabled=false;
 }
 public static void Run(){
  if(string.IsNullOrWhiteSpace(Root)||!Path.IsPathRooted(Root))throw new ArgumentException("An absolute -motionWorkspace is required");
  AssetDatabase.DisallowAutoRefresh();
  PlayerSettings.companyName="UnityMotionMatch";PlayerSettings.productName="Isolated_"+Role;
  GraphicsSettings.defaultRenderPipeline=null;QualitySettings.renderPipeline=null;
  AddressableAssetSettingsDefaultObject.Settings.ActivePlayModeDataBuilderIndex=0;
  EditorSceneManager.OpenScene("Assets/Scenes/Shop.unity",OpenSceneMode.Single);
  var shop=UnityEngine.Object.FindFirstObjectByType<ShopUI>();var canvas=shop.GetComponentInParent<Canvas>();if(canvas==null)canvas=UnityEngine.Object.FindFirstObjectByType<Canvas>();
  var camera=new GameObject("TransferCaptureCamera",typeof(Camera)).GetComponent<Camera>();camera.orthographic=true;camera.orthographicSize=540;camera.transform.position=new Vector3(0,0,-100);camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.12f,.15f,.2f);camera.allowHDR=false;camera.allowMSAA=false;
  canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=camera;canvas.planeDistance=10;
  foreach(var m in UnityEngine.Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include,FindObjectsSortMode.None)){if(m!=null&&(m.GetType().Name=="IAPButton"||m.GetType().Name=="IAPListener"||m.GetType().Name=="IAPHandler"))m.enabled=false;}
  SessionState.SetBool("transfer.run",true);EditorApplication.update+=Tick;EditorApplication.isPlaying=true;
 }
 static void Bind(AnimationClip c,string path,Type type,string prop,Key[] keys,Func<Key,float> value){var curve=new AnimationCurve(keys.Select(k=>new Keyframe(k.t,value(k))).ToArray());for(int i=0;i<keys.Length;i++){AnimationUtility.SetKeyLeftTangentMode(curve,i,AnimationUtility.TangentMode.Linear);AnimationUtility.SetKeyRightTangentMode(curve,i,AnimationUtility.TangentMode.Linear);}AnimationUtility.SetEditorCurve(c,EditorCurveBinding.FloatCurve(path,type,prop),curve);}
 static AnimationClip Clip(Solution s){var clip=new AnimationClip{frameRate=30};float factor=ui.GetComponent<Canvas>().scaleFactor;foreach(var tr in s.tracks){var t=targets[s.caseName].Single(r=>r.name==tr.name);var path=PathOf(t);var pos=positions[t];var scale=scales[t];Bind(clip,path,typeof(RectTransform),"m_AnchoredPosition.x",tr.keys,k=>pos.x+k.x/factor);Bind(clip,path,typeof(RectTransform),"m_AnchoredPosition.y",tr.keys,k=>pos.y-k.y/factor);Bind(clip,path,typeof(Transform),"m_LocalScale.x",tr.keys,k=>scale.x*k.scale);Bind(clip,path,typeof(Transform),"m_LocalScale.y",tr.keys,k=>scale.y*k.scale);Bind(clip,path,typeof(CanvasGroup),"m_Alpha",tr.keys,k=>k.alpha);}return clip;}
 static void Benchmark(string kind){
   string mode=Mode,revision=Revision,output=Out(kind);Directory.CreateDirectory(output);Canvas.ForceUpdateCanvases();foreach(var t in targets[kind]){positions[t]=t.anchoredPosition;scales[t]=t.localScale;}
   if(mode=="prepare"){
    var panel=panels[kind];var mc=new MetaCase{caseName=kind,transforms=panel.GetComponentsInChildren<Transform>(true).Length,buttons=panel.GetComponentsInChildren<Button>(true).Length,graphics=panel.GetComponentsInChildren<Graphic>(true).Length,layouts=panel.GetComponentsInChildren<LayoutGroup>(true).Length};
    Png(output+"/rest.png");foreach(var t in targets[kind])t.GetComponent<CanvasGroup>().alpha=0;Png(output+"/background.png");foreach(var t in targets[kind])t.GetComponent<CanvasGroup>().alpha=1;
    foreach(var t in targets[kind]){
     Vector3[] corners=new Vector3[4];t.GetWorldCorners(corners);var a=camera.WorldToScreenPoint(corners[0]);var b=camera.WorldToScreenPoint(corners[2]);var pivot=camera.WorldToScreenPoint(t.position);mc.elements.Add(new MetaItem{name=t.name,path=PathOf(t),x=(a.x+b.x)/2,y=960-(a.y+b.y)/2,w=b.x-a.x,h=b.y-a.y,pivotX=pivot.x,pivotY=960-pivot.y,layoutDriven=t.parent.GetComponent<LayoutGroup>()!=null});
     var all=ui.GetComponentsInChildren<Graphic>(true);var enabled=all.Select(x=>x.enabled).ToArray();for(int i=0;i<all.Length;i++)all[i].enabled=enabled[i]&&all[i].transform.IsChildOf(t);var bg=camera.backgroundColor;camera.backgroundColor=Color.clear;Png(output+"/template-"+Array.IndexOf(targets[kind],t)+".png");camera.backgroundColor=bg;for(int i=0;i<all.Length;i++)all[i].enabled=enabled[i];
    }
    metadata.cases.Add(mc);return;
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
   foreach(var wh in new[]{new Vector2Int(540,960),new Vector2Int(600,800)}){Resize(wh.x,wh.y);var rects=panels[kind].GetComponentsInChildren<RectTransform>(true);var actual=rects.Select(r=>r.anchoredPosition).ToArray();foreach(var target in targets[kind]){target.anchoredPosition=positions[target];target.localScale=scales[target];target.GetComponent<CanvasGroup>().alpha=1;}LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)panels[kind].transform);Canvas.ForceUpdateCanvases();float max=rects.Select((r,i)=>Vector2.Distance(r.anchoredPosition,actual[i])).DefaultIfEmpty(0).Max();result.checks.Add(new Check{name="resize:"+wh,pass=max<.1f,detail="Maximum anchored-position change on restore/rebuild: "+max});Png(output+"/rest-"+wh.x+"x"+wh.y+".png");}Resize(540,960);
   results[kind]=result;
   File.WriteAllText(Path.GetDirectoryName(output)+"/"+kind+".json",JsonUtility.ToJson(result,true));
   if(mode!="reference"){string asset="Assets/MotionMatchGenerated/"+kind+"-"+revision+".anim";AssetDatabase.DeleteAsset(asset);AssetDatabase.CreateAsset(clip,asset);} 
 }
 static bool Click(GameObject go,bool downOnly=false){
  var r=(RectTransform)go.transform;var point=camera.WorldToScreenPoint(r.TransformPoint(r.rect.center));var data=new PointerEventData(EventSystem.current){position=point,button=PointerEventData.InputButton.Left};var hits=new List<RaycastResult>();EventSystem.current.RaycastAll(data,hits);if(hits.Count==0)return false;var hit=hits[0].gameObject;
  var target=downOnly?ExecuteEvents.GetEventHandler<IPointerDownHandler>(hit):ExecuteEvents.GetEventHandler<IPointerClickHandler>(hit);if(target!=go)return false;
  ExecuteEvents.ExecuteHierarchy(hit,data,ExecuteEvents.pointerDownHandler);if(!downOnly){ExecuteEvents.ExecuteHierarchy(hit,data,ExecuteEvents.pointerUpHandler);ExecuteEvents.ExecuteHierarchy(hit,data,ExecuteEvents.pointerClickHandler);}return true;
 }

 static void Save(string k,Result r){File.WriteAllText(Path.GetDirectoryName(Out(k))+"/"+k+".json",JsonUtility.ToJson(r,true));}
 static void Interaction(string kind,Result r){
  Canvas.ForceUpdateCanvases();var p=PlayerData.instance;var entries=panels[kind].GetComponentsInChildren<ShopItemListItem>();
  if(kind=="items"){
   var b=entries.First(x=>x.buyButton.interactable).buyButton;int coins=p.coins,count=p.consumables.Values.Sum();bool hit=Click(b.gameObject);
   r.checks.Add(new Check{name="original_item_purchase",pass=hit&&p.coins<coins&&p.consumables.Values.Sum()==count+1,detail="GraphicRaycaster -> original runtime AddListener; coins "+coins+" -> "+p.coins+"; inventory "+count+" -> "+p.consumables.Values.Sum()});
  }else if(kind=="characters"){
   var b=entries.First(x=>x.buyButton.interactable).buyButton;int coins=p.coins,count=p.characters.Count;bool hit=Click(b.gameObject);
   r.checks.Add(new Check{name="original_character_purchase",pass=hit&&p.coins<coins&&p.characters.Count==count+1,detail="GraphicRaycaster -> original runtime AddListener; coins "+coins+" -> "+p.coins+"; characters "+count+" -> "+p.characters.Count});
  }else{
   var b=entries.First(x=>x.buyButton.interactable).buyButton;int coins=p.coins,count=p.themes.Count;bool hit=Click(b.gameObject);
   r.checks.Add(new Check{name="original_theme_purchase",pass=hit&&p.coins<coins&&p.themes.Count==count+1,detail="GraphicRaycaster -> original runtime AddListener; coins "+coins+" -> "+p.coins+"; themes "+count+" -> "+p.themes.Count});
  }
  var tab=ui.transform.Find("Background/TabsSwitch/"+(kind=="themes"?"Item":kind=="items"?"Character":"Themes"));bool clicked=Click(tab.gameObject);var nextPanel=kind=="themes"?shop.itemList:kind=="items"?(ShopList)shop.characterList:shop.themeList;
  r.checks.Add(new Check{name="original_tab_navigation",pass=clicked&&nextPanel.gameObject.activeSelf&&!panels[kind].gameObject.activeSelf,detail="Raycast click -> persistent original ShopUI.Open*List listener"});
 }
}

}
