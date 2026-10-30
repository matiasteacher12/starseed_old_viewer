#if UNITY_EDITOR || VIEWER_BACKEND
using System;
using System.Collections;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Video;
using TMPro;

public sealed partial class StoryScene {
    VideoPlayer storyVideo;
    AudioSource movieAudio;
    RenderTexture movieTarget;
    bool moviePaused,movieBlocking,movieFinished;
    string movieError;
    [Serializable] class MovieCaption { public double start,end;public string key,text; }
    [Serializable] class MovieCaptions { public MovieCaption[] captions; }
    GameObject subtitleRoot;
    TMP_Text movieSubtitle;
    string activeCaption;
    UnityEngine.UI.RawImage foregroundMovie;
    GameObject movieSkipRoot;
    UnityEngine.UI.Button movieSkipButton;
    void EnsureMovieSurface() {
        if(foregroundMovie)return;
        var go=new GameObject("Original movie overlay",typeof(RectTransform),typeof(UnityEngine.UI.RawImage));
        go.transform.SetParent(stage,false);foregroundMovie=go.GetComponent<UnityEngine.UI.RawImage>();
        var rect=foregroundMovie.rectTransform;rect.anchorMin=Vector2.zero;rect.anchorMax=Vector2.one;rect.offsetMin=rect.offsetMax=Vector2.zero;
        foregroundMovie.raycastTarget=true;foregroundMovie.texture=Texture2D.blackTexture;go.SetActive(false);
        var holder=new GameObject("Movie skip preparation");holder.SetActive(false);
        var original=Instantiate(OriginalAsset<GameObject>("Assets/Bundles/UI/Prefabs/Common/Popup_VideoPlayer.prefab"),holder.transform,false);
        var top=original.transform.Find("Contents/Center/Screen/Top");top.SetParent(stage,false);movieSkipRoot=top.gameObject;
        foreach(var animation in top.GetComponentsInChildren<Animation>(true)){animation.Stop();animation.enabled=false;}
        foreach(var group in top.GetComponentsInChildren<CanvasGroup>(true))group.alpha=1;
        movieSkipButton=top.Find("Skip/Btn_Skip").GetComponent<UnityEngine.UI.Button>();
        var skipKey=movieSkipButton.transform.Find("Key");if(skipKey)skipKey.gameObject.SetActive(true);
        movieSkipButton.onClick=new UnityEngine.UI.Button.ButtonClickedEvent();movieSkipButton.onClick.AddListener(originalUi.ToggleSummary);
        movieSkipRoot.SetActive(false);Destroy(holder);
        // Native Popup_VideoPlayer closes only itself after confirmation. The
        // suspended script then continues after Video; no story-boundary jump.
        originalUi.MovieSkipConfirmed=()=>{if(movieBlocking){movieFinished=true;Debug.Log("STORY_VIDEO_SKIP_CONFIRMED");}};
    }
    void EnsureMovieSubtitles() {
        if(subtitleRoot)return;
        var holder=new GameObject("Movie UI preparation");holder.SetActive(false);
        var original=Instantiate(OriginalAsset<GameObject>("Assets/Bundles/UI/Prefabs/Common/Popup_VideoPlayer.prefab"),holder.transform,false);
        // Keep the original subtitle hierarchy, frame, font and bottom anchor.
        var bottom=original.transform.Find("Contents/Center/Screen_Fix/Bottom");
        subtitleRoot=bottom.gameObject;bottom.SetParent(stage,false);
        foreach(var animation in bottom.GetComponentsInChildren<Animation>(true)){animation.Stop();animation.enabled=false;}
        foreach(var group in bottom.GetComponentsInChildren<CanvasGroup>(true))group.alpha=1;
        foreach(var graphic in bottom.GetComponentsInChildren<UnityEngine.UI.Graphic>(true))graphic.raycastTarget=false;
        movieSubtitle=bottom.Find("Subtitle_TMP/Layout/TMP_Subtitle").GetComponent<TMP_Text>();
        movieSubtitle.font=originalUi.Body.font;
        movieSubtitle.richText=false;bottom.Find("Subtitle_TMP").gameObject.SetActive(true);
        subtitleRoot.SetActive(false);Destroy(holder);
    }
    void ShowMovieCaption(MovieCaption caption) {
        string key=caption?.key;
        if(key==activeCaption)return;
        activeCaption=key;
        if(caption==null){if(subtitleRoot)subtitleRoot.SetActive(false);return;}
        EnsureMovieSubtitles();movieSubtitle.text=caption.text;subtitleRoot.SetActive(true);subtitleRoot.transform.SetAsLastSibling();
        Debug.Log("STORY_VIDEO_SUBTITLE "+key+" start="+caption.start+" end="+caption.end);
    }

    IEnumerator PlayEpisodeMovie(string path,bool loop,bool blocking=true,string transition="",float skipDelay=0) {
        if(loop)blocking=false;
        StopEpisodeMovies();
        if(!storyVideo) {
            storyVideo=gameObject.AddComponent<VideoPlayer>();storyVideo.playOnAwake=false;
            storyVideo.renderMode=VideoRenderMode.RenderTexture;storyVideo.waitForFirstFrame=true;
            storyVideo.audioOutputMode=VideoAudioOutputMode.AudioSource;
            movieAudio=gameObject.AddComponent<AudioSource>();movieAudio.playOnAwake=false;movieAudio.volume=.3f;
            // Route video through the same master mute/volume as voices and BGM.
            movieAudio.ignoreListenerVolume=false;movieAudio.spatialBlend=0;
            storyVideo.errorReceived+=(p,error)=>movieError=error;
            storyVideo.loopPointReached+=p=>{if(!p.isLooping)movieFinished=true;};
        }
        string full=StoryContent.FilePath(path);
        if(!File.Exists(full))throw new InvalidOperationException("Missing original movie "+full);
        movieError=null;movieFinished=false;moviePaused=false;movieBlocking=blocking;
        if(blocking) {
            EnsureMovieSurface();foregroundMovie.texture=Texture2D.blackTexture;
            foregroundMovie.gameObject.SetActive(true);foregroundMovie.transform.SetAsLastSibling();
            movieSkipRoot.transform.SetAsLastSibling();
            originalUi.SetMovieMode(true,false);
        }
        storyVideo.url=full;storyVideo.isLooping=loop;storyVideo.Prepare();
        float deadline=Time.realtimeSinceStartup+30;
        while(!storyVideo.isPrepared && movieError==null && Time.realtimeSinceStartup<deadline)yield return null;
        if(!storyVideo.isPrepared)throw new InvalidOperationException("Movie preparation failed: "+path+" "+movieError);
        movieTarget=new RenderTexture((int)storyVideo.width,(int)storyVideo.height,0,RenderTextureFormat.ARGB32);
        movieTarget.Create();storyVideo.targetTexture=movieTarget;
        if(storyVideo.audioTrackCount>0) {storyVideo.EnableAudioTrack(0,true);storyVideo.SetTargetAudioSource(0,movieAudio);}
        if(blocking)foregroundMovie.texture=movieTarget;
        else {backgroundImage.texture=movieTarget;SetFocusAmount(focusAmount);}
        storyVideo.Play();
        Debug.Log("STORY_VIDEO_BEGIN path="+path+" loop="+loop+" duration="+storyVideo.length+" audioTracks="+storyVideo.audioTrackCount);
        if(!blocking)yield break;
        var captionPath=Path.ChangeExtension(path,".subtitles.json");
        var captions=StoryContent.HasFile(captionPath)?BackendJson.FromJson<MovieCaptions>(File.ReadAllText(StoryContent.FilePath(captionPath))).captions:new MovieCaption[0];
        while(!movieFinished && movieError==null) {
            bool allowSkip=storyVideo.time>=skipDelay;
            movieSkipRoot.SetActive(allowSkip);originalUi.SetMovieMode(true,allowSkip);
            ShowMovieCaption(captions.FirstOrDefault(c=>storyVideo.time>=c.start && storyVideo.time<c.end));
            yield return null;
        }
        if(movieError!=null)throw new InvalidOperationException("Movie playback failed: "+movieError);
        Debug.Log("STORY_VIDEO_END path="+path);
        StopEpisodeMovies();backgroundImage.texture=Texture2D.blackTexture;
        if(transition=="FadeInOverlay")yield return originalUi.PlaySceneFade(false,"");
    }
    void StopEpisodeMovies() {
        if(foregroundMovie){foregroundMovie.texture=Texture2D.blackTexture;foregroundMovie.gameObject.SetActive(false);movieSkipRoot.SetActive(false);}
        if(originalUi)originalUi.SetMovieMode(false);
        ShowMovieCaption(null);
        if(storyVideo){storyVideo.Stop();storyVideo.targetTexture=null;}
        if(movieAudio)movieAudio.Stop();
        if(movieTarget){if(backgroundImage && backgroundImage.texture==movieTarget)backgroundImage.texture=Texture2D.blackTexture;movieTarget.Release();Destroy(movieTarget);movieTarget=null;}
        moviePaused=false;movieBlocking=false;
    }
    void PauseEpisodeMovie(bool value) {
        if(!storyVideo || !storyVideo.isPrepared || moviePaused==value)return;
        moviePaused=value;if(value)storyVideo.Pause();else if(!movieFinished)storyVideo.Play();
    }
    readonly System.Collections.Generic.List<GameObject> globalStoryEffects=new System.Collections.Generic.List<GameObject>();
    void ClearGlobalStoryEffects(){foreach(var effect in globalStoryEffects)if(effect)Destroy(effect);globalStoryEffects.Clear();}
    void ShowGlobalStoryEffect(string key,int target=0) {
        var definition=episode.effects.First(e=>e.key==key);
        var effect=Instantiate(OriginalAsset<GameObject>(definition.asset));
        ConfigureEffectComposite(effect);
        // Original CharacterRenderCamera.ShowGlobalEffect(target=0) uses the
        // rendering camera position as both causer and target. EFF_talk_scan_player
        // contains the local forward offset and its own particle curves.
        // Target 1 is the background anchor, never the currently speaking actor.
        // Its original prefab position is exported relative to the render rig.
        effect.transform.position=target==1?definition.backgroundPosition:actorCamera.transform.position;
        foreach(var tr in effect.GetComponentsInChildren<Transform>(true))tr.gameObject.layer=17;
        foreach(var ps in effect.GetComponentsInChildren<ParticleSystem>(true))ps.Play();
        globalStoryEffects.Add(effect);Debug.Log("STORY_GLOBAL_EFFECT "+key);
        AuditEffectIfRequested(effect,key);
    }
}

#endif
