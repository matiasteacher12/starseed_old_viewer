#if UNITY_EDITOR || VIEWER_BACKEND
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;
using UnityEngine.TextCore;

// Viewer-owned navigation. Content/IDs are from the old tables; no account API.
public sealed class InstarseedView : MonoBehaviour {
    [Serializable] public class Catalogue { public int schema; public Profile[] profiles; public Post[] posts; public Reply[] replies; public string[] images,videos; }
    [Serializable] public class Profile { public long id,characterId;public string name,icon,description;public bool ad; }
    [Serializable] public class Post { public long id,profile;public int ratio,type;public string media,thumbnail,text,femaleText;public bool special;public Caption[] captions; }
    [Serializable] public class Reply { public long id,author,post,parent;public int order;public string text,femaleText; }
    [Serializable] public class Caption { public float start,end;public string text; }
    [Serializable] class EmojiData {public EmojiCharacter[] characters;public EmojiGlyph[] glyphs;}
    [Serializable] class EmojiCharacter {public string name;public uint unicode,glyph;public float scale;}
    [Serializable] class EmojiGlyph {public uint index;public int x,y,width,height;public float scale;public float[] metrics;}
    TMP_SpriteAsset emoji; OriginalInstarseedUi native; GameObject nativeVideo;
    public Action<bool> VideoMode;
    RectTransform root,stage;TMP_FontAsset font;Catalogue data;OriginalStoryMenu story;
    readonly Dictionary<string,Texture2D> textures=new Dictionary<string,Texture2D>();
    readonly List<string> recent=new List<string>();
    VideoPlayer video;AudioSource audio;RawImage movie;TMP_Text subtitle,status;Post selected;
    long profile;int page;bool female;string screen="home";Coroutine mediaRoutine;
    readonly Color ink=new Color(.89f,.94f,.97f),muted=new Color(.58f,.68f,.75f),accent=new Color(.36f,.87f,.89f),panel=new Color(.09f,.14f,.19f);
    public bool Visible=>(root&&root.gameObject.activeSelf)||(native&&native.Visible)||nativeVideo;
    public void Initialize(RectTransform parent,TMP_FontAsset typeface,OriginalStoryMenu menu) {
        stage=parent;font=typeface;story=menu;
        root=Box(parent,"Viewer home",0,0,1280,720,new Color(.035f,.065f,.095f));
        video=gameObject.AddComponent<VideoPlayer>();audio=gameObject.AddComponent<AudioSource>();audio.volume=.6f;
        video.playOnAwake=false;video.isLooping=true;video.renderMode=VideoRenderMode.APIOnly;video.audioOutputMode=VideoAudioOutputMode.AudioSource;video.SetTargetAudioSource(0,audio);
        video.errorReceived+=(p,message)=>{if(status)status.text="영상을 불러오지 못했습니다.";Debug.LogError("INSTARSEED_VIDEO_ERROR "+message);};
        story.ReturnToViewer=ShowHome;story.Shown+=Hide;
        ShowHome();
    }
    void EnsureData(){if(data!=null)return;data=BackendJson.FromJson<Catalogue>(File.ReadAllText(StoryContent.FilePath("Instarseed/catalog.json")));if(data.schema!=1)throw new InvalidDataException("Unsupported Instarseed catalog");
        var e=BackendJson.FromJson<EmojiData>(File.ReadAllText(StoryContent.FilePath("Instarseed/emoji.json")));var sheet=new Texture2D(2,2,TextureFormat.RGBA32,false);if(!sheet.LoadImage(File.ReadAllBytes(StoryContent.FilePath("Instarseed/emoji.png"))))throw new InvalidDataException("Emoji sheet");
        emoji=ScriptableObject.CreateInstance<TMP_SpriteAsset>();emoji.name="Emoji_Text";JsonUtility.FromJsonOverwrite("{\"m_Version\":\"1.1.0\"}",emoji);emoji.spriteSheet=sheet;emoji.material=new Material(Shader.Find("TextMeshPro/Sprite"));emoji.material.mainTexture=sheet;
        foreach(var g in e.glyphs)emoji.spriteGlyphTable.Add(new TMP_SpriteGlyph(g.index,new GlyphMetrics(g.metrics[0],g.metrics[1],g.metrics[2],g.metrics[3],g.metrics[4]),new GlyphRect(g.x,g.y,g.width,g.height),g.scale,0));
        foreach(var c in e.characters)emoji.spriteCharacterTable.Add(new TMP_SpriteCharacter(c.unicode,emoji,emoji.spriteGlyphTable.First(g=>g.index==c.glyph)){name=c.name,scale=c.scale});emoji.UpdateLookupTables();
        Debug.Log("INSTARSEED_EMOJI_OK characters="+e.characters.Length);Debug.Log("INSTARSEED_READY posts="+data.posts.Length+" profiles="+data.profiles.Length+" replies="+data.replies.Length);}
    void Clear(string name) {
        VideoMode?.Invoke(false); if(native)native.Hide();if(nativeVideo){nativeVideo.SetActive(false);Destroy(nativeVideo);nativeVideo=null;}
        if(mediaRoutine!=null){StopCoroutine(mediaRoutine);mediaRoutine=null;}video.Stop();selected=null;movie=null;subtitle=null;status=null;
        foreach(Transform child in root){child.gameObject.SetActive(false);Destroy(child.gameObject);}
        // Only this page's media remains live. No startup preload or accumulating feed cache.
        foreach(var texture in textures.Values)Destroy(texture);textures.Clear();recent.Clear();
        story.Hide();root.gameObject.SetActive(true);root.SetAsLastSibling();screen=name;
    }
    public void Hide(){if(mediaRoutine!=null){StopCoroutine(mediaRoutine);mediaRoutine=null;}video.Stop();root.gameObject.SetActive(false);if(native)native.Hide();if(nativeVideo){Destroy(nativeVideo);nativeVideo=null;}}
    public void ShowHome(){Clear("home");ViewerHomeMenu.Draw(root,font,()=>story.ShowHome(),()=>{profile=0;page=0;ShowFeed();});}
    void Header(string title,Action back){Button(root,"뒤로",32,22,126,44,back,panel,19);Label(root,title,184,23,840,42,28,ink);Button(root,"홈",1152,22,96,44,ShowHome,panel,19);Box(root,"Divider",32,82,1216,1,new Color(.18f,.27f,.33f));}
    void EnsureNative(){EnsureData();if(native)return;native=gameObject.AddComponent<OriginalInstarseedUi>();native.Initialize(stage,font,emoji,data,ShowHome);native.PlayVideo=ShowPost;}
    public void ShowFeed(){EnsureNative();Clear("feed");root.gameObject.SetActive(false);native.ShowFeed();}
    void ShowProfiles(){EnsureNative();Clear("profiles");root.gameObject.SetActive(false);native.ShowProfiles();}
    public void ShowPost(Post post){EnsureNative();Clear("post");root.gameObject.SetActive(false);selected=post;
        if(post.type==0){native.ShowFeed(false,post);return;}
        nativeVideo=native.VideoPage();var image=nativeVideo.transform.Find("Center/SafeArea/Scaler/Image");
        var old=image.GetComponent<Image>();if(old)old.enabled=false;var surface=new GameObject("Decoded movie",typeof(RectTransform),typeof(RawImage));surface.transform.SetParent(image,false);movie=surface.GetComponent<RawImage>();movie.color=Color.white;movie.material=null;
        var rt=movie.rectTransform;rt.anchorMin=rt.anchorMax=rt.pivot=new Vector2(.5f,.5f);rt.anchoredPosition=Vector2.zero;rt.sizeDelta=new Vector2(1280,720);
        status=Label(nativeVideo.transform,"영상 준비 중…",50,645,1080,40,18,Color.white,TextAlignmentOptions.Center);status.gameObject.AddComponent<Shadow>();
        subtitle=Label(nativeVideo.transform,"",120,545,1040,80,26,Color.white,TextAlignmentOptions.Center);subtitle.gameObject.AddComponent<Shadow>();
        var close=nativeVideo.transform.Find("Center/Btm/Menu/Button_Close").GetComponent<Button>();close.interactable=true;close.onClick.AddListener(()=>{ShowFeed();native.ShowFeed(false,post);});
        VideoMode?.Invoke(true);mediaRoutine=StartCoroutine(PlayMovie(post));
    }
    string TextVariant(string normal,string alternate)=>female&&!string.IsNullOrEmpty(alternate)?alternate:normal;
    static string Plain(string text)=>Regex.Replace(text??"","<[^>]+>","").Replace('\n',' ');
    static string Display(string text)=>text??"";
    float Paragraph(Transform parent,string text,float x,float y,float width,float size){var label=Label(parent,Display(text),x,y,width,32,size,ink);float height=Mathf.Max(32,label.GetPreferredValues(Display(text),width,10000).y+8);label.rectTransform.sizeDelta=new Vector2(width,height);return height;}
    IEnumerator PlayMovie(Post post){yield return StoryContent.VerifyAsync(post.media);video.url=StoryContent.FilePath(post.media);video.Prepare();float deadline=Time.realtimeSinceStartup+30;
        while(!video.isPrepared&&Time.realtimeSinceStartup<deadline)yield return null;
        if(!video.isPrepared){status.text="영상을 불러오지 못했습니다.";yield break;}video.Play();status.text="클릭: 일시 정지 / 재생";
        movie.raycastTarget=true;var button=movie.gameObject.AddComponent<Button>();button.targetGraphic=movie;button.onClick.AddListener(()=>{if(video.isPlaying)video.Pause();else video.Play();});
        while(selected==post){if(video.texture){movie.texture=video.texture;float scale=Mathf.Min(1280f/video.texture.width,720f/video.texture.height);movie.rectTransform.sizeDelta=new Vector2(video.texture.width*scale,video.texture.height*scale);movie.rectTransform.anchoredPosition=Vector2.zero;}var caption=(post.captions??new Caption[0]).FirstOrDefault(c=>video.time>=c.start&&video.time<c.end);subtitle.text=caption?.text??"";yield return null;}
    }
    void Update(){if(!Visible)return;if(Input.GetKeyDown(KeyCode.Escape)||Input.GetKeyDown(KeyCode.Backspace)){if(screen=="home")return;if(native&&native.Visible)native.Back();else ShowFeed();}}
    RectTransform Box(Transform parent,string name,float x,float y,float w,float h,Color color){var rt=Rect(parent,name,x,y,w,h);rt.gameObject.AddComponent<Image>().color=color;return rt;}
    static RectTransform Rect(Transform parent,string name,float x,float y,float w,float h){var rt=new GameObject(name,typeof(RectTransform)).GetComponent<RectTransform>();rt.SetParent(parent,false);rt.anchorMin=rt.anchorMax=new Vector2(0,1);rt.pivot=new Vector2(0,1);rt.anchoredPosition=new Vector2(x,-y);rt.sizeDelta=new Vector2(w,h);return rt;}
    TMP_Text Label(Transform parent,string value,float x,float y,float w,float h,float size,Color color,TextAlignmentOptions align=TextAlignmentOptions.TopLeft){var rt=Rect(parent,"Label",x,y,w,h);rt.gameObject.SetActive(false);var t=rt.gameObject.AddComponent<TextMeshProUGUI>();t.font=font;if(emoji)t.spriteAsset=emoji;t.fontSize=size;t.color=color;t.text=value;t.alignment=align;t.raycastTarget=false;t.overflowMode=TextOverflowModes.Ellipsis;rt.gameObject.SetActive(true);return t;}
    RectTransform Button(Transform parent,string value,float x,float y,float w,float h,Action action,Color color,float size){var rt=Box(parent,"Button "+value,x,y,w,h,color);var b=rt.gameObject.AddComponent<Button>();b.targetGraphic=rt.GetComponent<Image>();b.navigation=new Navigation{mode=Navigation.Mode.None};b.onClick.AddListener(()=>action());Label(rt,value,12,0,w-24,h,size,ink,TextAlignmentOptions.Center);return rt;}
    RawImage Picture(Transform parent,string path,float x,float y,float w,float h){var rt=Rect(parent,"Image",x,y,w,h);var img=rt.gameObject.AddComponent<RawImage>();img.raycastTarget=false;
        if(!string.IsNullOrEmpty(path)){if(!textures.TryGetValue(path,out var t)){t=new Texture2D(2,2,TextureFormat.RGBA32,false);if(!t.LoadImage(File.ReadAllBytes(StoryContent.FilePath(path))))throw new InvalidDataException("Invalid image "+path);textures[path]=t;}img.texture=t;Fit(img,w,h);}else img.color=Color.clear;return img;}
    static void Fit(RawImage image,float w,float h){if(!image.texture)return;float s=Mathf.Min(w/image.texture.width,h/image.texture.height);var rt=image.rectTransform;var old=rt.sizeDelta;var size=new Vector2(image.texture.width*s,image.texture.height*s);rt.anchoredPosition+=new Vector2((old.x-size.x)/2,-(old.y-size.y)/2);rt.sizeDelta=size;}
    RectTransform Scroll(Transform parent,float x,float y,float w,float h){var viewport=Box(parent,"Scroll",x,y,w,h,Color.clear);viewport.gameObject.AddComponent<RectMask2D>();var scroll=viewport.gameObject.AddComponent<ScrollRect>();scroll.horizontal=false;scroll.movementType=ScrollRect.MovementType.Clamped;scroll.scrollSensitivity=34;var content=Rect(viewport,"Content",0,0,w-16,h);content.anchorMax=new Vector2(1,1);content.sizeDelta=new Vector2(-16,h);scroll.viewport=viewport;scroll.content=content;return content;}
    public IEnumerator Check(){var initialTimer=System.Diagnostics.Stopwatch.StartNew();EnsureData();
        var camera=new GameObject("Instarseed check camera",typeof(Camera)).GetComponent<Camera>();camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.black;camera.orthographic=true;camera.orthographicSize=360;camera.transform.position=new Vector3(0,0,-10);
        var target=new RenderTexture(1280,720,24);target.Create();camera.targetTexture=target;var canvas=stage.GetComponentInParent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=camera;canvas.planeDistance=1;
        var directory=Path.GetFullPath(Path.Combine(Application.dataPath,"../../RenderChecks"));Directory.CreateDirectory(directory);
        Action<string> capture=name=>{Canvas.ForceUpdateCanvases();camera.Render();RenderTexture.active=target;var texture=new Texture2D(1280,720,TextureFormat.RGBA32,false);texture.ReadPixels(new Rect(0,0,1280,720),0,0);texture.Apply();File.WriteAllBytes(Path.Combine(directory,name+".png"),texture.EncodeToPNG());RenderTexture.active=null;Destroy(texture);};
        if(Environment.GetCommandLineArgs().Contains("-instarseed-perf-check")){
            ShowFeed();initialTimer.Stop();yield return new WaitForSecondsRealtime(.25f);capture("perf-feed");
            Debug.Log("INSTARSEED_PERF initialMs="+initialTimer.Elapsed.TotalMilliseconds.ToString("F2",System.Globalization.CultureInfo.InvariantCulture)+" "+native.PerformanceState());
            var repro=data.posts.First(p=>p.id==1022236);
            var actions=new Action[]{()=>native.ShowFeed(),()=>native.ShowFeed(true),()=>native.ShowProfiles(),()=>native.ShowComments(repro),()=>native.ShowProfile(data.profiles.First(p=>data.posts.Any(x=>x.profile==p.id)))};
            var names=new[]{"home","special","profiles","comments","profile"};
            for(int pass=0;pass<4;pass++)for(int i=0;i<actions.Length;i++){
                int before=native.PictureDecodes,measured=native.Measurements;var timer=System.Diagnostics.Stopwatch.StartNew();actions[i]();Canvas.ForceUpdateCanvases();timer.Stop();yield return null;native.CheckCache();
                Debug.Log("INSTARSEED_PERF pass="+pass+" page="+names[i]+" ms="+timer.Elapsed.TotalMilliseconds.ToString("F2",System.Globalization.CultureInfo.InvariantCulture)+" newDecodes="+(native.PictureDecodes-before)+" newMeasurements="+(native.Measurements-measured)+" "+native.PerformanceState());
                if(pass==3){yield return new WaitForSecondsRealtime(.25f);capture("perf-"+names[i]);}
            }
            native.ShowFeed();for(int i=0;i<=160;i++){native.CheckScroll(1-i/160f);yield return null;native.CheckCache();}
            for(int i=160;i>=0;i--){native.CheckScroll(1-i/160f);yield return null;native.CheckCache();}
            native.ShowComments(repro);yield return null;native.CheckScroll(0);yield return new WaitForSecondsRealtime(.25f);capture("perf-comments-bottom");
            Debug.Log("INSTARSEED_PERF stress "+native.PerformanceState());ShowHome();yield return null;native.CheckCacheEmpty();
            Debug.Log("INSTARSEED_PERF_CHECK_OK cacheReleased=true");Application.Quit(0);yield break;
        }
        if(Environment.GetCommandLineArgs().Contains("-instarseed-tabs-check")){
            ShowFeed();yield return null;yield return native.CheckTabs(capture);native.CheckNavigation();ShowHome();
            Debug.Log("INSTARSEED_TABS_CHECK_OK");Application.Quit(0);yield break;
        }
        if(Environment.GetCommandLineArgs().Contains("-instarseed-scroll-check")){
            ShowFeed();yield return null;
            var repro=data.posts.First(p=>p.id==1022236);native.ShowComments(repro);yield return null;native.CheckWheelBottom();yield return null;capture("comments-1022236-bottom");
            foreach(var post in data.posts){native.ShowComments(post);yield return null;native.CheckScroll(0);yield return null;}
            foreach(var person in data.profiles){native.ShowProfile(person);yield return null;native.CheckScroll(0);yield return null;}
            ShowProfiles();yield return null;native.CheckScroll(0);yield return null;capture("profiles-bottom");
            ShowFeed();yield return null;native.CheckScroll(0);yield return null;capture("feed-bottom");
            native.ShowFeed(true);yield return null;native.CheckScroll(0);yield return null;
            native.ShowComments(repro);yield return null;native.CheckWheelBottom();yield return null;capture("comments-1022236-reopen-bottom");
            Debug.Log("INSTARSEED_SCROLL_CHECK_OK comments=236 profiles=75 wheel=true reopen=true");Application.Quit(0);yield break;
        }
        ShowHome();yield return null;capture("viewer-home");ShowFeed();yield return null;capture("instarseed-feed");native.CheckNavigation();ShowFeed();native.CheckScroll(.5f);yield return null;native.CheckScroll(0);yield return null;native.CheckScroll(1);ShowProfiles();yield return null;capture("instarseed-profiles");native.CheckScroll(0);yield return null;native.ShowProfile(data.profiles.First(p=>data.posts.Any(x=>x.profile==p.id)));yield return null;capture("instarseed-profile");foreach(var person in data.profiles){native.ShowProfile(person);yield return null;native.CheckScroll(0);if(person.name=="탈리아페")capture("instarseed-long-profile");}Debug.Log("INSTARSEED_NATIVE_PROFILES_OK count="+data.profiles.Length);native.ShowFeed(true);yield return null;capture("instarseed-special");
        foreach(var path in data.images){var t=new Texture2D(2,2);if(!t.LoadImage(File.ReadAllBytes(StoryContent.FilePath(path))))throw new InvalidDataException(path);Destroy(t);yield return null;}
        foreach(var post in data.posts){ShowPost(post);yield return null;if(post==data.posts.First(p=>p.type==0)){capture("instarseed-post");native.CheckScroll(0);yield return null;capture("instarseed-post-body");}if(post.type==0){native.ShowComments(post);yield return null;if(post==data.posts.First(p=>p.type==0))capture("instarseed-comments");native.CheckScroll(0);yield return null;}
            if(post.type==1){float deadline=Time.realtimeSinceStartup+40;while((!video.isPrepared||video.frame<1)&&Time.realtimeSinceStartup<deadline)yield return null;if(!video.isPrepared||video.frame<1)throw new Exception("Video did not decode "+post.id);capture("instarseed-video-"+post.id);
                var control=movie.GetComponent<Button>();if(!control||!movie.raycastTarget)throw new Exception("Video control unavailable");control.onClick.Invoke();yield return null;if(video.isPlaying)throw new Exception("Video pause failed");control.onClick.Invoke();yield return null;if(!video.isPlaying)throw new Exception("Video resume failed");
                Debug.Log("INSTARSEED_VIDEO_OK "+post.media+" frame="+video.frame+" pauseResume=true");nativeVideo.transform.Find("Center/Btm/Menu/Button_Close").GetComponent<Button>().onClick.Invoke();if(nativeVideo||!native.Visible)throw new Exception("Video close failed");native.ShowComments(post);yield return null;native.CheckScroll(0);}}
        ShowHome();story.ShowHome();if(Visible||!story.Visible)throw new Exception("Story navigation failed");story.Back();if(!Visible||story.Visible)throw new Exception("Home return failed");
        Debug.Log("INSTARSEED_CHECK_OK posts="+data.posts.Length+" images="+data.images.Length+" videos="+data.videos.Length+" replies="+data.replies.Length+" homeStoryReturn=true");Application.Quit(0);
    }
}


#endif
