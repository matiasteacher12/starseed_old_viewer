#if UNITY_EDITOR || VIEWER_BACKEND
using System.Collections;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

// Opt-in batch-mode renderer. Saves an offscreen camera target, never the desktop.
public sealed class StoryMenuRenderCheck : MonoBehaviour {
    public static void Attach(GameObject host,Transform stage){
        if(System.Environment.GetCommandLineArgs().Any(a=>a=="-menu-render-probe"||a=="-expansion-menu-check"))
            host.AddComponent<StoryMenuRenderCheck>().StartCoroutine(Render(stage));
    }
    static IEnumerator Render(Transform stage) {
        yield return new WaitForSecondsRealtime(1);
        var camera=new GameObject("Offscreen menu camera",typeof(Camera)).GetComponent<Camera>();
        camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.black;
        camera.orthographic=true;camera.orthographicSize=360;camera.transform.position=new Vector3(0,0,-10);
        var target=new RenderTexture(1120,630,24);target.Create();camera.targetTexture=target;
        var canvas=stage.GetComponentInParent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=camera;canvas.planeDistance=1;
        var root=stage.Find("Root_Story");var bg=root.Find("Center/Bg");
        var directory=Path.GetFullPath(Path.Combine(Application.dataPath,"../../RenderChecks"));Directory.CreateDirectory(directory);
        yield return null;
        Save(camera,target,Path.Combine(directory,"runtime-home.png"));
        var menu=FindObjectOfType<OriginalStoryMenu>();
        if(menu.Catalogue.chapters.Length!=7 || menu.Catalogue.chapters.Sum(c=>c.episodes.Length)!=94)throw new System.Exception("Old menu catalogue mismatch");
        if(System.Environment.GetCommandLineArgs().Contains("-expansion-menu-check")){
            int[] expected={52,14,207,398};string[] kinds={"faction","archive","event","character"};
            if(menu.AllChapters.SelectMany(c=>c.episodes).Any(e=>e.id.StartsWith("Date_")))throw new System.Exception("Deferred dates entered catalogue");
            for(int k=0;k<kinds.Length;k++){
                string kind=kinds[k];var chapters=menu.AllChapters.Where(c=>c.category==kind).ToArray();
                if(chapters.Sum(c=>c.episodes.Length)!=expected[k])throw new System.Exception("Expansion category count "+kind);
                if(kind=="character")menu.ShowCharacters();else menu.ShowCategory(kind);
                yield return new WaitForSecondsRealtime(.5f);Save(camera,target,Path.Combine(directory,"category-"+kind+".png"));
                if(kind=="character"){
                    var charContent=(RectTransform)stage.Find("Root_Story_Char/Contents/Anchor_Center/CharList/ScrollView/Viewport/Content");var charScroll=charContent.parent.parent.GetComponent<ScrollRect>();
                    var grid=charContent.GetChild(0).Find("Grid");var rows=grid.Cast<Transform>().Where(t=>t.gameObject.activeSelf).ToArray();
                    if(rows.Length!=75||charContent.rect.height<=charScroll.viewport.rect.height)throw new System.Exception("Character grid extent mismatch");
                    charScroll.verticalNormalizedPosition=0;yield return null;Canvas.ForceUpdateCanvases();
                    var bounds=RectTransformUtility.CalculateRelativeRectTransformBounds(charScroll.viewport,rows.Last());
                    if(bounds.center.y<charScroll.viewport.rect.yMin-1||bounds.center.y>charScroll.viewport.rect.yMax+1)throw new System.Exception("Last character row is unreachable");
                    Save(camera,target,Path.Combine(directory,"category-character-bottom.png"));Debug.Log("EXPANSION_CHARACTER_SCROLL_OK entries=75");
                }
                foreach(var item in chapters){
                    menu.ShowChapter(item);yield return null;
                    var rows=stage.Find("Root_StoryChapter/Episode/Center/Right/Info/ScrollView/Viewport/Content");
                    if(rows.Cast<Transform>().Count(t=>t.gameObject.activeSelf)!=item.episodes.Length)throw new System.Exception("Expansion list mismatch "+item.id);
                    if(item==chapters[0]){yield return new WaitForSecondsRealtime(.5f);Save(camera,target,Path.Combine(directory,"episodes-"+kind+".png"));}
                    menu.Back();yield return null;
                }
                Debug.Log("EXPANSION_MENU_CATEGORY_OK "+kind+" chapters="+chapters.Length+" episodes="+expected[k]);
            }
            Debug.Log("EXPANSION_MENU_OK added=671 main=94 dates=0");Application.Quit(0);yield break;
        }
        root.Find("Center/Story/Btn_MainStory").GetComponent<Button>().onClick.Invoke();yield return new WaitForSecondsRealtime(1);
        Save(camera,target,Path.Combine(directory,"runtime-chapters.png"));
        foreach(var item in menu.Catalogue.chapters) {
            stage.Find("Root_StoryCategory/Contents/Anchor_Center/Layout/ScrollView/Viewport/Content/"+item.id+"/Story").GetComponent<Button>().onClick.Invoke();
            yield return null;
            var list=stage.Find("Root_StoryChapter/Episode/Center/Right/Info/ScrollView/Viewport/Content");
            if(list.Cast<Transform>().Count(t=>t.gameObject.activeSelf)!=item.episodes.Length)throw new System.Exception("Chapter entry count mismatch "+item.id);
            stage.Find("Root_StoryChapter/Hud/Hud/Left/Btn_Back").GetComponent<Button>().onClick.Invoke();
        }
        stage.Find("Root_StoryCategory/Contents/Anchor_Center/Layout/ScrollView/Viewport/Content/"+menu.Catalogue.chapters[0].id+"/Story").GetComponent<Button>().onClick.Invoke();yield return new WaitForSecondsRealtime(1);
        var content=(RectTransform)stage.Find("Root_StoryChapter/Episode/Center/Right/Info/ScrollView/Viewport/Content");
        var scroll=content.parent.parent.GetComponent<ScrollRect>();scroll.verticalNormalizedPosition=1;
        Canvas.ForceUpdateCanvases();
        foreach(Transform row in content)if(row.gameObject.activeSelf) {
            var rect=(RectTransform)row;
            float center=content.InverseTransformPoint(rect.TransformPoint(rect.rect.center)).x;
            if(Mathf.Abs(center-content.rect.center.x)>.01f)throw new System.Exception("Episode row is not centered");
        }
        var chapter=stage.Find("Root_StoryChapter");
        var prologue=chapter.Find("Episode/Center/Left/Main/Story/Bottom/Layout/Prologue/Text").GetComponent<MText>();
        var deco=content.GetChild(1).Find("Contents/Center/Bg/Text_Deco").GetComponent<MText>();
        if(prologue.spacing!=10 || deco.spacing!=30 || prologue.fontSize!=18 || deco.fontSize!=10)
            throw new System.Exception("Original menu typography settings lost");
        Save(camera,target,Path.Combine(directory,"runtime-episodes.png"));
        Debug.Log("MENU_LAYOUT_OK centeredRows=true originalSpacing=true originalFontSizes=true viewportWidth="+content.rect.width);
        var plan=StoryScene.LoadOldCheckPlan();
        var selected=plan!=null?menu.Catalogue.chapters.SelectMany(c=>c.episodes).First(e=>e.scene==plan.scene):menu.Catalogue.chapters[0].episodes[0];
        menu.ShowChapter(menu.Catalogue.chapters.Single(c=>c.episodes.Contains(selected)));
        content.Find(selected.id+"/Contents/Center/Btn_Ok").GetComponent<Button>().onClick.Invoke();
        if(menu.Visible)throw new System.Exception("Playable episode did not launch");
        Debug.Log("MENU_OFFSCREEN_OK "+directory);
        yield return FindObjectOfType<StoryScene>().CheckOldEpisode(true);
    }
    static void Save(Camera camera,RenderTexture target,string path) {
        Canvas.ForceUpdateCanvases();camera.Render();RenderTexture.active=target;
        var texture=new Texture2D(target.width,target.height,TextureFormat.RGBA32,false);
        texture.ReadPixels(new Rect(0,0,target.width,target.height),0,0);texture.Apply();File.WriteAllBytes(path,texture.EncodeToPNG());
        RenderTexture.active=null;Destroy(texture);
    }
}

#endif
