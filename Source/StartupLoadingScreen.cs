using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;
using UnityEngine.EventSystems;

// Visual layout from preserved Windows Root_Intro. No login or game scripts.
public sealed class StartupLoadingScreen:MonoBehaviour {
    [Serializable] class Layout {public Node[] nodes;public string poster,movie,music;}
    [Serializable] class Node {public string path;public bool active,mask;public Vector2 anchorMin,anchorMax,position,size,pivot;public Vector3 scale;public Quaternion rotation;public Picture image;public Caption text;public DropShadow shadow;}
    [Serializable] class Picture {public Color color;public int type,fillMethod,fillOrigin;public bool fillClockwise,fillCenter,preserveAspect;public string file;public Vector4 border;public float ppu;}
    [Serializable] class Caption {public string value,font;public int size,style,alignment;public float spacing;public Color color;}
    [Serializable] class DropShadow {public Color color;public Vector2 distance;}
    AssetBundle assets;Canvas canvas;Camera clear;RawImage movieImage;VideoPlayer movie;Image bar;MText percent;
    readonly List<Sprite> sprites=new List<Sprite>();
    readonly Dictionary<string,RectTransform> nodes=new Dictionary<string,RectTransform>();
    float progress;bool finished;string moviePath,assetPrefix;MText downloadText;Image downloadBar;GameObject downloadRoot,loadingRoot;
    public static int Shows {get;private set;}
    public bool MovieDecoded=>movie&&movie.isPrepared&&movie.frame>=0;
    bool movieStarted;AudioSource introMusic;
    Button startButton;CanvasGroup startHint;bool waitingForStart,startArmed;int startRequests;
    public IEnumerator Prepare(string localBundle=null){
        if(localBundle==null)yield return StoryContent.VerifyAsync("Startup/startup.bundle");
        var request=AssetBundle.LoadFromFileAsync(localBundle??StoryContent.FilePath("Startup/startup.bundle"));yield return request;assets=request.assetBundle;
        if(!assets)throw new InvalidDataException("Startup UI missing");
        assetPrefix=assets.GetAllAssetNames().Single(n=>n.EndsWith("/layout.json")).Replace("layout.json","");
        var data=JsonUtility.FromJson<Layout>(Load<TextAsset>("layout.json").text);moviePath=data.movie;
        canvas=new GameObject("Original login loading",typeof(Canvas),typeof(CanvasScaler),typeof(GraphicRaycaster)).GetComponent<Canvas>();
        canvas.renderMode=RenderMode.ScreenSpaceOverlay;canvas.sortingOrder=30000;
        var scaler=canvas.GetComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(1280,720);scaler.screenMatchMode=CanvasScaler.ScreenMatchMode.Expand;
        clear=new GameObject("Startup clear",typeof(Camera)).GetComponent<Camera>();clear.cullingMask=0;clear.depth=-200;clear.clearFlags=CameraClearFlags.SolidColor;clear.backgroundColor=Color.black;
        var stage=new GameObject("Stage",typeof(RectTransform)).GetComponent<RectTransform>();stage.SetParent(canvas.transform,false);stage.anchorMin=stage.anchorMax=stage.pivot=new Vector2(.5f,.5f);stage.sizeDelta=new Vector2(1280,720);
        foreach(var n in data.nodes){
            var rt=new GameObject(Path.GetFileName(n.path),typeof(RectTransform)).GetComponent<RectTransform>();
            string parent=n.path.Substring(0,n.path.LastIndexOf('/'));rt.SetParent(nodes.TryGetValue(parent,out var p)?p:stage,false);
            rt.anchorMin=n.anchorMin;rt.anchorMax=n.anchorMax;rt.pivot=n.pivot;rt.anchoredPosition=n.position;rt.sizeDelta=n.size;rt.localScale=n.scale;rt.localRotation=n.rotation;
            // Duplicate original decoration names are harmless; only unique control paths are queried.
            nodes[n.path]=rt;
            if(n.image!=null&&n.image.ppu>0){var image=rt.gameObject.AddComponent<MImage>();var v=n.image;image.color=v.color;image.raycastTarget=false;image.type=(Image.Type)v.type;image.fillMethod=(Image.FillMethod)v.fillMethod;image.fillOrigin=v.fillOrigin;image.fillClockwise=v.fillClockwise;image.fillCenter=v.fillCenter;image.preserveAspect=v.preserveAspect;
                if(!string.IsNullOrEmpty(v.file)){var t=Load<Texture2D>(v.file);var sprite=Sprite.Create(t,new Rect(0,0,t.width,t.height),new Vector2(.5f,.5f),v.ppu,0,SpriteMeshType.FullRect,v.border);sprites.Add(sprite);image.sprite=sprite;}
                if(n.path.EndsWith("/DownloadGage/Gage")){if(n.path.Contains("/DownLoad/"))downloadBar=image;else bar=image;}
            }
            if(n.text!=null&&!string.IsNullOrEmpty(n.text.font)){var v=n.text;var text=rt.gameObject.AddComponent<MText>();text.font=Load<Font>(v.font);text.fontSize=v.size;text.fontStyle=(FontStyle)v.style;text.alignment=(TextAnchor)v.alignment;text.spacing=v.spacing;text.color=v.color;text.text=v.value;text.raycastTarget=false;text.horizontalOverflow=HorizontalWrapMode.Overflow;text.resizeTextForBestFit=true;text.resizeTextMinSize=n.path.EndsWith("/Info/TextNum")?2:1;text.resizeTextMaxSize=v.size;if(n.path.EndsWith("/Info/TextNum"))percent=text;if(n.path.EndsWith("/DownLoad/Download/Text"))downloadText=text;}
            if(n.mask)rt.gameObject.AddComponent<Mask>().showMaskGraphic=false;
            if(n.shadow!=null&&n.shadow.color.a>0){var shadow=rt.gameObject.AddComponent<Shadow>();shadow.effectColor=n.shadow.color;shadow.effectDistance=n.shadow.distance;}
            rt.gameObject.SetActive(n.active);
            if(n.path.EndsWith("/SafeArea/DownLoad"))downloadRoot=rt.gameObject;
            if(n.path.EndsWith("/SafeArea/Loading"))loadingRoot=rt.gameObject;
        }
        // Viewer branding follows the logo, independently of the original
        // bottom copyright slot retained in the immutable layout bundle.
        var title=nodes["/Root_Intro/Contens/Copyright/Copyright"].GetComponent<MText>();
        if(title&&title.text=="Starseed Old Story Viewer"){
            var rt=title.rectTransform;rt.SetParent(nodes["/Root_Intro/Logo/Img_logo"],false);
            rt.anchorMin=rt.anchorMax=new Vector2(.5f,0);rt.pivot=new Vector2(.5f,1);
            rt.anchoredPosition=Vector2.zero;rt.sizeDelta=new Vector2(654,42);rt.localScale=Vector3.one;
            title.resizeTextForBestFit=false;title.fontSize=30;title.alignment=TextAnchor.MiddleCenter;
        }
        var surface=nodes["/Root_Intro/Image"];surface.GetComponent<Image>().enabled=false;
        var imageGo=new GameObject("Original intro movie",typeof(RectTransform),typeof(RawImage));imageGo.transform.SetParent(surface,false);
        var imgRt=(RectTransform)imageGo.transform;imgRt.anchorMin=Vector2.zero;imgRt.anchorMax=Vector2.one;imgRt.offsetMin=imgRt.offsetMax=Vector2.zero;
        movieImage=imageGo.GetComponent<RawImage>();movieImage.texture=Load<Texture2D>(data.poster);movieImage.raycastTarget=false;
        if(!string.IsNullOrEmpty(data.music)){
            introMusic=gameObject.AddComponent<AudioSource>();introMusic.playOnAwake=false;introMusic.loop=true;introMusic.volume=.3f;introMusic.mute=Application.isBatchMode;
            introMusic.clip=Load<AudioClip>(data.music);introMusic.Play();Debug.Log("STARTUP_MUSIC_STARTED clip="+data.music+" seconds="+introMusic.clip.length);
        }
        if(!bar||!percent)throw new Exception("Original startup progress controls missing");
        DownloadMode(false);Shows++;if(Shows!=1)throw new Exception("Startup screen repeated");SetProgress(0,"bootstrap");
        Debug.Log("STARTUP_UI_SHOWN nodes="+data.nodes.Length+" beforeCommonResources=true");
        yield return null;Capture("startup-000");
        if(localBundle==null)BeginMovie();
    }
    T Load<T>(string file) where T:UnityEngine.Object {var value=assets.LoadAsset<T>(assetPrefix+file.ToLowerInvariant());if(!value)throw new InvalidDataException(file);return value;}
    public void BeginMovie(string verifiedPath=null){if(movieStarted||finished)return;movieStarted=true;StartCoroutine(PlayMovie(verifiedPath));}
    public void DownloadMode(bool active){if(downloadRoot)downloadRoot.SetActive(active);if(loadingRoot)loadingRoot.SetActive(!active);}
    public void DownloadProgress(long done,long total,string phase){
        DownloadMode(true);float value=total>0?Mathf.Clamp01((float)((double)done/total)):0;
        if(downloadBar)downloadBar.fillAmount=value;
        if(downloadText)downloadText.text=phase+"  "+Mathf.FloorToInt(value*100)+"%"+(total>1024?"  ("+(done/1048576d).ToString("F1")+" / "+(total/1048576d).ToString("F1")+" MB)":"");
    }
    public void DownloadError(){DownloadMode(true);if(downloadText)downloadText.text="다운로드를 완료하지 못했습니다. 연결·저장 공간을 확인한 후 Enter로 다시 시도 / Esc로 종료";}
    IEnumerator PlayMovie(string verifiedPath){
        if(verifiedPath==null){yield return StoryContent.VerifyAsync(moviePath);StoryContent.SaveVerification();verifiedPath=StoryContent.FilePath(moviePath);}
        movie=gameObject.AddComponent<VideoPlayer>();movie.playOnAwake=false;movie.audioOutputMode=VideoAudioOutputMode.None;movie.renderMode=VideoRenderMode.APIOnly;movie.isLooping=true;movie.waitForFirstFrame=true;movie.url=verifiedPath;
        movie.errorReceived+=(player,error)=>Debug.LogWarning("STARTUP_MOVIE_FALLBACK "+error);
        movie.Prepare();float deadline=Time.realtimeSinceStartup+15;
        while(!movie.isPrepared&&Time.realtimeSinceStartup<deadline)yield return null;
        if(!movie.isPrepared)yield break;movie.Play();
        bool logged=false;while(!finished){if(movie.texture){movieImage.texture=movie.texture;if(!logged){logged=true;Debug.Log("STARTUP_MOVIE_DECODED width="+movie.texture.width+" height="+movie.texture.height);}}yield return null;}
    }
    public void SetProgress(float value,string phase){
        if(value<progress-.0001f||value>1.0001f)throw new Exception("Invalid startup progress");progress=Mathf.Clamp01(value);
        if(bar)bar.fillAmount=progress;if(percent)percent.text=Mathf.FloorToInt(progress*100)+"%";
        Debug.Log("STARTUP_PROGRESS value="+progress.ToString("F4",System.Globalization.CultureInfo.InvariantCulture)+" phase="+phase);
    }
    public IEnumerator CheckDownloadMedia(){
        float deadline=Time.realtimeSinceStartup+12;
        while((!MovieDecoded||movie.frame<10||!introMusic||introMusic.timeSamples<1000)&&Time.realtimeSinceStartup<deadline)yield return null;
        if(!MovieDecoded||!introMusic||!introMusic.isPlaying)throw new Exception("Intro media did not start during data verification");
        long frame=movie.frame;int sample=introMusic.timeSamples;
        Capture("startup077-before");yield return new WaitForSecondsRealtime(1);
        Capture("startup077-after");
        if(movie.frame<=frame||introMusic.timeSamples<=sample||!downloadRoot.activeSelf)throw new Exception("Intro media stalled during data verification");
        var title=nodes["/Root_Intro/Contens/Copyright/Copyright"].GetComponent<MText>();
        if(title.text!="Starseed Old Story Viewer"||title.font!=percent.font||title.GetComponent<Image>())throw new Exception("Startup title replacement failed");
        if(FindObjectsOfType<AudioListener>().Count(a=>a.enabled)!=1)throw new Exception("Multiple audio listeners");
        Debug.Log("STARTUP_MEDIA_CHECK_OK beforeInstall=true frameFrom="+frame+" frameTo="+movie.frame+" sampleFrom="+sample+" sampleTo="+introMusic.timeSamples+" titleFont="+title.font.name+" video="+movie.width+"x"+movie.height);
    }
    public IEnumerator Complete(){
        SetProgress(1,"ready");yield return null;Capture("startup-100");
        if(Environment.GetCommandLineArgs().Contains("-startup-ui-check")){
            float deadline=Time.realtimeSinceStartup+12;while(!MovieDecoded&&Time.realtimeSinceStartup<deadline)yield return null;
            if(!MovieDecoded)throw new Exception("Startup background movie did not decode");
            Debug.Log("STARTUP_UI_CHECK_OK shows="+Shows+" progress="+progress+" movie=true");
        }
        yield return WaitForStart();
        Close();yield return null;yield return null;ReleaseAssets();yield return null;Debug.Log("STARTUP_UI_CLOSED");
    }
    IEnumerator WaitForStart(){
        if(downloadRoot)downloadRoot.SetActive(false);if(loadingRoot)loadingRoot.SetActive(false);
        var input=new GameObject("Tap to start",typeof(RectTransform),typeof(Image),typeof(Button));
        var hit=(RectTransform)input.transform;hit.SetParent(canvas.transform,false);hit.anchorMin=Vector2.zero;hit.anchorMax=Vector2.one;hit.offsetMin=hit.offsetMax=Vector2.zero;
        input.GetComponent<Image>().color=Color.clear;
        startButton=input.GetComponent<Button>();startButton.transition=Selectable.Transition.None;startButton.interactable=false;startButton.onClick.AddListener(RequestStart);
        // Preserved Korean Root_Intro/Contens/Btn_Touch: 200x20, bottom +100,
        // box_basic white alpha .427, Number_Slim_EB size 15 / spacing 24.
        // Reuse the already loaded sprite/font; no additional asset payload.
        var hint=new GameObject("Start hint",typeof(RectTransform),typeof(Image),typeof(CanvasGroup));
        var rt=(RectTransform)hint.transform;rt.SetParent(nodes["/Root_Intro"],false);rt.SetAsLastSibling();
        rt.anchorMin=rt.anchorMax=new Vector2(.5f,0);rt.pivot=new Vector2(.5f,0);rt.anchoredPosition=new Vector2(0,100);rt.sizeDelta=new Vector2(200,20);
        var bg=hint.GetComponent<Image>();bg.sprite=bar.sprite;bg.type=Image.Type.Sliced;bg.color=new Color(1,1,1,.427451f);bg.raycastTarget=false;
        startHint=hint.GetComponent<CanvasGroup>();
        var label=new GameObject("Text",typeof(RectTransform),typeof(MText));var tr=(RectTransform)label.transform;tr.SetParent(rt,false);tr.anchorMin=Vector2.zero;tr.anchorMax=Vector2.one;tr.offsetMin=new Vector2(0,-1.5f);tr.offsetMax=new Vector2(0,-1.5f);
        var caption=label.GetComponent<MText>();caption.font=percent.font;caption.fontSize=15;caption.spacing=24;caption.alignment=TextAnchor.MiddleCenter;caption.color=new Color(0,0,0,.545098f);caption.text="- TAP TO START -";caption.raycastTarget=false;
        waitingForStart=true;startArmed=false;
        bool probe=Application.isBatchMode&&Environment.GetCommandLineArgs().Contains("-startup-tap-check");
        if(probe){startButton.onClick.Invoke();if(!waitingForStart||startRequests!=0)throw new Exception("Premature start accepted");}
        // A press held during loading cannot become a start request. Menu input
        // remains disabled until the subsequent click has been consumed.
        yield return null;
        while(Input.GetMouseButton(0)||Input.GetKey(KeyCode.Return)||Input.GetKey(KeyCode.KeypadEnter)||Input.GetKey(KeyCode.Space)||Input.touchCount>0)yield return null;
        startArmed=true;startButton.interactable=true;Debug.Log("STARTUP_TAP_READY");
        if(probe){
            float deadline=Time.realtimeSinceStartup+12;while(!MovieDecoded&&Time.realtimeSinceStartup<deadline)yield return null;
            if(!MovieDecoded||!introMusic||!introMusic.isPlaying)throw new Exception("Start screen media missing");
            long frame=movie.frame;int sample=introMusic.timeSamples;Capture("startup-tap-before");yield return new WaitForSecondsRealtime(2);Capture("startup-tap-after");
            if(!waitingForStart||startRequests!=0||movie.frame<=frame||introMusic.timeSamples<=sample)throw new Exception("Start gate auto-advanced or media stopped");
            var hits=new List<RaycastResult>();EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current){position=new Vector2(Screen.width*.5f,Screen.height*.15f)},hits);
            if(hits.Count==0||hits[0].gameObject!=input)throw new Exception("Start click is not routed to overlay");
            startButton.onClick.Invoke();startButton.onClick.Invoke();if(waitingForStart||startRequests!=1)throw new Exception("Start click was not consumed exactly once");
            Debug.Log("STARTUP_TAP_CHECK_OK wait=true media=true earlyIgnored=true raycast=true singleClick=true");
        } else if(Application.isBatchMode)RequestStart(); // Existing unrelated probes remain noninteractive.
        while(waitingForStart)yield return null;
        startButton.interactable=false;
        while(Input.GetMouseButton(0)||Input.GetKey(KeyCode.Return)||Input.GetKey(KeyCode.KeypadEnter)||Input.GetKey(KeyCode.Space)||Input.touchCount>0)yield return null;
        yield return null;Debug.Log("STARTUP_TAP_ACCEPTED");
    }
    void RequestStart(){if(!waitingForStart||!startArmed)return;startRequests++;waitingForStart=false;}
    void Update(){
        if(!waitingForStart)return;
        if(startHint)startHint.alpha=.8f+.2f*Mathf.Cos(Time.unscaledTime*2.5f);
        if(startArmed&&(Input.GetKeyDown(KeyCode.Return)||Input.GetKeyDown(KeyCode.KeypadEnter)||Input.GetKeyDown(KeyCode.Space)))RequestStart();
    }
    public void Capture(string name){
        if(!Application.isBatchMode||!Environment.GetCommandLineArgs().Contains("-startup-ui-check")||!canvas)return;
        var camera=new GameObject("Startup probe",typeof(Camera)).GetComponent<Camera>();camera.orthographic=true;camera.orthographicSize=360;camera.transform.position=new Vector3(0,0,-10);camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.black;
        var target=new RenderTexture(1280,720,24);target.Create();camera.targetTexture=target;
        canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=camera;canvas.planeDistance=1;Canvas.ForceUpdateCanvases();camera.Render();
        var previous=RenderTexture.active;RenderTexture.active=target;var texture=new Texture2D(1280,720,TextureFormat.RGBA32,false);texture.ReadPixels(new Rect(0,0,1280,720),0,0);texture.Apply();
        string directory=Path.GetFullPath(Path.Combine(Application.dataPath,"../../RenderChecks"));Directory.CreateDirectory(directory);File.WriteAllBytes(Path.Combine(directory,name+".png"),texture.EncodeToPNG());
        RenderTexture.active=previous;canvas.renderMode=RenderMode.ScreenSpaceOverlay;canvas.worldCamera=null;camera.targetTexture=null;target.Release();Destroy(target);Destroy(texture);Destroy(camera.gameObject);
    }
    public void StopMusic(){if(introMusic){introMusic.Stop();Destroy(introMusic);introMusic=null;}}
    public void Close(){finished=true;StopAllCoroutines();StopMusic();if(movie){movie.Stop();movie.targetTexture=null;movieImage.texture=null;Destroy(movie);movie=null;}if(canvas){canvas.gameObject.SetActive(false);Destroy(canvas.gameObject);canvas=null;}if(clear){Destroy(clear.gameObject);clear=null;}foreach(var sprite in sprites)Destroy(sprite);sprites.Clear();}
    void ReleaseAssets(){if(assets){assets.Unload(true);assets=null;}}
    void OnDestroy(){Close();ReleaseAssets();}
}
