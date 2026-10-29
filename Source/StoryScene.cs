using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;
using TMPro;
using UnityEngine.U2D;

// A deliberately small player for Chapter1_3_001 and its first choice.
// Actor, controller, clips, materials and compiled shader come from the original
// Windows bundles. No code from the installed game is executed.
public sealed partial class StoryScene : MonoBehaviour
{
    [Serializable] public class ContinuationActor {
        public string asset, expression; public Vector3 position;
        public float expressionWeight, brightness, rotationY;
    }
    [Serializable] public class Config {
        public string[] bundles; public string characterBundle, characterAsset, headBoneName;
        public string uiBundle, uiAsset, logAsset, summaryAsset, touchBundle, touchAsset, atlasBundle, storyTitle, storySummary;
        public Vector3 characterPosition, cameraPosition;
        public float fieldOfView; public string firstLine; public string[] choices;
        public float dialogueCharDelay = .025f, dialogueEndWait = .3f;
        public string followupLine, followupSpeaker;
        public float followupCharDelay = .025f, followupEndWait = .3f;
        public ContinuationActor[] followupActors;
    }
    Config config;
    Font font, keyFont;
    RectTransform stage;
    GameObject dialogue, choices, dim, logPanel, previewEnd;
    Text loading;
    StartupLoadingScreen startup;
    TMP_Text body;
    OriginalStoryUi originalUi;
    OriginalStoryMenu storyMenu;
    InstarseedView viewerHome;
    OriginalDialogueTiming dialogueTiming;
    float dialogueHoldElapsed;
    string currentLine;
    bool followingUp;
    GameObject continuation;
    AudioClip firstVoice, followupVoice;
    bool wasModal;
    Animator actorAnimator;
    SkinnedMeshRenderer face;
    GameObject actor;
    Transform headBone;
    Camera actorCamera;
    RenderTexture renderTexture;
    RawImage actorImage, backgroundImage;
    Material backgroundMaterial, cameraCompositeMaterial;
    Coroutine replayRoutine, focusRoutine;
    bool establishing, previewComplete;
    float focusAmount;
    AudioSource voice, music;
    readonly List<AssetBundle> bundles = new List<AssetBundle>();
    readonly Dictionary<string, Sprite> sprites = new Dictionary<string, Sprite>();
    readonly List<Material> actorMaterials = new List<Material>();
    bool ready, showingChoices, hidden;
    float started, blinkAt = 3, blinkStart = -10;
    int angryIndex = -1, blinkIndex = -1, mouthIndex = -1;
    float[] audioSamples = new float[256];
    float volume = .03f;
    bool muted;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot() { if (!FindObjectOfType<StoryScene>()) new GameObject("Airen story").AddComponent<StoryScene>(); }

    IEnumerator Start()
    {
        Application.targetFrameRate = 60;
        StoryAudioPreferences.Load(out muted,out volume);AudioListener.volume=muted?0:volume;
        gameObject.AddComponent<AudioListener>();
        CheckAudioPreferences();
        var downloader=gameObject.AddComponent<StoryDownloadBootstrap>();Exception downloadFailure=null;
        yield return CatchLoadingFailure(downloader.Run(),e=>downloadFailure=e);
        if(downloadFailure!=null){Debug.LogError("ASSET_DOWNLOAD_SETUP_ERROR "+downloadFailure.Message);if(Application.isBatchMode)Application.Quit(1);else gameObject.AddComponent<StoryContentError>().Message=downloadFailure.Message;yield break;}
        if(downloader.Configured&&!downloader.Installed)yield break;
        if(Application.isBatchMode&&Environment.GetCommandLineArgs().Contains("-download-only-check"))yield break;
        startup=downloader.Screen;
        try {StoryContent.Initialize(false);}
        catch(Exception error) {
            Debug.LogError("STORY_CONTENT_ERROR "+error.Message);
            if(Application.isBatchMode)Application.Quit(1);
            else gameObject.AddComponent<StoryContentError>().Message=error.Message;
            yield break;
        }
        if(Application.isBatchMode)
            Application.logMessageReceived+=(message,stack,type)=>{if(type==LogType.Exception)Application.Quit(1);};
        gameObject.AddComponent<StoryWindowAspect>();
        Exception startupFailure=null;
        if(startup)downloader.BeginMovie();
        if(!startup&&StoryContent.HasFile("Startup/startup.bundle")){
            startup=gameObject.AddComponent<StartupLoadingScreen>();
            yield return CatchLoadingFailure(startup.Prepare(),e=>startupFailure=e);
        }
        if(startupFailure==null)yield return CatchLoadingFailure(StoryContent.InitializeCommon(p=>{if(startup)startup.SetProgress(.05f+.3f*p,"common");}),e=>startupFailure=e);
        if(startupFailure!=null){if(startup)startup.Close();Debug.LogError("STORY_CONTENT_ERROR "+startupFailure.Message);if(Application.isBatchMode)Application.Quit(1);else gameObject.AddComponent<StoryContentError>().Message=startupFailure.Message;yield break;}
        if(startup){startup.SetProgress(.38f,"layout");startup.Capture("startup-038");yield return null;}
        config = JsonUtility.FromJson<Config>(StoryContent.Load<TextAsset>("scene-config").text);
        font = StoryContent.Load<Font>("Original/MFont_ko");
        keyFont = StoryContent.Load<Font>("Original/Number_Slim_EB");
        CreateStage();
        loading = Label(stage, "지역을 불러오는 중…", 22, new Rect(0,320,1280,80), TextAnchor.MiddleCenter);
        loadingCatalogue=JsonUtility.FromJson<LoadingCatalogue>(StoryContent.Load<TextAsset>("story-loading").text);
        Exception menuFailure=null;
        yield return CatchLoadingFailure(LoadStoryBundles(loadingCatalogue.menuBundles,p=>{if(startup)startup.SetProgress(.4f+.4f*p,"menu-bundles");}),e=>menuFailure=e);
        if(menuFailure!=null) {
            if(startup)startup.Close();
            Debug.LogError("STORY_CONTENT_ERROR "+menuFailure.Message);
            if(Application.isBatchMode)Application.Quit(1);
            else {loading.gameObject.SetActive(false);gameObject.AddComponent<StoryContentError>().Message=menuFailure.Message;}
            yield break;
        }
        SpriteAtlasManager.atlasRequested += LoadOriginalAtlas;
        if(startup){startup.SetProgress(.85f,"ui");startup.Capture("startup-085");yield return null;}
        actor=new GameObject("Legacy actor anchor");actor.transform.position=config.characterPosition;actor.SetActive(false);
        CreateCamera();
        bool uiReady = false;
        try { CreateUI(); uiReady = true; }
        catch (Exception error) {
            loading.text = "스토리 UI를 불러오지 못했습니다. 앱 로그를 확인해 주세요.";
            loading.transform.SetAsLastSibling();
            Debug.LogException(error);
        }
        if (!uiReady) {if(startup)startup.Close();yield break;}
        loading.gameObject.SetActive(false);
        if(startup)startup.StopMusic();
        music=gameObject.AddComponent<AudioSource>();music.loop=true;music.volume=.3f;music.mute=Application.isBatchMode;
        voice=gameObject.AddComponent<AudioSource>();voice.volume=1;
        // Log opening discards the current utterance instead of pausing it.
        // Later modal UnPause calls cannot resume an AudioSource stopped here.
        originalUi.LogOpened+=()=>voice.Stop();
        InitializeEpisode();
        if(startup){startup.SetProgress(.95f,"archive-menu");yield return null;}
        ready=true;
        if(StoryContent.Load<TextAsset>("story-menu") && !Environment.GetCommandLineArgs().Any(a=>a=="-story-verify"||a=="-old-story-check")) {
            storyMenu=gameObject.AddComponent<OriginalStoryMenu>();
            storyMenu.Initialize(stage,bundles.First(b=>b.GetAllAssetNames().Any(n=>n.EndsWith("/root_story.prefab",StringComparison.OrdinalIgnoreCase))),body.font);
            storyMenu.Shown+=PlayMenuMusic;
            storyMenu.PlayEpisode+=BeginSelectedEpisode;
            originalUi.ResetView();originalUi.Root.SetActive(false);backgroundImage.gameObject.SetActive(false);actorImage.gameObject.SetActive(false);music.Stop();storyMenu.ShowHome();
            if(StoryContent.HasFile("Instarseed/catalog.json")) {
                viewerHome=gameObject.AddComponent<InstarseedView>();viewerHome.Initialize(stage,body.font,storyMenu);
                viewerHome.VideoMode=active=>{if(active)music.Pause();else PlayMenuMusic();};
                if(Application.isBatchMode&&!Environment.GetCommandLineArgs().Contains("-instarseed-check"))storyMenu.ShowHome();
            }
        } else {yield return GuardEpisodeLoading(loadingCatalogue.episodes.First(p=>LoadOldCheckPlan()==null||p.scene==LoadOldCheckPlan().scene));}
        if(startup){yield return startup.Complete();Destroy(startup);startup=null;}
        Debug.Log("STORY_READY menuSeconds="+startupWatch.Elapsed.TotalSeconds.ToString("F3",System.Globalization.CultureInfo.InvariantCulture)+" actors="+episodeActors.Count+" bundles="+loadedBundles.Count+" hashedBytes="+StoryContent.HashedBytes+" hashes="+StoryContent.Hashes+" cacheHits="+StoryContent.CacheHits);
        downloader.Ready();
        if(Environment.GetCommandLineArgs().Contains("-startup-only-check")){Application.Quit(0);yield break;}
        if(Environment.GetCommandLineArgs().Contains("-instarseed-check"))StartCoroutine(viewerHome.Check());
        if(Environment.GetCommandLineArgs().Contains("-loading-smoke-check"))StartCoroutine(CheckDemandLoading());
        if(Environment.GetCommandLineArgs().Contains("-asset-store-media-check"))StartCoroutine(CheckAssetStoreMedia());
        if(Environment.GetCommandLineArgs().Contains("-all-main-check"))StartCoroutine(CheckAllMain());
        if(Environment.GetCommandLineArgs().Contains("-old-background-check"))StartCoroutine(CheckOldBackgrounds());
        if(Environment.GetCommandLineArgs().Contains("-old-bloom-check"))StartCoroutine(CheckOldBloom());
        if(Environment.GetCommandLineArgs().Contains("-old-story-check")) {
            Application.logMessageReceived+=(message,stack,type)=>{if(type==LogType.Exception)Application.Quit(1);};
            StartCoroutine(CheckOldEpisode());
        }
        if(Environment.GetCommandLineArgs().Any(a=>a=="-touch-only-probe"||a=="-story-progress-probe"||a=="-progress-reload-probe"||a=="-story-meeting-five-probe"||a=="-story-meeting-probe"||a=="-story-movie-stage-probe"||a=="-story-transparency-probe"||a=="-story-choice-transition-probe"||a=="-story-skip-probe"||a=="-story-film-probe"||a=="-story-timeline-probe"||a=="-story-awakening-probe"||a=="-story-actor-probe"||a=="-story-render-probe"||a=="-story-surface-probe"||a=="-story-settings-probe")) {
            Application.logMessageReceived+=(message,stack,type)=>{if(type==LogType.Exception)Application.Quit(1);};
            StartCoroutine(RenderStoryCheck());
        }
    }

    void SetMaterialDefaults(Material m, Transform owner = null)
    {
        // Replaces the original per-character component's serialized defaults.
        m.SetColor("_PerCharacterBaseColorTint",Color.white);
        m.SetColor("_PerCharacterOutlineColorTint",Color.white);
        m.SetColor("_PerCharacterOutlineColorLerp",Color.clear);
        m.SetFloat("_PerCharacterOutlineWidthMultiply",1);
        m.SetFloat("_PerCharacterRenderOutline",1);
        m.SetFloat("_DitherFadeoutAmount",0);
        m.SetColor("_PerCharEffectTintColor",Color.white);
        m.SetFloat("_DitherFadeoutNormalScaleFix",1);
        m.SetFloat("_FixFaceNormalAmount",1);
        m.SetFloat("_FixFaceNormalUseFlattenOrProxySphereMethod",0);
        m.SetVector("_FaceForwardDirection",Vector3.back);
        m.SetVector("_FaceUpDirection",Vector3.up);
        m.SetFloat("_CharacterBoundRadius",1.25f);
        m.SetVector("_CharacterBoundCenterPosWS",(owner?owner:actor.transform).position+Vector3.up);
        // PP_nilo_talk disables average-shadow rendering. No texture is generated
        // for it here, so retaining the prefab's index 0 incorrectly shades it black.
        m.SetFloat("_AverageShadowMapRTSampleIndex",-1);
        m.SetFloat("_PerCharReceiveAverageURPShadowMap",0);
    }

    void CreateContinuation()
    {
        continuation=new GameObject("Chapter1_3_02 first line");continuation.SetActive(false);
        foreach(var entry in config.followupActors) {
            var bundle=bundles.First(b=>b.GetAllAssetNames().Any(n=>n.Equals(entry.asset,StringComparison.OrdinalIgnoreCase)));
            var prefab=bundle.LoadAsset<GameObject>(entry.asset);
            var child=Instantiate(prefab,continuation.transform,false);
            child.AddComponent<StoryContinuationActor>().Initialize(entry,SetMaterialDefaults);
        }
    }

    void CreateStage()
    {
        var screenCamera=new GameObject("Screen clear").AddComponent<Camera>();
        screenCamera.cullingMask=0;screenCamera.depth=-100;
        screenCamera.clearFlags=CameraClearFlags.SolidColor;screenCamera.backgroundColor=Color.black;
        var canvas=new GameObject("Original 1280x720 layout",typeof(Canvas),typeof(CanvasScaler),typeof(GraphicRaycaster));
        canvas.GetComponent<Canvas>().renderMode=RenderMode.ScreenSpaceOverlay;
        var scaler=canvas.GetComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution=new Vector2(1280,720);scaler.screenMatchMode=CanvasScaler.ScreenMatchMode.Expand;
        stage=Rect(canvas.transform,"Stage",new Rect(0,0,1280,720));
        stage.anchorMin=stage.anchorMax=stage.pivot=new Vector2(.5f,.5f);stage.anchoredPosition=Vector2.zero;
        backgroundImage=Rect(stage,"Original city",new Rect(0,0,1280,720)).gameObject.AddComponent<RawImage>();
        backgroundImage.texture=StoryContent.Load<Texture2D>("Original/background");backgroundImage.raycastTarget=false;
        backgroundMaterial=new Material(StoryContent.Load<Shader>("StoryBackground"));backgroundImage.material=backgroundMaterial;
        SetFocusAmount(0);
        actorImage=Rect(stage,"Character camera",new Rect(0,0,1280,720)).gameObject.AddComponent<RawImage>();actorImage.raycastTarget=false;
        cameraCompositeMaterial=new Material(StoryContent.Load<Shader>("StoryCameraComposite"));
        actorImage.material=cameraCompositeMaterial;
        if(!FindObjectOfType<EventSystem>())new GameObject("Input",typeof(EventSystem),typeof(StandaloneInputModule));
    }

    void CreateCamera()
    {
        StoryBloomFeature.Background=backgroundImage;
        actorCamera=new GameObject("Original Talk_Center_L_Zoom_01").AddComponent<Camera>();
        StoryBloomFeature.BackgroundCamera=actorCamera;
        actorCamera.transform.position=config.cameraPosition;
        actorCamera.transform.rotation=Quaternion.identity;
        actorCamera.fieldOfView=config.fieldOfView;actorCamera.nearClipPlane=.1f;actorCamera.farClipPlane=1000;
        actorCamera.clearFlags=CameraClearFlags.SolidColor;actorCamera.backgroundColor=Color.clear;
        actorCamera.cullingMask=1<<17;actorCamera.allowHDR=true;actorCamera.allowMSAA=true;
        var extra=actorCamera.GetUniversalAdditionalCameraData();extra.renderPostProcessing=false;
        UpdateRenderTexture();
        var light=new GameObject("Original character directional light").AddComponent<Light>();
        light.type=LightType.Directional;light.color=Color.white;light.intensity=1;light.cullingMask=1<<17;
        light.transform.rotation=new Quaternion(.25488704f,.16773126f,-.044943456f,.95125127f);
        RenderSettings.sun=light;
        RenderSettings.ambientMode=AmbientMode.Flat;RenderSettings.ambientLight=new Color(.5f,.5f,.5f);
        OriginalToonParameters.Apply(actorCamera,light);
    }

    void UpdateRenderTexture()
    {
        int width=Mathf.Max(1280,Screen.width),height=Mathf.RoundToInt(width*9f/16);
        if(renderTexture && renderTexture.width==width)return;
        if(renderTexture) {actorCamera.targetTexture=null;renderTexture.Release();Destroy(renderTexture);}
        // Original effect materials contain HDR emission. Clamping to ARGB32
        // before bloom destroys the orange fire halo and flattens its colour.
        renderTexture=new RenderTexture(width,height,24,RenderTextureFormat.ARGBHalf);renderTexture.antiAliasing=4;renderTexture.Create();
        actorCamera.targetTexture=renderTexture;actorImage.texture=renderTexture;
    }

    void CreateUI()
    {
        originalUi = gameObject.AddComponent<OriginalStoryUi>();
        originalUi.Advance = Advance; originalUi.Hide = ToggleHidden;
        originalUi.Selected = Choose; originalUi.SkipConfirmed = () => Choose(-1);
        originalUi.Initialize(stage, bundles.First(b=>b.GetAllAssetNames().Any(n=>n.Equals(config.uiAsset,StringComparison.OrdinalIgnoreCase))), config);
        dialogue = originalUi.Dialogue; choices = originalUi.Choices; body = originalUi.Body;
        // Empty compatibility holders: all actual dim/log visuals belong to the prefab.
        dim = new GameObject("Legacy dim placeholder"); logPanel = new GameObject("Legacy log placeholder"); logPanel.SetActive(false);
        previewEnd = Label(stage,"장면 미리보기 끝 · 클릭하거나 R을 눌러 다시 보기",18,new Rect(160,660,960,40),TextAnchor.MiddleCenter).gameObject;
        previewEnd.SetActive(false);
        var touchBundle = bundles.First(b=>b.GetAllAssetNames().Any(n=>n.Equals(config.touchAsset,StringComparison.OrdinalIgnoreCase)));
        var effects = gameObject.AddComponent<OriginalScreenTouch>();
        effects.Initialize(stage, touchBundle.LoadAsset<GameObject>(config.touchAsset));
    }

    void Replay() {
        if(episode!=null){ReplayEpisode();return;}
        if(replayRoutine!=null)StopCoroutine(replayRoutine);
        replayRoutine=StartCoroutine(ReplaySequence());
    }
    IEnumerator ReplaySequence() {
        establishing=true;previewComplete=false;showingChoices=false;hidden=false;dialogueHoldElapsed=0;
        followingUp=false;continuation.SetActive(false);currentLine=StoryTextDisplay.Format(config.firstLine);
        dialogueTiming=new OriginalDialogueTiming(currentLine.Length,config.dialogueCharDelay,config.dialogueEndWait);
        originalUi.ResetView();
        dialogue.SetActive(false);choices.SetActive(false);dim.SetActive(false);logPanel.SetActive(false);previewEnd.SetActive(false);
        voice.Stop();voice.clip=firstVoice;actor.SetActive(false);body.text=currentLine;body.maxVisibleCharacters=0;
        if(focusRoutine!=null)StopCoroutine(focusRoutine);
        SetFocusAmount(0);
        yield return new WaitForSeconds(.5f);
        // Chapter1_3_01: ZoomIn(ZoomIn1_Large) immediately precedes Airen's line.
        ApplyStoryZoom("ZoomIn1_Large");actor.SetActive(true);
        actorAnimator.Play("answer_yes",0,0);actorAnimator.Update(0);
        started=Time.time;originalUi.ShowDialogue();voice.Play();establishing=false;originalUi.SetRevealing(true);
        Debug.Log("STORY_DIALOGUE");
    }
    void ApplyStoryZoom(string mode) {
        bool focus=!string.IsNullOrEmpty(mode);
        if(focusRoutine!=null)StopCoroutine(focusRoutine);
        focusRoutine=StartCoroutine(AnimateFocus(focus?1:0,focus?25f/60:.4f));
        Debug.Log("STORY_FOCUS "+(focus?mode:"normal"));
    }
    IEnumerator AnimateFocus(float target,float duration) {
        float from=focusAmount,elapsed=0;
        while(elapsed<duration){elapsed+=Time.deltaTime;SetFocusAmount(Mathf.Lerp(from,target,Mathf.SmoothStep(0,1,elapsed/duration)));yield return null;}
        SetFocusAmount(target);
    }
    void SetFocusAmount(float value) {
        focusAmount=value;backgroundMaterial.SetFloat("_BlurFactor",value);
        float width=1/Mathf.Lerp(1,1.07f,value);
        float height=backgroundImage.texture.width/(float)backgroundImage.texture.height/(16f/9f)*width;
        backgroundImage.uvRect=new Rect((1-width)/2,(1-height)/2,width,height);
        if(actorCamera && episode==null)actorCamera.transform.position=Vector3.Lerp(new Vector3(0,15.6f,-40),config.cameraPosition,value);
    }
    void Advance() {
        if(episode!=null){AdvanceEpisode();return;}
        if(!ready||establishing||originalUi.ModalOpen||originalUi.Busy)return;
        if(hidden){ToggleHidden();return;}
        if(showingChoices)return;
        if(previewComplete){Replay();return;}
        if(dialogueTiming.IsTyping){dialogueTiming.RevealAll();body.maxVisibleCharacters=currentLine.Length;started=Time.time-2;return;}
        if(followingUp)FinishPreview();else ShowChoices();
    }
    void ShowChoices() {if(!ready||establishing||previewComplete||showingChoices||originalUi.ModalOpen)return;showingChoices=true;hidden=false;originalUi.ShowChoices();voice.Stop();Debug.Log("STORY_CHOICES");}
    void Choose(int selected) {
        if(episode!=null){ChooseEpisode(selected);return;}
        Debug.Log(selected<0?"STORY_SKIPPED":"STORY_CHOICE_SELECTED "+(selected+1));
        if(selected<0){FinishPreview();return;}
        // Both choices converge on the same original Mana line.
        showingChoices=false;followingUp=true;actor.SetActive(false);continuation.SetActive(true);
        ApplyStoryZoom(null);currentLine=StoryTextDisplay.Format(config.followupLine);dialogueHoldElapsed=0;
        dialogueTiming=new OriginalDialogueTiming(currentLine.Length,config.followupCharDelay,config.followupEndWait);
        body.text=currentLine;body.maxVisibleCharacters=0;
        originalUi.ShowDialogue(config.followupSpeaker);originalUi.AddFollowupToLog();originalUi.SetRevealing(true);
        voice.Stop();voice.clip=followupVoice;voice.Play();
        Debug.Log("STORY_FOLLOWUP speaker="+config.followupSpeaker+" characters="+currentLine.Length);
    }
    void FinishPreview() {
        showingChoices=false;previewComplete=true;voice.Stop();actor.SetActive(false);continuation.SetActive(false);
        originalUi.SetRevealing(false);
        dialogue.SetActive(false);choices.SetActive(false);dim.SetActive(false);previewEnd.SetActive(true);
        ApplyStoryZoom(null);
    }
    void ToggleHidden(){if(!ready||establishing||previewComplete||originalUi.ModalOpen||originalUi.Busy)return;hidden=!hidden;originalUi.SetHidden(hidden);}
    void ToggleLog(){if(ready&&!hidden&&!establishing)originalUi.ToggleLog();}

    void Update()
    {
        if(!ready){UpdateAudioHotkeys();return;}
        originalUi.SetVoicePlaying(voice && voice.isPlaying && !originalUi.ModalOpen && !hidden);
        if(!originalUi.NameInputCaptured)UpdateAudioHotkeys();
        if((viewerHome&&viewerHome.Visible)||(storyMenu&&storyMenu.Visible))return;
        if(episode!=null){UpdateEpisode();return;}
        UpdateRenderTexture();
        if(Input.GetKeyDown(KeyCode.F2)&&!hidden&&!establishing)originalUi.OpenNameSettings();
        if(!originalUi.NameInputCaptured) {
        if(Input.GetKeyDown(KeyCode.R))Replay();
        if(Input.GetKeyDown(KeyCode.Space)||Input.GetKeyDown(KeyCode.Return))Advance();
        if(Input.GetKeyDown(KeyCode.Escape)&&!hidden&&!establishing)originalUi.Escape();
        if(Input.GetKeyDown(KeyCode.Tab))ToggleLog();
        if(Input.GetKeyDown(KeyCode.LeftShift)&&!hidden&&!establishing)originalUi.CycleAuto();
        if(showingChoices&&Input.GetKeyDown(KeyCode.Alpha1))originalUi.Select(0);
        if(showingChoices&&Input.GetKeyDown(KeyCode.Alpha2))originalUi.Select(1);
        if(Input.GetKeyDown(KeyCode.F6)){StoryBloomFeature.Enabled=!StoryBloomFeature.Enabled;Debug.Log("STORY_BLOOM_ENABLED "+StoryBloomFeature.Enabled);}
        }
        bool modal = originalUi.ModalOpen;
        if(modal != wasModal) { if(modal)voice.Pause();else voice.UnPause();wasModal=modal; }
        voice.pitch=originalUi.PlaybackSpeed;
        bool activeDialogue=!establishing&&!previewComplete&&!showingChoices;
        if(activeDialogue&&!modal&&!hidden) {
            if(dialogueTiming.Holding) dialogueHoldElapsed+=Time.deltaTime;
            dialogueTiming.Tick(Time.deltaTime,originalUi.AutoMode,voice.isPlaying);
            body.maxVisibleCharacters=dialogueTiming.VisibleCharacters;
        }
        originalUi.SetRevealing(activeDialogue&&!hidden&&!dialogueTiming.Holding);
        if(originalUi.AutoMode>0&&!modal&&!hidden&&activeDialogue&&dialogueTiming.Holding&&dialogueHoldElapsed>.3f) {
            if(followingUp)FinishPreview();else ShowChoices();
        }
    }
    void LateUpdate() {
        if(episode!=null)return;
        if(!ready)return;
        if(headBone) foreach(var material in actorMaterials) {
            // Original enum: face forward = head Y; face up = negative head X.
            material.SetVector("_FaceForwardDirection",headBone.up);
            material.SetVector("_FaceUpDirection",-headBone.right);
        }
        if(face) {
            face.SetBlendShapeWeight(angryIndex,35*Mathf.Clamp01((Time.time-started)/.3f));
            if(Time.time>blinkAt){blinkStart=Time.time;blinkAt=Time.time+UnityEngine.Random.Range(2f,4f);}
            if(blinkIndex>=0)face.SetBlendShapeWeight(blinkIndex,Mathf.Max(0,1-Mathf.Abs((Time.time-blinkStart)/.1f-1))*100);
            if(mouthIndex>=0) {
                float strength=0;if(voice.isPlaying){voice.GetOutputData(audioSamples,0);strength=Mathf.Clamp(audioSamples.Sum(s=>Mathf.Abs(s))/audioSamples.Length*900,0,75);}
                face.SetBlendShapeWeight(mouthIndex,strength);
            }
        }
    }

    RectTransform Rect(Transform parent,string name,Rect r) {
        var rt=new GameObject(name,typeof(RectTransform)).GetComponent<RectTransform>();rt.SetParent(parent,false);
        rt.anchorMin=rt.anchorMax=rt.pivot=new Vector2(0,1);rt.anchoredPosition=new Vector2(r.x,-r.y);rt.sizeDelta=r.size;return rt;
    }
    GameObject Box(Transform parent,string name,Rect r,Color color,bool hit=true) {var go=Rect(parent,name,r).gameObject;var im=go.AddComponent<UnityEngine.UI.Image>();im.color=color;im.raycastTarget=hit;return go;}
    GameObject Image(Transform parent,string name,Rect r) {
        if(!sprites.TryGetValue(name,out var sprite)) {
            var tex=StoryContent.Load<Texture2D>("Original/"+name);Vector4 border=name=="choice"?new Vector4(6,7,6,7):name=="key-line"?new Vector4(4,4,4,4):Vector4.zero;
            sprite=Sprite.Create(tex,new Rect(0,0,tex.width,tex.height),new Vector2(.5f,.5f),100,0,SpriteMeshType.FullRect,border);sprites[name]=sprite;
        }
        var go=Rect(parent,name,r).gameObject;var im=go.AddComponent<UnityEngine.UI.Image>();im.sprite=sprite;im.raycastTarget=false;if(name=="key-line")im.type=UnityEngine.UI.Image.Type.Sliced;return go;
    }
    Text Label(Transform parent,string text,int size,Rect r,TextAnchor anchor=TextAnchor.MiddleLeft,Color? color=null) {
        var t=Rect(parent,"Text",r).gameObject.AddComponent<Text>();t.font=font;t.fontSize=size;t.text=text;t.alignment=anchor;t.color=color??Color.white;t.raycastTarget=false;
        t.horizontalOverflow=HorizontalWrapMode.Wrap;t.verticalOverflow=VerticalWrapMode.Overflow;return t;
    }
    void LoadOriginalAtlas(string tag, Action<SpriteAtlas> register) {
        string suffix = "/" + tag + ".spriteatlas";
        foreach(var bundle in bundles) {
            string path = bundle.GetAllAssetNames().FirstOrDefault(n=>n.EndsWith(suffix,StringComparison.OrdinalIgnoreCase));
            if(path == null) continue;
            var atlas = bundle.LoadAsset<SpriteAtlas>(path);
            if(atlas) { register(atlas); Debug.Log("STORY_ATLAS " + tag); return; }
        }
        Debug.LogWarning("Original atlas unavailable: " + tag);
    }
    void OnDestroy(){SpriteAtlasManager.atlasRequested -= LoadOriginalAtlas;foreach(var b in bundles)if(b)b.Unload(false);if(renderTexture)renderTexture.Release();if(backgroundMaterial)Destroy(backgroundMaterial);if(cameraCompositeMaterial)Destroy(cameraCompositeMaterial);}
}

public sealed class StoryGradient : MaskableGraphic
{
    protected override void OnPopulateMesh(VertexHelper vh) {
        vh.Clear();Rect r=rectTransform.rect;
        for(int i=0;i<=16;i++){float t=i/16f;var c=new Color(0,0,0,.74f*Mathf.Pow(1-t,.65f));vh.AddVert(new Vector3(r.xMin,Mathf.Lerp(r.yMin,r.yMax,t)),c,Vector2.zero);vh.AddVert(new Vector3(r.xMax,Mathf.Lerp(r.yMin,r.yMax,t)),c,Vector2.one);if(i>0){int n=i*2;vh.AddTriangle(n-2,n,n-1);vh.AddTriangle(n-1,n,n+1);}}
    }
}

// Original Gradient2 uses RGB(52,53,55) with alpha 1,0,0,1 at 0,.4,.6,1.
public sealed class ChoicePattern : MaskableGraphic
{
    public Texture texture;
    public override Texture mainTexture=>texture?texture:Texture2D.whiteTexture;
    protected override void OnPopulateMesh(VertexHelper vh) {
        vh.Clear();if(!texture)return;var r=rectTransform.rect;
        float w=texture.width/1.1f,h=texture.height/1.1f;
        for(float y=0;y<r.height;y+=h)for(float x=0;x<r.width;x+=w){
            float x1=Mathf.Min(x+w,r.width),y1=Mathf.Min(y+h,r.height);int start=vh.currentVertCount;
            Add(vh,r,x,y,new Vector2(0,0));Add(vh,r,x,y1,new Vector2(0,(y1-y)/h));Add(vh,r,x1,y1,new Vector2((x1-x)/w,(y1-y)/h));Add(vh,r,x1,y,new Vector2((x1-x)/w,0));
            vh.AddTriangle(start,start+1,start+2);vh.AddTriangle(start,start+2,start+3);
        }
    }
    void Add(VertexHelper vh,Rect r,float x,float y,Vector2 uv){float t=x/r.width;float a=t<.4f?1-t/.4f:t>.6f?(t-.6f)/.4f:0;vh.AddVert(new Vector3(r.xMin+x,r.yMin+y),new Color(52/255f,53/255f,55/255f,a*.3921569f),uv);}
}
