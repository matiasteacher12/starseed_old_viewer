#if UNITY_EDITOR || VIEWER_BACKEND
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Preserved UI assets; only local data, navigation and missing game scrollers are replaced.
public sealed partial class OriginalInstarseedUi : MonoBehaviour {
    [Serializable] class Config {public string uiBundle;public string[] extraBundles;public OriginalStoryMenu.Page[] pages;public LocalText[] localization;}
    [Serializable] class LocalText {public string key,value;}
    Config config;AssetBundle bundle;RectTransform stage;GameObject frame,shell,current,holder;
    TMP_FontAsset font;TMP_SpriteAsset emoji;InstarseedView.Catalogue data;Action home;
    public Action<InstarseedView.Post> PlayVideo;
    readonly Dictionary<string,GameObject> prefabs=new Dictionary<string,GameObject>();
    readonly Dictionary<string,GameObject> pages=new Dictionary<string,GameObject>();
    readonly List<Row> rows=new List<Row>();
    readonly InstarseedImageCache pictures=new InstarseedImageCache();
    readonly Dictionary<(string,float,float),float> heights=new Dictionary<(string,float,float),float>();
    readonly bool baseline=Application.isBatchMode&&Environment.GetCommandLineArgs().Contains("-instarseed-perf-baseline");
    int measurements,decodes;string[] bundleNames;
    public int PictureDecodes=>baseline?decodes:pictures.Decodes;
    public int Measurements=>measurements;
    public int LoadedPrefabs=>prefabs.Count;
    class Row {public float y,height;public Func<Transform,GameObject> make;public GameObject go;}
    ScrollRect scroll;RectTransform content;Action back;InstarseedView.Post selected;
    public bool Visible=>frame&&frame.activeSelf;
    public int LiveRows=>rows.Count(r=>r.go);
    const string HomeCell="Root_Instarseed_Home_feedPrefab", ProfileCell="Root_Instarseed_Profile_profileCellViewPrefab", AlbumCell="Root_Instarseed_Profile_postCellViewPrefab";
    public void Initialize(RectTransform parent,TMP_FontAsset f,TMP_SpriteAsset e,InstarseedView.Catalogue d,Action returnHome){
        stage=parent;font=f;emoji=e;data=d;home=returnHome;
        config=BackendJson.FromJson<Config>(File.ReadAllText(StoryContent.FilePath("Instarseed/ui.json")));
        foreach(var path in config.extraBundles)if(!AssetBundle.LoadFromFile(StoryContent.FilePath(path)))throw new InvalidDataException(path);
        bundle=AssetBundle.GetAllLoadedAssetBundles().First(b=>b.GetAllAssetNames().Any(n=>string.Equals(n,config.pages[0].asset,StringComparison.OrdinalIgnoreCase)));
        holder=new GameObject("Original Instarseed preparation");holder.SetActive(false);
        bundleNames=bundle.GetAllAssetNames();
        if(baseline)foreach(var p in config.pages)Prefab(p.name);
        frame=Make("Root_InstarseedFrame",stage);Stretch(frame.transform);frame.SetActive(false);
        shell=Make("Root_Instarseed",frame.transform.Find("InstarseedFrame/InstarseedStack"));
        // Original cells are authored at 1280 units. Fit them into the authored phone frame.
        var rt=(RectTransform)shell.transform;rt.anchorMin=rt.anchorMax=rt.pivot=new Vector2(.5f,.5f);rt.anchoredPosition=Vector2.zero;rt.sizeDelta=new Vector2(1280,620*1280f/720);rt.localScale=Vector3.one*(720f/1280);
        var host=shell.transform.Find("Center/SafeArea/Stack_Home");
        if(baseline)foreach(var n in new[]{"Home","Follower","Profile","Comment"})Page(n);
        foreach(var n in new[]{"Stack_Follow","Stack_Special","Account","FollowerList","Special"})Off(shell,"Center/SafeArea/"+n);
        Bind(shell,"Center/HUD_Btm/Layout/Toggle_Home",()=>ShowFeed());
        Bind(shell,"Center/HUD_Btm/Layout/Toggle_Follower",ShowProfiles);
        Bind(shell,"Center/HUD_Btm/Layout/Toggle_Special",()=>ShowFeed(true));
        Bind(shell,"Center/HUD_Btm/Layout/Button_exit",home);
        Bind(shell,"Center/HUD_Top/Top/Favor_Top/Btn_Back",Back);
        foreach(var n in new[]{"Battery","Network","Sound"})Off(shell,"Center/HUD_Top/Top/Favor_Top/"+n);
        Set(shell,"Center/HUD_Top/Top/Favor_Top/Time","OFFLINE");
        shell.SetActive(true);Debug.Log("INSTARSEED_NATIVE_READY pages="+pages.Count+" loadedPrefabs="+prefabs.Count);
    }
    GameObject Prefab(string name){
        if(prefabs.TryGetValue(name,out var asset))return asset;
        var path=config.pages.FirstOrDefault(p=>p.name==name)?.asset??bundleNames.Single(n=>n.EndsWith("/"+name+".prefab",StringComparison.OrdinalIgnoreCase));
        asset=bundle.LoadAsset<GameObject>(bundleNames.First(n=>string.Equals(n,path,StringComparison.OrdinalIgnoreCase)));
        if(!asset)throw new InvalidDataException(path);prefabs[name]=asset;return asset;
    }
    GameObject Page(string name){
        if(pages.TryGetValue(name,out var page))return page;
        page=Make(name=="Chatting"?"Popup_Instarseed_Chatting":"Root_Instarseed_"+name,shell.transform.Find("Center/SafeArea/Stack_Home"));
        Stretch(page.transform);page.SetActive(false);pages[name]=page;return page;
    }
    GameObject Make(string name,Transform parent){var go=Instantiate(Prefab(name),holder.transform,false);go.name=name;Prepare(go);go.SetActive(false);go.transform.SetParent(parent,false);go.SetActive(true);return go;}
    void Prepare(GameObject go){
        foreach(var b in go.GetComponentsInChildren<Button>(true)){b.onClick=new Button.ButtonClickedEvent();b.interactable=false;b.navigation=new Navigation{mode=Navigation.Mode.None};}
        foreach(var t in go.GetComponentsInChildren<Toggle>(true)){t.onValueChanged=new Toggle.ToggleEvent();t.interactable=false;t.navigation=new Navigation{mode=Navigation.Mode.None};}
        foreach(var t in go.GetComponentsInChildren<Text>(true)){t.font=StoryContent.Load<Font>("Original/MFont_ko");t.raycastTarget=false;var v=config.localization.FirstOrDefault(x=>x.key==t.text);if(v!=null)t.text=v.value;}
        foreach(var t in go.GetComponentsInChildren<TMP_Text>(true)){t.font=font;t.fontSharedMaterial=font.material;t.spriteAsset=emoji;t.raycastTarget=false;}
        foreach(var c in go.GetComponentsInChildren<Canvas>(true))c.overrideSorting=false;
        foreach(var a in go.GetComponentsInChildren<Animation>(true)){a.playAutomatically=false;a.Stop();}
        foreach(var fx in go.GetComponentsInChildren<Coffee.UIExtensions.UIParticle>(true)){fx.raycastTarget=false;fx.particles.RemoveAll(ps=>!ps||!ps.GetComponent<ParticleSystemRenderer>()||!ps.GetComponent<ParticleSystemRenderer>().sharedMaterial);}
    }
    public void Hide(){ClearRows();pictures.Retain(false);if(frame)frame.SetActive(false);if(current)current.SetActive(false);}
    public void Back(){(back??home)();}
    static void ReleaseRow(GameObject row){foreach(var lease in row.GetComponentsInChildren<InstarseedRowAssets>(true))lease.Release();row.SetActive(false);Destroy(row);}
    void ClearRows(){foreach(var row in rows)if(row.go)ReleaseRow(row.go);rows.Clear();scroll=null;content=null;}
    void Show(string name,Action previous,int tab){ClearRows();foreach(var p in pages.Values)p.SetActive(false);frame.SetActive(true);frame.transform.SetAsLastSibling();pictures.Retain(true);current=Page(name);current.SetActive(true);back=previous;
        Active(shell,"Center/HUD_Top/Top/Favor_Top/Time",name!="Chatting");
        var keys=new[]{"Home","Follower","Special"};for(int i=0;i<3;i++){var path="Center/HUD_Btm/Layout/Toggle_"+keys[i];shell.transform.Find(path).GetComponent<Toggle>().SetIsOnWithoutNotify(i==tab);Active(shell,path+"/On",i==tab);Active(shell,path+"/Off",i!=tab);Off(shell,path+"/New");}
        Canvas.ForceUpdateCanvases();Debug.Log("INSTARSEED_NATIVE_PAGE "+name);
    }
    static void Stretch(Transform t){var rt=(RectTransform)t;rt.anchorMin=Vector2.zero;rt.anchorMax=Vector2.one;rt.offsetMin=rt.offsetMax=Vector2.zero;rt.localScale=Vector3.one;}
    static void Place(Transform t,float x,float y,float w,float h){var r=(RectTransform)t;r.anchorMin=r.anchorMax=new Vector2(0,1);r.pivot=new Vector2(0,1);r.anchoredPosition=new Vector2(x,-y);r.sizeDelta=new Vector2(w,h);r.localScale=Vector3.one;}
    static void Active(GameObject go,string path,bool value){var t=go.transform.Find(path);if(t)t.gameObject.SetActive(value);}
    static void Off(GameObject go,string path)=>Active(go,path,false);
    static void Disabled(GameObject go,string path){var t=go.transform.Find(path);if(!t)return;var c=t.GetComponent<CanvasGroup>()??t.gameObject.AddComponent<CanvasGroup>();c.alpha=.3f;c.interactable=false;c.blocksRaycasts=false;foreach(var tr in t.GetComponentsInChildren<Transform>(true))if(tr.name=="New")tr.gameObject.SetActive(false);}

    static void Set(GameObject go,string path,string value){var t=go.transform.Find(path);if(!t)return;var tmp=t.GetComponent<TMP_Text>();if(tmp)tmp.text=value??"";else {var text=t.GetComponent<Text>();if(text)text.text=value??"";}}
    static void Bind(GameObject go,string path,Action action){var t=string.IsNullOrEmpty(path)?go.transform:go.transform.Find(path);if(!t)throw new InvalidDataException("UI control missing "+go.name+"/"+path);var toggle=t.GetComponent<Toggle>();if(toggle){toggle.enabled=true;toggle.interactable=true;toggle.group=null;if(toggle.graphic)toggle.graphic.CrossFadeAlpha(1,0,true);toggle.graphic=null;toggle.transition=Selectable.Transition.None;toggle.onValueChanged=new Toggle.ToggleEvent();toggle.onValueChanged.AddListener(_=>action());return;}var b=t.GetComponent<Button>()??t.gameObject.AddComponent<Button>();b.interactable=true;b.navigation=new Navigation{mode=Navigation.Mode.None};b.onClick=new Button.ButtonClickedEvent();b.onClick.AddListener(()=>action());var g=t.GetComponent<Graphic>();if(!g)g=t.gameObject.AddComponent<Image>();g.raycastTarget=true;b.targetGraphic=g;if(g is Image&&((Image)g).sprite==null)g.color=Color.clear;}
    void Picture(GameObject row,string path,string file){var t=row.transform.Find(path);if(!t)throw new InvalidDataException("UI picture missing "+path);var image=t.GetComponent<Image>();if(!image)throw new InvalidDataException("Image component missing "+path);var lease=row.GetComponent<InstarseedRowAssets>()??row.AddComponent<InstarseedRowAssets>();Sprite sprite;
        if(baseline){var texture=new Texture2D(2,2,TextureFormat.RGBA32,false);if(!texture.LoadImage(File.ReadAllBytes(StoryContent.FilePath(file))))throw new InvalidDataException(file);sprite=Sprite.Create(texture,new Rect(0,0,texture.width,texture.height),new Vector2(.5f,.5f));lease.assets.Add(texture);lease.assets.Add(sprite);decodes++;}
        else {sprite=pictures.Acquire(file,out var release);lease.releases.Add(release);}
        image.overrideSprite=null;image.sprite=sprite;image.color=Color.white;image.material=null;}
    void Scroller(string path){var t=current.transform.Find(path);if(!t)throw new InvalidDataException(path);scroll=t.GetComponent<ScrollRect>()??t.gameObject.AddComponent<ScrollRect>();var viewport=(t.Find("Viewport")??t.Find("Viewer viewport")) as RectTransform;if(!viewport){foreach(Transform child in t)child.gameObject.SetActive(false);viewport=new GameObject("Viewer viewport",typeof(RectTransform),typeof(Image),typeof(RectMask2D)).GetComponent<RectTransform>();viewport.SetParent(t,false);Stretch(viewport);viewport.GetComponent<Image>().color=Color.clear;}
        // The original Comment/Profile scroller extends behind the bottom HUD.
        // Its replacement viewport must end at the visible navigation edge.
        var nav=(RectTransform)shell.transform.Find("Center/HUD_Btm/Layout");
        var navTop=nav.TransformPoint(new Vector3(0,nav.rect.yMax,0));
        float overlap=viewport.InverseTransformPoint(navTop).y-viewport.rect.yMin;
        if(overlap>0.01f){var minimum=viewport.offsetMin;minimum.y+=overlap;viewport.offsetMin=minimum;}
        foreach(Transform child in viewport){child.gameObject.SetActive(false);if(child.name=="Viewer content")Destroy(child.gameObject);}
        content=new GameObject("Viewer content",typeof(RectTransform)).GetComponent<RectTransform>();content.SetParent(viewport,false);Place(content,0,0,1280,0);
        scroll.viewport=viewport;scroll.content=content;scroll.horizontal=false;scroll.vertical=true;scroll.movementType=ScrollRect.MovementType.Clamped;scroll.scrollSensitivity=85;scroll.StopMovement();scroll.onValueChanged=new ScrollRect.ScrollRectEvent();scroll.onValueChanged.AddListener(_=>Refresh());
    }
    void Add(float height,Func<Transform,GameObject> make){float y=rows.Count==0?0:rows.Last().y+rows.Last().height;rows.Add(new Row{y=y,height=height,make=make});content.sizeDelta=new Vector2(1280,y+height+20);}
    void Refresh(){if(!scroll)return;float top=content.anchoredPosition.y,bottom=top+scroll.viewport.rect.height;foreach(var row in rows){bool needed=row.y+row.height>=top-100&&row.y<=bottom+100;if(needed&&!row.go){row.go=row.make(content);Place(row.go.transform,0,row.y,1280,row.height);}else if(!needed&&row.go){ReleaseRow(row.go);row.go=null;}}}
    void LateUpdate(){if(Visible)Refresh();}
    float TextHeight(string text,float width,float size){
        var key=(text??"",width,size);if(!baseline&&heights.TryGetValue(key,out var cached))return cached;
        float height=Mathf.Max(size*1.4f,font?Measure(text,width,size):size*2);
        if(!baseline){if(heights.Count>=4096)heights.Clear();heights[key]=height;}return height;
    }
    TMP_Text measure;
    float Measure(string text,float width,float size){
        if(!measure){var go=new GameObject("Instarseed text measurement",typeof(RectTransform),typeof(CanvasGroup));go.SetActive(false);go.transform.SetParent(stage,false);go.GetComponent<CanvasGroup>().alpha=0;go.GetComponent<CanvasGroup>().blocksRaycasts=false;measure=go.AddComponent<TextMeshProUGUI>();measure.font=font;measure.fontSharedMaterial=font.material;measure.spriteAsset=emoji;measure.raycastTarget=false;measure.enableAutoSizing=false;measure.enableWordWrapping=true;measure.fontStyle=FontStyles.Bold;go.SetActive(true);}
        measurements++;measure.fontSize=size;measure.text=text??"";Place(measure.transform,10000,10000,width,100000);measure.ForceMeshUpdate(true,true);float height=measure.preferredHeight;
        if(height<=0&&!string.IsNullOrEmpty(text))throw new InvalidOperationException("Instarseed text measurement failed");return height;
    }
    static void Manual(GameObject go){foreach(var l in go.GetComponents<LayoutGroup>())l.enabled=false;foreach(var f in go.GetComponents<ContentSizeFitter>())f.enabled=false;}
    float Paragraph(GameObject row,string path,string text,float x,float y,float width,float size){var t=row.transform.Find(path);t.gameObject.SetActive(true);var tmp=t.GetComponent<TMP_Text>();tmp.fontSize=size;tmp.enableAutoSizing=false;tmp.text=text??"";tmp.overflowMode=TextOverflowModes.Overflow;float height=TextHeight(text,width,size);Place(t,x,y,width,height);return height;}
    public void ShowFeed(bool special=false,InstarseedView.Post only=null){Show("Home",special||only!=null?(Action)(()=>ShowFeed()):home,special?2:0);selected=only;
        foreach(var n in new[]{"Body","Comment","Comment_Reply","Top_Profile","Top_Special"})Off(current,n);
        Bind(current,"Top_Profile/Btn_Back",Back);Bind(current,"Top_Special/Btn_Back",Back);Active(current,"Top",!special&&only==null);Active(current,"Top_Profile",only!=null);Active(current,"Top_Special",special);if(only!=null)Set(current,"Top_Profile/Text_ID",data.profiles.First(p=>p.id==only.profile).name);
        Disabled(current,"Top/Btn_Dm");Disabled(current,"Top/Btn_ModeChange");Scroller("Ani/Scroll View");var posts=only==null?data.posts.Where(p=>!special||p.special).ToArray():new[]{only};
        foreach(var post in posts){var p=post;float media=new[]{720,1280,1600}[Mathf.Clamp(p.ratio,0,2)];float body=TextHeight(p.text,1172,40)+12;Add(100+media+76+body+60+20,parent=>FeedCell(parent,p,media,body));}Refresh();
        Debug.Log("INSTARSEED_NATIVE_FEED posts="+posts.Length+" special="+special);
    }
    GameObject FeedCell(Transform parent,InstarseedView.Post post,float mediaHeight,float bodyHeight){var row=Make(HomeCell,parent);Manual(row);var owner=data.profiles.First(p=>p.id==post.profile);Set(row,"Top/ID/Text_id",owner.name);Picture(row,"Top/Portrait/Mask/Img_Portrait",owner.icon);Bind(row,"Top/Portrait",()=>ShowProfile(owner));Bind(row,"Top/ID",()=>ShowProfile(owner));Off(row,"Top/ID/New");Off(row,"Top/ID/Mark");
        foreach(var name in new[]{"Type_1","Type_2","Type_3","Btn_Ad","Likes","Comment","Comment_Reply","Time","Reward"})Off(row,name);
        int ratio=Mathf.Clamp(post.ratio,0,2);string type="Type_"+(ratio+1);Active(row,type,true);var media=row.transform.Find(type);Place(media,0,100,1280,mediaHeight);
        foreach(Transform child in media)child.gameObject.SetActive(false);var cell=Make("Instarseed_Media_"+ratio,media);Stretch(cell.transform);
        foreach(var n in new[]{"PostVideo","LoadingIndicator","Touch_Ani"})Off(cell,n);Active(cell,"PostImg",true);Picture(cell,"PostImg",post.type==0?post.media:post.thumbnail);
        if(post.type==1){Bind(cell,"PostImg",()=>PlayVideo(post));var icon=Prefab(AlbumCell).transform.Find("Anchor_1/Slot_Album/Icon_video");if(icon){var play=Instantiate(icon.gameObject,cell.transform,false);play.SetActive(true);Prepare(play);var pr=(RectTransform)play.transform;pr.anchorMin=pr.anchorMax=pr.pivot=new Vector2(.5f,.5f);pr.anchoredPosition=Vector2.zero;pr.sizeDelta=new Vector2(110,110);Bind(play,"",()=>PlayVideo(post));}}
        Place(row.transform.Find("Top"),0,0,1280,100);Place(row.transform.Find("Menu"),0,100+mediaHeight,1280,76);Bind(row,"Menu/Left/Btn_Comment",()=>ShowComments(post));Off(row,"Menu/Page_Layout");Disabled(row,"Menu/Left/Btn_Heart");Disabled(row,"Menu/Btn_Dm");
        var body=row.transform.Find("Body");Manual(body.gameObject);Place(body,0,176+mediaHeight,1280,bodyHeight);Off(body.gameObject,"Text_Low");Off(body.gameObject,"Text_Extend");Paragraph(body.gameObject,"Text (TMP)",post.text,28,6,1172,40);
        var more=row.transform.Find("Comment_More");Place(more,0,176+mediaHeight+bodyHeight,1280,60);Set(row,"Comment_More/Text_More","댓글 "+data.replies.Count(r=>r.post==post.id)+"개 모두 보기");Bind(row,"Comment_More",()=>ShowComments(post));return row;
    }
    public void ShowProfiles(){Show("Follower",()=>ShowFeed(),1);Bind(current,"Ani/Top/Btn_Back",Back);var template=current.transform.Find("Ani/ScrollView/Viewport/Content/List_Instarseed_Follower").gameObject;Scroller("Ani/ScrollView");foreach(var person in data.profiles.OrderBy(p=>p.name,StringComparer.CurrentCulture)){var p=person;Add(140,parent=>{var row=Instantiate(template,parent,false);Prepare(row);var alpha=row.GetComponent<CanvasGroup>();if(alpha)alpha.alpha=1;row.SetActive(true);Set(row,"Info/ID/Text_id",p.name);Set(row,"Info/Post/Text_Num",data.posts.Count(x=>x.profile==p.id).ToString());Picture(row,"Portrait/Mask/Img_Portrait",p.icon);Off(row,"Portrait/New");Bind(row,"Btn_Profile",()=>ShowProfile(p));return row;});}Refresh();}
    public void ShowProfile(InstarseedView.Profile person){Show("Profile",ShowProfiles,1);Disabled(current,"Ani/Top_FollowerList/Btn_Greeting");Bind(current,"Ani/Top_FollowerList/Btn_Back",ShowProfiles);Set(current,"Ani/Top_FollowerList/Layout_Title/Text_ID",person.name);Scroller("Ani/Scroll View");var posts=data.posts.Where(p=>p.profile==person.id).ToArray();float infoHeight=Mathf.Max(150,TextHeight(person.description,1224,32)+20);float headerHeight=270+infoHeight;Add(headerHeight,parent=>{var row=Make(ProfileCell,parent);Manual(row);Picture(row,"Portrait/Mask/Img_Portrait",person.icon);Off(row,"Portrait/New");Off(row,"Deta/Favor");Off(row,"Deta/PostInfo");Set(row,"Deta/Post_1/Text_Num",posts.Length.ToString());Off(row,"Link");ConfigureMessageButton(row,person);Place(row.transform.Find("Portrait"),28,20,160,160);Place(row.transform.Find("Deta"),220,32,1020,150);var info=row.transform.Find("Info");Manual(info.gameObject);Place(info,28,214,1224,infoHeight);Set(row,"Info/Text",person.description);Stretch(info.Find("Text"));Place(row.transform.Find("Menu"),24,headerHeight-45,1232,45);return row;});
        for(int i=0;i<posts.Length;i+=3){int start=i;Add(426,parent=>{var row=Make(AlbumCell,parent);Off(row,"Empty");for(int j=0;j<3;j++){string path="Anchor_"+(j+1)+"/Slot_Album";bool exists=start+j<posts.Length;Active(row,"Anchor_"+(j+1),exists);if(!exists)continue;var p=posts[start+j];Off(row,path+"/Empty");Picture(row,path+"/Img_Preview",p.thumbnail);Active(row,path+"/Icon_video",p.type==1);Bind(row,path+"/Img_Preview",()=>{ShowFeed(false,p);back=()=>ShowProfile(person);});}return row;});}Refresh();}
    public void ShowComments(InstarseedView.Post post){Show("Comment",()=>ShowFeed(false,post),0);Bind(current,"Ani/Top/Btn_Back",Back);Scroller("Ani/Scroll View");AddComment(data.profiles.First(p=>p.id==post.profile),post.text,"commentCellViewPrefab",false);
        var replies=data.replies.Where(r=>r.post==post.id).OrderBy(r=>r.order).ThenBy(r=>r.id).ToArray();foreach(var r in replies)AddComment(data.profiles.First(p=>p.id==r.author),r.text,r.parent==0?"replyCellViewPrefab":"matchReplyCellViewPrefab",r.parent!=0);Refresh();Debug.Log("INSTARSEED_NATIVE_COMMENTS "+post.id+" replies="+replies.Length);}
    void AddComment(InstarseedView.Profile person,string text,string kind,bool reply){float x=reply?300:164;float height=Mathf.Max(140,60+TextHeight(text,1252-x,40)+32);Add(height,parent=>{var row=Make("Root_Instarseed_Comment_"+kind,parent);Manual(row);foreach(var n in new[]{"Reaction","Touch_ReactionSelect","Text","Portrait/New"})Off(row,n);Picture(row,"Portrait/Mask/Img_Portrait",person.icon);Set(row,"ID/Text_id",person.name);Manual(row.transform.Find("ID").gameObject);Place(row.transform.Find("ID"),x,12,1252-x,40);Stretch(row.transform.Find("ID/Text_id"));Paragraph(row,"Text (TMP)",text,x,58,1252-x,40);Bind(row,"Portrait",()=>ShowProfile(person));return row;});}
    public GameObject VideoPage(){Hide();var go=Make("Root_VideoPlayer_Instarseed",stage);Stretch(go.transform);
        // The original controller sizes the movie area and fills these placeholder panels.
        // This local player fits the decoded aspect ratio; it has no account-side panel.
        Stretch(go.transform.Find("Center/SafeArea/Scaler"));Stretch(go.transform.Find("Center/SafeArea/Scaler/Image"));
        Off(go,"Center/SafeArea/Scaler/Navi");go.transform.Find("Center/Btm/Menu").GetComponent<Image>().enabled=false;
        go.transform.Find("BackCollider").GetComponent<Image>().color=Color.black;
        var close=go.transform.Find("Center/Btm/Menu/Button_Close").GetComponent<Image>();close.color=Color.clear;close.raycastTarget=true;
        // box_btn_normal is the authored hit target, not a visible close symbol.
        var arrow=Instantiate(Page("Home").transform.Find("Top_Profile/Btn_Back/Back").gameObject,close.transform,false);Prepare(arrow);arrow.SetActive(true);
        var ar=(RectTransform)arrow.transform;ar.anchorMin=ar.anchorMax=ar.pivot=new Vector2(.5f,.5f);ar.anchoredPosition=Vector2.zero;ar.sizeDelta=new Vector2(28,42);ar.localScale=Vector3.one;
        var ag=arrow.GetComponent<Image>();ag.color=Color.white;ag.raycastTarget=false;
        return go;}

    public System.Collections.IEnumerator CheckTabs(Action<string> capture){
        var ev=new UnityEngine.EventSystems.PointerEventData(UnityEngine.EventSystems.EventSystem.current){button=UnityEngine.EventSystems.PointerEventData.InputButton.Left};
        var names=new[]{"Home","Follower","Special"};
        for(int pass=0;pass<2;pass++)for(int tab=0;tab<3;tab++){
            var toggle=shell.transform.Find("Center/HUD_Btm/Layout/Toggle_"+names[tab]).GetComponent<Toggle>();
            for(int click=0;click<2;click++){
                toggle.OnPointerClick(ev);yield return new WaitForSecondsRealtime(.15f);
                for(int i=0;i<3;i++){
                    var t=shell.transform.Find("Center/HUD_Btm/Layout/Toggle_"+names[i]);var selected=i==tab;
                    if(t.GetComponent<Toggle>().isOn!=selected||t.Find("On").gameObject.activeSelf!=selected||t.Find("Off").gameObject.activeSelf==selected)throw new Exception("Tab selection mismatch");
                    var icon=t.Find(selected?"On":"Off").GetComponent<Graphic>();if(!icon||icon.canvasRenderer.GetAlpha()<.99f)throw new Exception("Tab icon hidden after real click: "+names[i]);
                }
            }
            toggle.OnSubmit(new UnityEngine.EventSystems.BaseEventData(UnityEngine.EventSystems.EventSystem.current));yield return new WaitForSecondsRealtime(.15f);
            if(!toggle.isOn)throw new Exception("Tab submit deselected");
            int expected=tab==0?data.posts.Length:tab==1?data.profiles.Length:data.posts.Count(p=>p.special);
            if(rows.Count!=expected)throw new Exception("Wrong tab content");capture("tabs-"+names[tab]);
            if(tab==2){current.transform.Find("Top_Special/Btn_Back").GetComponent<Button>().onClick.Invoke();yield return null;if(!Visible||current!=pages["Home"]||rows.Count!=data.posts.Length||!current.transform.Find("Top").gameObject.activeSelf)throw new Exception("Special back left Instarseed home");capture("special-back-home");}
        }
        Debug.Log("INSTARSEED_TABS_OK realClicks=12 submits=6 home="+data.posts.Length+" special="+data.posts.Count(p=>p.special)+" specialBack=home");
    }
    public void CheckNavigation(){
        shell.transform.Find("Center/HUD_Btm/Layout/Toggle_Follower").GetComponent<Toggle>().onValueChanged.Invoke(true);if(current!=pages["Follower"])throw new Exception("Follower tab failed");
        rows.First(r=>r.go).go.transform.Find("Btn_Profile").GetComponent<Button>().onClick.Invoke();if(current!=pages["Profile"])throw new Exception("Profile control failed");
        current.transform.Find("Ani/Top_FollowerList/Btn_Back").GetComponent<Button>().onClick.Invoke();if(current!=pages["Follower"])throw new Exception("Profile back failed");
        shell.transform.Find("Center/HUD_Btm/Layout/Toggle_Home").GetComponent<Toggle>().onValueChanged.Invoke(true);if(current!=pages["Home"])throw new Exception("Home tab failed");
        rows.First(r=>r.go).go.transform.Find("Comment_More").GetComponent<Button>().onClick.Invoke();if(current!=pages["Comment"])throw new Exception("Comment control failed");
        current.transform.Find("Ani/Top/Btn_Back").GetComponent<Button>().onClick.Invoke();if(current!=pages["Home"])throw new Exception("Comment back failed");
        shell.transform.Find("Center/HUD_Btm/Layout/Toggle_Special").GetComponent<Toggle>().onValueChanged.Invoke(true);if(rows.Count!=data.posts.Count(p=>p.special))throw new Exception("Special tab failed");
        shell.transform.Find("Center/HUD_Btm/Layout/Button_exit").GetComponent<Button>().onClick.Invoke();if(Visible)throw new Exception("Instarseed exit failed");Debug.Log("INSTARSEED_NATIVE_NAVIGATION_OK");
    }
    public void CheckCache(){pictures.Check();}
    public string PerformanceState()=>"decodes="+PictureDecodes+" measurements="+measurements+" prefabs="+prefabs.Count+" cacheBytes="+pictures.Bytes+" cachePeak="+pictures.PeakBytes+" hits="+pictures.Hits;
    public void CheckCacheEmpty(){if(pictures.Bytes!=0)throw new Exception("Pictures retained outside Instarseed");}
    void OnDestroy(){pictures.Retain(false);if(measure)Destroy(measure.gameObject);if(holder)Destroy(holder);}
    public void CheckScroll(float normalized){if(scroll){Canvas.ForceUpdateCanvases();scroll.verticalNormalizedPosition=normalized;Canvas.ForceUpdateCanvases();Refresh();if(LiveRows>12)throw new Exception("Instarseed virtualization failed");if(normalized==0)CheckBottom();}}
    void CheckBottom(){
        if(rows.Count==0)return;var last=rows.Last();if(!last.go)throw new Exception("Last Instarseed row not instantiated");
        var nav=(RectTransform)shell.transform.Find("Center/HUD_Btm/Layout");
        var vr=scroll.viewport;var lastRect=(RectTransform)last.go.transform;
        float navTop=stage.InverseTransformPoint(nav.TransformPoint(new Vector3(0,nav.rect.yMax,0))).y;
        float viewportBottom=stage.InverseTransformPoint(vr.TransformPoint(new Vector3(0,vr.rect.yMin,0))).y;
        float rowBottom=stage.InverseTransformPoint(lastRect.TransformPoint(new Vector3(0,lastRect.rect.yMin,0))).y;
        if(viewportBottom<navTop-.1f||rowBottom<viewportBottom-.1f)throw new Exception("Instarseed bottom occluded: "+current.name+" nav="+navTop+" viewport="+viewportBottom+" row="+rowBottom);
        int generated=scroll.transform.Cast<Transform>().Count(t=>t.name=="Viewer viewport");if(generated>1)throw new Exception("Instarseed viewport accumulated");
        Debug.Log("INSTARSEED_SCROLL_END_OK page="+current.name+" rows="+rows.Count+" viewport="+viewportBottom+" row="+rowBottom+" nav="+navTop);
    }
    public void CheckWheelBottom(){
        Canvas.ForceUpdateCanvases();scroll.verticalNormalizedPosition=1;
        var ev=new UnityEngine.EventSystems.PointerEventData(UnityEngine.EventSystems.EventSystem.current){scrollDelta=new Vector2(0,-10)};
        for(int i=0;i<100;i++){scroll.OnScroll(ev);Canvas.ForceUpdateCanvases();Refresh();}CheckBottom();
        Debug.Log("INSTARSEED_WHEEL_END_OK");
    }

}
public sealed class InstarseedRowAssets:MonoBehaviour {
    public readonly List<UnityEngine.Object> assets=new List<UnityEngine.Object>();
    public readonly List<Action> releases=new List<Action>();
    public void Release(){foreach(var release in releases)release();releases.Clear();foreach(var a in assets)if(a)Destroy(a);assets.Clear();}
    void OnDestroy(){Release();}
}

#endif
