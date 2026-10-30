#if UNITY_EDITOR || VIEWER_BACKEND
using System;
using System.Collections.Generic;
using System.Linq;
using Coffee.UIExtensions;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

// Visuals and timings are preserved assets. These bindings replace the game's
// account service / UILoop with a read-only, table-generated local catalogue.
public sealed class OriginalStoryMenu : MonoBehaviour {
    [Serializable] public class Data {public Page[] pages;public Chapter[] chapters;public Chapter[] factions,archives,events,characters;}
    [Serializable] public class Page {public string name,asset;public Tween[] tweens;}
    [Serializable] public class Chapter {public string id,number,title,description,image,category,portrait,characterId;public int order;public bool prologue;public Episode[] episodes;}
    [Serializable] public class Episode {public string id,title,scene,summary,clearCondition;public int order;public bool playable;}
    [Serializable] public class Tween {public string path;public int kind,ease,loops,loopType;public float duration,delay,value;public bool relative,fromValue;public Vector3 vector;}
    public bool Visible => current!=null && current.activeSelf;
    public Action<Episode> PlayEpisode;
    public Action Shown;
    public Action ReturnToViewer;
    Action externalReturn;
    public Data Catalogue {get;private set;}
    readonly Dictionary<string,GameObject> pages=new Dictionary<string,GameObject>();
    readonly Dictionary<string,Sprite> images=new Dictionary<string,Sprite>();
    GameObject current; Chapter selected; Episode playing;
    RectTransform cards,entries; GameObject cardTemplate,entryTemplate;
    Text notice;
    TMP_FontAsset koreanTmp;
    bool restoringScroll;
    string category="main";
    string builtCategory;
    RectTransform characterContent;
    public IEnumerable<Chapter> AllChapters=>Catalogue.chapters.Concat(Catalogue.factions??Array.Empty<Chapter>()).Concat(Catalogue.archives??Array.Empty<Chapter>()).Concat(Catalogue.events??Array.Empty<Chapter>()).Concat(Catalogue.characters??Array.Empty<Chapter>());
    string CategoryScrollKey=>category=="main"?"story.menu.chapters":"story.menu."+category;
    readonly HashSet<string> batchRead=new HashSet<string>();
    [Serializable] class BatchProgress {public string[] read;}
    string batchProgressPath;
    public bool IsRead(Episode ep)=>Application.isBatchMode?batchRead.Contains(ep.id):PlayerPrefs.GetInt("story.read."+ep.id,0)==1;
    public int ReadCount(Chapter chapter)=>chapter.episodes.Count(IsRead);
    const string CardPath="Contents/Anchor_Center/Layout/ScrollView/Viewport/Content";
    const string EntryPath="Episode/Center/Right/Info/ScrollView/Viewport/Content";

    public void Initialize(Transform stage,AssetBundle bundle,TMP_FontAsset textFont) {
        Catalogue=BackendJson.FromJson<Data>(StoryContent.Load<TextAsset>("story-menu").text);
        // Automated checks use an isolated store, never the user's completion keys.
        var args=Environment.GetCommandLineArgs();int progressArg=Array.IndexOf(args,"-progress-check-store");
        if(Application.isBatchMode&&progressArg>=0) {
            batchProgressPath=System.IO.Path.GetFullPath(args[progressArg+1]);
            if(System.IO.File.Exists(batchProgressPath))foreach(var id in BackendJson.FromJson<BatchProgress>(System.IO.File.ReadAllText(batchProgressPath)).read)batchRead.Add(id);
        }
        koreanTmp=textFont;
        var holder=new GameObject("Menu preparation");holder.SetActive(false);
        foreach(var page in Catalogue.pages) {
            Debug.Log("STORY_MENU_PAGE_BEGIN "+page.name+" tweens="+(page.tweens==null?-1:page.tweens.Length));
            var prefab=bundle.LoadAsset<GameObject>(page.asset);
            if(!prefab)prefab=AssetBundle.GetAllLoadedAssetBundles().Select(b=>b.LoadAsset<GameObject>(page.asset)).FirstOrDefault(p=>p);
            if(!prefab)throw new InvalidOperationException("Missing menu prefab "+page.asset);
            var go=Instantiate(prefab,holder.transform,false);go.name=page.name;
            if(!go.transform.Find("Hud")&&pages.ContainsKey("Root_Story")){var hud=Instantiate(pages["Root_Story"].transform.Find("Hud").gameObject,go.transform,false);hud.name="Hud";}
            foreach(var button in go.GetComponentsInChildren<Button>(true)) {button.onClick=new Button.ButtonClickedEvent();button.navigation=new Navigation{mode=Navigation.Mode.None};}
            foreach(var text in go.GetComponentsInChildren<Text>(true))if(!text.font||!text.font.name.Contains("Number"))text.font=StoryContent.Load<Font>("Original/MFont_ko");
            foreach(var text in go.GetComponentsInChildren<TMP_Text>(true))text.font=koreanTmp;
            foreach(var canvas in go.GetComponentsInChildren<Canvas>(true))canvas.overrideSorting=false;
            foreach(var a in go.GetComponentsInChildren<Animation>(true)){a.playAutomatically=false;a.Stop();}
            foreach(var effect in go.GetComponentsInChildren<UIParticle>(true)) {
                effect.raycastTarget=false;
                effect.particles.RemoveAll(ps=>!ps||!ps.GetComponent<ParticleSystemRenderer>()||ps.GetComponent<ParticleSystemRenderer>().sharedMaterial==null);
            }
            foreach(var graphic in go.GetComponentsInChildren<Graphic>(true))if(graphic is Text || graphic is TMP_Text)graphic.raycastTarget=false;
            go.SetActive(false);go.transform.SetParent(stage,false);
            var rt=(RectTransform)go.transform;rt.anchorMin=Vector2.zero;rt.anchorMax=Vector2.one;rt.offsetMin=rt.offsetMax=Vector2.zero;rt.localScale=Vector3.one;
            SetActive(go,"Hud/Hud/Money",false);SetActive(go,"Hud/Hud/Favor",false);SetActive(go,"Hud/Hud/AlarmTooltipRoot",false);SetActive(go,"Hud/Hud/Left/Btn_Guide",false);
            Bind(go,"Hud/Hud/Left/Btn_Back",Back);Bind(go,"Hud/Hud/Left/Btn_Lobby",()=>{if(ReturnToViewer!=null)ReturnToViewer();else ShowHome();});
            foreach(var tween in page.tweens??new Tween[0]) {
                var target=go.transform.Find(tween.path);
                if(!target)throw new InvalidOperationException("Missing tween target "+tween.path);
                target.gameObject.AddComponent<OriginalMenuTween>().Configure(tween);
            }
            pages.Add(page.name,go);
            Debug.Log("STORY_MENU_PAGE_READY "+page.name);
        }
        Destroy(holder);
        var home=pages["Root_Story"];
        var background=ImageForResource("Original/Menu/bg_story_01");
        SetImage(home.transform,"Center/Bg/Bg",background);
        SetImage(home.transform,"Center/Bg/Bg/Fx",background);
        foreach(var name in new[]{"Btn_MainStory","Btn_Proxyan","Btn_Archive","Btn_Eventstory","Btn_FactionStory"}) {
            string path="Center/Story/"+name;
            SetActive(home,path+"/LockInfo",false);SetActive(home,path+"/New",false);
            string kind=name=="Btn_MainStory"?"main":name=="Btn_FactionStory"?"faction":name=="Btn_Archive"?"archive":name=="Btn_Eventstory"?"event":"character";
            Bind(home,path,()=>{if(kind=="character")ShowCharacters();else ShowCategory(kind);});
            home.transform.Find(path+"/Text/Desc/Text_Desc").GetComponent<Text>().text=ChaptersFor(kind).Sum(c=>c.episodes.Length)+"개 에피소드";
        }
        var category=pages["Root_StoryCategory"];
        SetActive(category,"Contents/Anchor_Center/Layout/Bottom",false);
        category.transform.Find("Contents/Anchor_Top/Title/Text_Title").GetComponent<Text>().text="메인 스토리";
        cards=(RectTransform)category.transform.Find(CardPath);cardTemplate=cards.GetChild(0).gameObject;cardTemplate.SetActive(false);
        RebuildCategories("main");
        var chapterPage=pages["Root_StoryChapter"];
        SetActive(chapterPage,"Bg",true);SetActive(chapterPage,"Bg_Eventstory",false);
        entries=(RectTransform)chapterPage.transform.Find(EntryPath);entryTemplate=entries.GetChild(0).gameObject;entryTemplate.SetActive(false);
        foreach(var name in new[]{"Faction","Character","Archive","Eventstory"})SetActive(chapterPage,"Episode/Center/Left/"+name,false);
        SetActive(chapterPage,"Episode/Center/Left/Main",true);
        SetActive(chapterPage,"Episode/Center/Right/Info/Tab",false);
        var noticeGo=new GameObject("Menu notice",typeof(RectTransform),typeof(Text));noticeGo.transform.SetParent(stage,false);
        notice=noticeGo.GetComponent<Text>();notice.font=StoryContent.Load<Font>("Original/MFont_ko");notice.fontSize=18;notice.alignment=TextAnchor.MiddleCenter;notice.color=Color.white;notice.raycastTarget=false;
        var noticeShadow=noticeGo.AddComponent<Shadow>();noticeShadow.effectColor=Color.black;noticeShadow.effectDistance=new Vector2(1,-1);
        var nr=(RectTransform)noticeGo.transform;nr.anchorMin=new Vector2(0,0);nr.anchorMax=new Vector2(1,0);nr.pivot=new Vector2(.5f,0);nr.sizeDelta=new Vector2(0,42);nr.anchoredPosition=new Vector2(0,8);
        noticeGo.SetActive(false);
        Debug.Log("STORY_MENU_READY chapters="+Catalogue.chapters.Length+" episodes="+Catalogue.chapters.Sum(c=>c.episodes.Length));
        if(Application.isPlaying)StoryMenuRenderCheck.Attach(gameObject,stage);
    }
    static void SetActive(GameObject go,string path,bool active){var tr=go.transform.Find(path);if(tr)tr.gameObject.SetActive(active);}
    static void Bind(GameObject go,string path,UnityAction action){var b=(string.IsNullOrEmpty(path)?go.transform:go.transform.Find(path))?.GetComponent<Button>();if(!b)throw new InvalidOperationException("Menu button missing "+path);b.onClick=new Button.ButtonClickedEvent();b.onClick.AddListener(action);}
    static void Values(GameObject go,Dictionary<string,string> values){foreach(var text in go.GetComponentsInChildren<MText>(true))if(text.valueId!=null&&values.TryGetValue(text.valueId,out var v))text.text=v;}
    Sprite ImageFor(Chapter chapter) {
        return ImageForResource(chapter.image);
    }
    Sprite ImageForResource(string resource) {
        if(images.TryGetValue(resource,out var sprite))return sprite;
        if(resource.StartsWith("portrait:",StringComparison.Ordinal)){sprite=OriginalStoryUi.OriginalPortrait(resource.Substring(9));images.Add(resource,sprite);return sprite;}
        var texture=StoryContent.Load<Texture2D>(resource);
        if(!texture)throw new InvalidOperationException("Missing menu image "+resource);
        sprite=Sprite.Create(texture,new Rect(0,0,texture.width,texture.height),new Vector2(.5f,.5f));images.Add(resource,sprite);return sprite;
    }
    static void SetImage(Transform parent,string path,Sprite sprite){var image=parent.Find(path).GetComponent<Image>();image.overrideSprite=null;image.sprite=sprite;image.enabled=sprite;}
    void CreateChapter(Chapter chapter,int index) {
        var row=Instantiate(cardTemplate,cards,false);row.name=chapter.id;
        var rt=(RectTransform)row.transform;rt.anchoredPosition=new Vector2(80+index*(380+75),0);
        Values(row,new Dictionary<string,string>{{"Name",chapter.title},{"ChapterNumber",chapter.number},{"Order",chapter.order.ToString("00")},{"Progress",""}});
        var desc=row.transform.Find("Story/Story/Bottom/Text_Desc_TMP")??row.transform.Find("Story/Story/Bottom/Text_Desc");
        if(!desc)throw new InvalidOperationException("Missing chapter description");
        if(desc.GetComponent<TMP_Text>())desc.GetComponent<TMP_Text>().text=chapter.description;else desc.GetComponent<Text>().text=chapter.description;
        string top=chapter.category=="faction"?"Faction":chapter.category=="archive"?"Archive":chapter.category=="event"?"Eventstory":"Main";
        foreach(var name in new[]{"Main","Archive","Faction","Eventstory"})SetActive(row,"Story/Story/Top/"+name,name==top);
        SetImage(row.transform,"Story/Story/Top/"+top+(top=="Main"?"/Mask/Img_Chapter":"/Mask/Img"),ImageFor(chapter));
        SetActive(row,"Story/Story/Bottom/Prologue",chapter.prologue);
        foreach(var path in new[]{"Story/Story/LockInfo","Story/Story/Complete","Story/Story/New","Story/Story/Bottom/Progress"})SetActive(row,path,false);
        Bind(row,"Story",()=>ShowChapter(chapter));row.SetActive(true);
    }
    static void ConfigureContent(RectTransform content,bool horizontal,float length) {
        content.anchorMin=new Vector2(0,1);content.anchorMax=horizontal?new Vector2(0,1):new Vector2(1,1);content.pivot=new Vector2(0,1);
        content.sizeDelta=horizontal?new Vector2(length,620):new Vector2(0,length);content.anchoredPosition=Vector2.zero;
        var scroll=content.parent.parent.GetComponent<ScrollRect>();scroll.content=content;scroll.viewport=(RectTransform)content.parent;scroll.horizontal=horizontal;scroll.vertical=!horizontal;scroll.movementType=ScrollRect.MovementType.Clamped;
        scroll.StopMovement();scroll.horizontalNormalizedPosition=0;scroll.verticalNormalizedPosition=1;
    }
    void Show(string name,string clip) {
        SaveScroll();
        foreach(var page in pages.Values)page.SetActive(false);
        current=pages[name];current.SetActive(true);current.transform.SetAsLastSibling();
        notice.gameObject.SetActive(false);
        var a=current.GetComponent<Animation>();if(a&&a[clip]!=null)a.Play(clip);
        var hud=current.transform.Find("Hud/Hud").GetComponent<Animation>();if(hud&&hud["Hud_In"]!=null)hud.Play("Hud_In");
        Debug.Log("STORY_MENU_PAGE "+name);
        Shown?.Invoke();
    }
    public void ShowHome(){Show("Root_Story","Root_Story_In");if(Application.isPlaying)StartCoroutine(AuditHome());}
    System.Collections.IEnumerator AuditHome(){
        yield return new WaitForSecondsRealtime(.6f);
        var root=pages["Root_Story"].transform;
        foreach(var g in root.Find("Center/Bg").GetComponentsInChildren<Graphic>(true)){
            string path=g.name;var p=g.transform.parent;while(p&&p!=root){path=p.name+"/"+path;p=p.parent;}
            Debug.Log("MENU_BG_AUDIT "+path+" active="+g.gameObject.activeInHierarchy+" color="+g.color+" texture="+(g.mainTexture?g.mainTexture.name:"null")+" material="+g.material.name+" effects="+string.Join(",",g.GetComponents<BaseMeshEffect>().Select(e=>e.GetType().Name))+" rect="+g.rectTransform.rect+" scale="+g.transform.lossyScale+" groups="+string.Join(",",g.GetComponentsInParent<CanvasGroup>().Select(c=>c.alpha.ToString())));
        }
    }
    Chapter[] ChaptersFor(string kind)=>kind=="faction"?(Catalogue.factions??Array.Empty<Chapter>()):kind=="archive"?(Catalogue.archives??Array.Empty<Chapter>()):kind=="event"?(Catalogue.events??Array.Empty<Chapter>()):kind=="character"?(Catalogue.characters??Array.Empty<Chapter>()):Catalogue.chapters;
    void RebuildCategories(string kind){
        category=kind;
        // Cards are static; returning from an episode need not clone the entire
        // event/faction list again. Progress is refreshed in the episode page.
        if(builtCategory==kind)return;
        foreach(var child in cards.Cast<Transform>().ToArray())if(child.gameObject!=cardTemplate){child.gameObject.SetActive(false);Destroy(child.gameObject);}
        var list=ChaptersFor(kind);for(int i=0;i<list.Length;i++)CreateChapter(list[i],i);
        ConfigureContent(cards,true,80+list.Length*455+40);
        pages["Root_StoryCategory"].transform.Find("Contents/Anchor_Top/Title/Text_Title").GetComponent<Text>().text=kind=="faction"?"팩션 스토리":kind=="archive"?"아카이브":kind=="event"?"이벤트 스토리":"메인 스토리";
        builtCategory=kind;
    }
    public void ShowCategory(string kind){SaveScroll();if(current)current.SetActive(false);RebuildCategories(kind);Show("Root_StoryCategory","Root_Contents_In_2");RestoreScroll(cards,CategoryScrollKey,true);}
    public void ShowChapters()=>ShowCategory("main");
    void PrepareCharacters(){
        var page=pages["Root_Story_Char"];
        characterContent=(RectTransform)page.transform.Find("Contents/Anchor_Center/CharList/ScrollView/Viewport/Content");
        var group=characterContent.GetChild(0);group.Find("Title/Text_Title").GetComponent<Text>().text="프록시안 스토리";
        var grid=group.Find("Grid");var template=grid.GetChild(0).gameObject;template.SetActive(false);
        foreach(var chapter in ChaptersFor("character")){
            var row=Instantiate(template,grid,false);row.name=chapter.id;
            row.transform.Find("Text_Name").GetComponent<Text>().text=chapter.title;SetImage(row.transform,"Slot/Mask/Img_Character",ImageFor(chapter));
            SetActive(row,"LockInfo",false);SetActive(row,"New",false);Bind(row,"",()=>ShowChapter(chapter));row.SetActive(true);
        }
        var fitter=grid.GetComponent<ContentSizeFitter>()??grid.gameObject.AddComponent<ContentSizeFitter>();fitter.verticalFit=ContentSizeFitter.FitMode.PreferredSize;
    }
    public void ShowCharacters(){if(!characterContent&&pages.ContainsKey("Root_Story_Char"))PrepareCharacters();if(!characterContent){Notify("프록시안 자료가 아직 설치되지 않았습니다.");return;}Show("Root_Story_Char","Root_Contents_In_1");category="character";Canvas.ForceUpdateCanvases();RestoreScroll(characterContent,"story.menu.characters",false);}
    public void ShowChapter(Chapter chapter) {
        SaveScroll();
        // Hide the old list before replacing its chapter and content.
        if(current==pages["Root_StoryChapter"])current.SetActive(false);
        selected=chapter;
        category=chapter.category??"main";
        foreach(Transform child in entries)if(child.gameObject!=entryTemplate){child.gameObject.SetActive(false);Destroy(child.gameObject);}
        var page=pages["Root_StoryChapter"];var main=page.transform.Find("Episode/Center/Left/Main").gameObject;
        Values(main,new Dictionary<string,string>{{"Name",chapter.title},{"Desc",chapter.description},{"ChapterNumber","제 "+chapter.number+"장"}});
        SetActive(main,"Story/Bottom/Layout/Prologue",chapter.prologue);SetImage(main.transform,"Story/Story/Mask/Img_Chapter",ImageFor(chapter));
        foreach(var name in new[]{"Main","Faction","Archive","Eventstory","Character"})SetActive(page,"Episode/Center/Left/"+name,name.Equals(category=="event"?"Eventstory":category,StringComparison.OrdinalIgnoreCase));
        SetActive(page,"Bg",category!="event");SetActive(page,"Bg_Eventstory",category=="event");
        if(category=="faction"||category=="archive"||category=="event"){
            var panel=page.transform.Find("Episode/Center/Left/"+(category=="event"?"Eventstory":category=="archive"?"Archive":"Faction")).gameObject;
            SetImage(panel.transform,category=="event"?"Frame/Mask_Img/Img":"Group/Mask_Img/Img",ImageFor(chapter));
            if(category=="event"){SetActive(panel,"Frame/Logo",false);SetActive(panel,"Bottom/Info/Tag",false);}
            Values(panel,new Dictionary<string,string>{{"Name",chapter.title},{"Desc",chapter.description}});
            panel.transform.Find("Bottom/Info/Text_Title").GetComponent<Text>().text=chapter.title;
            panel.transform.Find("Bottom/Info/Text_Desc").GetComponent<Text>().text=chapter.description;
            SetActive(panel,"Group/Char_Grid",false);SetActive(panel,"Favor_Total",false);
        }
        if(category=="character"){
            var left=page.transform.Find("Episode/Center/Left/Character");SetActive(left.gameObject,"Favor",false);
            Values(left.gameObject,new Dictionary<string,string>{{"Name",chapter.title},{"Desc",chapter.description}});
            left.Find("Bottom/Info/Text_Title").GetComponent<Text>().text=chapter.title;
            left.Find("Bottom/Info/Text_Desc").GetComponent<Text>().text=chapter.description;
            var pivot=left.Find("Group/Spine/Mask/Pivot");var image=pivot.GetComponent<Image>()??pivot.gameObject.AddComponent<Image>();
            image.sprite=ImageFor(chapter);image.enabled=image.sprite;image.preserveAspect=true;image.raycastTarget=false;
            var rect=(RectTransform)pivot;rect.localScale=Vector3.one;rect.localRotation=Quaternion.identity;rect.anchorMin=Vector2.zero;rect.anchorMax=Vector2.one;rect.offsetMin=rect.offsetMax=Vector2.zero;
            SetActive(left.gameObject,"Group/Block",false);
        }
        var progress=page.transform.Find("Episode/Center/Right/Info/Progress/Bg/Text_Progress").GetComponent<Text>();
        progress.text=ReadCount(chapter)+"/"+chapter.episodes.Length;
        SetActive(page,"Episode/Center/Right/Info/Empty",chapter.episodes.Length==0);
        for(int i=0;i<chapter.episodes.Length;i++) {
            var ep=chapter.episodes[i];var row=Instantiate(entryTemplate,entries,false);row.name=ep.id;
            // Original UILoop has alignCenter=true. Keep the authored cell width
            // and center it inside the viewport instead of pinning it to x=0.
            var rowRect=(RectTransform)row.transform;
            rowRect.anchorMin=rowRect.anchorMax=new Vector2(.5f,1);
            rowRect.pivot=new Vector2(.5f,1);
            rowRect.anchoredPosition=new Vector2(0,-i*(rowRect.sizeDelta.y+10));
            Values(row,new Dictionary<string,string>{{"Number",ep.order.ToString()},{"Name",ep.title},{"ClearCondition",ep.clearCondition??""}});
            foreach(var path in new[]{"Contents/Center/LockInfo","Contents/Center/New_list","Contents/Center/New_Left","Contents/Center/Contents/RewardItem"})SetActive(row,path,false);
            SetActive(row,"Contents/Center/ListNum/ClearDesc",!string.IsNullOrEmpty(ep.clearCondition));
            Bind(row,"Contents/Center/Btn_Ok",()=>OpenEpisode(ep));row.SetActive(true);
        }
        ConfigureContent(entries,false,chapter.episodes.Length*118);
        Show("Root_StoryChapter","Root_Episode_In");
        RestoreScroll(entries,"story.menu.chapter."+chapter.id,false);
        Debug.Log("STORY_MENU_CHAPTER "+chapter.id+" entries="+chapter.episodes.Length);
    }
    public void OpenEpisode(Episode ep) {
        if(!ep.playable){Notify("이 이야기는 아직 재생 준비 중입니다.");return;}
        SaveScroll();playing=ep;current.SetActive(false);notice.gameObject.SetActive(false);PlayEpisode?.Invoke(ep);
    }
    public void OpenExternalEpisode(Episode ep,Action returned) {externalReturn=returned;OpenEpisode(ep);}
    public void ReturnFromEpisode(bool completed) {
        if(completed&&playing!=null) {
            if(Application.isBatchMode) {
                batchRead.Add(playing.id);
                if(batchProgressPath!=null){System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(batchProgressPath));System.IO.File.WriteAllText(batchProgressPath,BackendJson.ToJson(new BatchProgress{read=batchRead.ToArray()}));}
            } else {PlayerPrefs.SetInt("story.read."+playing.id,1);PlayerPrefs.Save();}
            Debug.Log("STORY_PROGRESS_SAVED "+playing.id);
        }
        playing=null;
        if(externalReturn!=null){var callback=externalReturn;externalReturn=null;Hide();callback();return;}
        if(selected!=null)ShowChapter(selected);else ShowHome();
    }
    public void Hide(){SaveScroll();foreach(var page in pages.Values)page.SetActive(false);if(notice)notice.gameObject.SetActive(false);}
    public void Back(){if(current==pages["Root_StoryChapter"]){if(category=="character")ShowCharacters();else ShowCategory(category);}else if(current==pages["Root_Story"]&&ReturnToViewer!=null)ReturnToViewer();else ShowHome();}
    void Notify(string value){notice.text=value;notice.transform.SetAsLastSibling();notice.gameObject.SetActive(true);CancelInvoke(nameof(HideNotice));Invoke(nameof(HideNotice),4);}
    void HideNotice(){notice.gameObject.SetActive(false);}
    void Update(){if(Visible&&Input.GetKeyDown(KeyCode.Backspace))Back();}
    void SaveScroll() {
        // Automated offscreen checks must not overwrite the user's scroll history.
        if(!Application.isPlaying||Application.isBatchMode||!Visible||restoringScroll)return;
        if(pages.TryGetValue("Root_Story_Char",out var charPage)&&current==charPage)PlayerPrefs.SetFloat("story.menu.characters",characterContent.parent.parent.GetComponent<ScrollRect>().verticalNormalizedPosition);
        else if(current==pages["Root_StoryCategory"])PlayerPrefs.SetFloat(CategoryScrollKey,cards.parent.parent.GetComponent<ScrollRect>().horizontalNormalizedPosition);
        else if(current==pages["Root_StoryChapter"]&&selected!=null)PlayerPrefs.SetFloat("story.menu.chapter."+selected.id,entries.parent.parent.GetComponent<ScrollRect>().verticalNormalizedPosition);
        PlayerPrefs.Save();
    }
    void RestoreScroll(RectTransform content,string key,bool horizontal) {
        var scroll=content.parent.parent.GetComponent<ScrollRect>();
        float position=Mathf.Clamp01(PlayerPrefs.GetFloat(key,horizontal?0:1));
        Canvas.ForceUpdateCanvases();scroll.StopMovement();
        if(horizontal)scroll.horizontalNormalizedPosition=position;else scroll.verticalNormalizedPosition=position;
        if(Application.isPlaying)StartCoroutine(RestoreAfterLayout(scroll,position,horizontal));
    }
    System.Collections.IEnumerator RestoreAfterLayout(ScrollRect scroll,float position,bool horizontal) {
        restoringScroll=true;yield return null;Canvas.ForceUpdateCanvases();scroll.StopMovement();
        if(horizontal)scroll.horizontalNormalizedPosition=position;else scroll.verticalNormalizedPosition=position;
        restoringScroll=false;
    }
    void OnApplicationQuit(){SaveScroll();}
    void OnDestroy(){foreach(var sprite in images.Values)if(sprite)Destroy(sprite);}
}

public sealed class OriginalMenuTween : MonoBehaviour {
    OriginalStoryMenu.Tween data;Vector3 initialPosition,initialScale,initialRotation;float initialAlpha,elapsed;Graphic graphic;CanvasGroup group;
    public void Configure(OriginalStoryMenu.Tween tween){data=tween;initialPosition=transform.localPosition;initialScale=transform.localScale;initialRotation=transform.localEulerAngles;graphic=GetComponent<Graphic>();group=GetComponent<CanvasGroup>();initialAlpha=graphic?graphic.color.a:group?group.alpha:1;}
    void OnEnable(){elapsed=0;}
    void Update(){
        if(data==null)return;elapsed+=Time.unscaledDeltaTime;if(elapsed<data.delay)return;
        float time=(elapsed-data.delay)/Mathf.Max(.001f,data.duration);int cycle=Mathf.FloorToInt(time);
        float t=data.loops>0&&cycle>=data.loops?1:time-cycle;
        if(data.loopType==1&&cycle%2==1)t=1-t;
        if(data.loops>0&&cycle>=data.loops)t=data.loopType==1&&data.loops%2==0?0:1;
        t=Ease(t,data.ease);if(data.fromValue)t=1-t;
        if(data.kind==2)transform.localPosition=Vector3.LerpUnclamped(initialPosition,data.relative?initialPosition+data.vector:data.vector,t);
        if(data.kind==3)transform.localEulerAngles=Vector3.LerpUnclamped(initialRotation,data.relative?initialRotation+data.vector:data.vector,t);
        if(data.kind==5)transform.localScale=Vector3.LerpUnclamped(initialScale,Vector3.one*data.value,t);
        if(data.kind==7){float a=Mathf.LerpUnclamped(initialAlpha,data.value,t);if(graphic){var c=graphic.color;c.a=a;graphic.color=c;}else if(group)group.alpha=a;}
    }
    static float Ease(float t,int ease){if(ease==1)return t;if(ease==6)return 1-(1-t)*(1-t);if(ease==28){const float c=1.70158f*1.525f;return t<.5f?Mathf.Pow(2*t,2)*((c+1)*2*t-c)/2:(Mathf.Pow(2*t-2,2)*((c+1)*(2*t-2)+c)+2)/2;}throw new InvalidOperationException("Unsupported original menu ease "+ease);}
}

#endif
