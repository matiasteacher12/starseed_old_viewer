#if UNITY_EDITOR || VIEWER_BACKEND
using System;
using System.Collections;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

public sealed partial class StoryScene {
    IEnumerator CheckMovieStage(Camera camera,RenderTexture target,string directory) {
        storyMenu.OpenEpisode(storyMenu.Catalogue.chapters.SelectMany(c=>c.episodes).First(e=>e.scene=="Chapter1_1"));
        yield return WaitForEpisodeLoad();
        float deadline=Time.realtimeSinceStartup+30;
        while((!storyVideo || !storyVideo.isPlaying || storyVideo.time<1.5)&&Time.realtimeSinceStartup<deadline)yield return null;
        if(!movieSkipRoot.activeInHierarchy || originalUi.Root.transform.Find("Center/Top/Hud").gameObject.activeInHierarchy)
            throw new Exception("Full movie must expose only the original movie SKIP UI");
        Canvas.ForceUpdateCanvases();camera.Render();SaveStoryTarget(target,Path.Combine(directory,"movie-opening-skip.png"));
        originalUi.Escape();yield return new WaitForSecondsRealtime(.4f);
        if(!originalUi.ModalOpen || !moviePaused)throw new Exception("ESC did not open movie confirmation/pause");
        double pausedTime=storyVideo.time;yield return new WaitForSecondsRealtime(.3f);
        if(Math.Abs(pausedTime-storyVideo.time)>.1)throw new Exception("Movie kept playing under confirmation");
        var popup=stage.Cast<Transform>().Single(t=>t.gameObject.activeInHierarchy&&t.Find("Window/Contents/Skip")!=null);
        popup.Find("Window/Button/Btn_Cancel").GetComponent<Button>().onClick.Invoke();yield return new WaitForSecondsRealtime(.6f);
        if(originalUi.ModalOpen || storyVideo.time<=pausedTime)throw new Exception("Movie cancel did not resume");
        movieSkipButton.onClick.Invoke();yield return new WaitForSecondsRealtime(.2f);
        popup.Find("Window/Button/Btn_Ok").GetComponent<Button>().onClick.Invoke();
        deadline=Time.realtimeSinceStartup+15;
        while(!(episodeWaiting&&currentTextId=="Text_Chapter1_1_001")&&Time.realtimeSinceStartup<deadline)yield return null;
        if(currentTextId!="Text_Chapter1_1_001" || !episodeWaiting)throw new Exception("Movie skip jumped a story boundary instead of continuing after Video");
        Debug.Log("STORY_MOVIE_SKIP_OK onlySkip=true escape=true cancelResumes=true resumesAfterVideo=true");
        StopCoroutine(episodeRoutine);StopEpisodeMovies();ClearGlobalStoryEffects();originalUi.ResetView();
        currentTextId="";episodeRoutine=StartCoroutine(RunGroup("Chapter1_1_17",3));
        deadline=Time.realtimeSinceStartup+20;
        while((!movieBlocking || !storyVideo.isPlaying || storyVideo.time<1)&&Time.realtimeSinceStartup<deadline)yield return null;
        if(!movieBlocking)throw new Exception("Authored middle movie not reached");
        storyVideo.time=12;yield return new WaitForSecondsRealtime(.8f);
        if(episodeFade.color.a<.95f)throw new Exception("Missing precondition: original black fade must still exist below the movie");
        if(foregroundMovie.transform.GetSiblingIndex()<=episodeFade.transform.GetSiblingIndex())throw new Exception("Fade covers the movie");
        Canvas.ForceUpdateCanvases();camera.Render();SaveStoryTarget(target,Path.Combine(directory,"movie-middle-visible.png"));
        var pixels=ReadTransparencyPixels(target);
        if(pixels.Count(p=>p.r>30||p.g>30||p.b>30)<pixels.Length/10)throw new Exception("Middle movie still renders black");
        movieSkipButton.onClick.Invoke();yield return new WaitForSecondsRealtime(.2f);
        popup.Find("Window/Button/Btn_Ok").GetComponent<Button>().onClick.Invoke();
        deadline=Time.realtimeSinceStartup+20;
        while(!(episodeWaiting&&currentTextId=="Text_Chapter1_1_053")&&Time.realtimeSinceStartup<deadline)yield return null;
        if(currentTextId!="Text_Chapter1_1_053" || !storyBackground)throw new Exception("Middle movie failed to restore original background/actor");
        RenderTalkCamera();Canvas.ForceUpdateCanvases();camera.Render();SaveStoryTarget(target,Path.Combine(directory,"fire-background-hdr.png"));
        foreach(var graphic in storyBackground.GetComponentsInChildren<Graphic>(true))
            Debug.Log("STORY_BG_GRAPHIC "+graphic.name+" texture="+graphic.mainTexture+" shader="+graphic.material.shader.name+" alpha="+graphic.color.a+" rect="+graphic.rectTransform.rect);
        var hdr=renderTexture;
        if(hdr.format!=RenderTextureFormat.ARGBHalf)throw new Exception("Effect camera must retain HDR before bloom");
        var floats=new Texture2D(hdr.width,hdr.height,TextureFormat.RGBAFloat,false,true);RenderTexture.active=hdr;floats.ReadPixels(new Rect(0,0,hdr.width,hdr.height),0,0);floats.Apply();RenderTexture.active=null;
        float maximum=floats.GetPixels().Max(p=>Mathf.Max(p.r,Mathf.Max(p.g,p.b)));Destroy(floats);
        if(maximum<=1)throw new Exception("Fire emission was clamped before composite");
        Debug.Log("STORY_FIRE_HDR_MAX "+maximum);
        // Hide foreground effects for an explicit background-only pixel check.
        actorImage.gameObject.SetActive(false);camera.Render();SaveStoryTarget(target,Path.Combine(directory,"fire-background-only.png"));
        actorImage.gameObject.SetActive(true);
        pixels=ReadTransparencyPixels(target);
        if(pixels.Count(p=>p.r>30||p.g>30||p.b>30)<pixels.Length/5)throw new Exception("Native building background is missing");
        Debug.Log("STORY_MOVIE_MIDDLE_OK blackFadeBelowMovie=true backgroundRestored=true");
        StopCoroutine(episodeRoutine);originalUi.HideAllDialogue();showingChoices=false;
        // The reported path is attention -> idle_inven, not intro03 -> idle.
        float entrance=occupied["Center"].PlayMotion("attention");
        yield return new WaitForSecondsRealtime(entrance+1f);
        episodeRoutine=StartCoroutine(RunGroup("Chapter1_1_19"));
        yield return null;
        if(motionDelay<=0 || choices.activeSelf)throw new Exception("Choice must wait for the authored motion");
        float waitStart=Time.time;deadline=Time.realtimeSinceStartup+15;
        while(!showingChoices&&Time.realtimeSinceStartup<deadline) {
            if(motionDelay>.02f&&choices.activeSelf)throw new Exception("Choice appeared before animation wait ended");
            yield return null;
        }
        if(!showingChoices || motionDelay>0 || Time.time-waitStart<.1f)throw new Exception("Animation/choice wait failed");
        Debug.Log("STORY_MOTION_CHOICE_OK waited="+(Time.time-waitStart));
        // Nonblocking background cinema (white Airen transition) has no movie controls.
        originalUi.HideAllDialogue();showingChoices=false;
        originalUi.SetScriptInteraction(false);
        yield return PlayEpisodeMovie("Movies/jp_spine_story_main_01_01_01.mp4",false,false);
        yield return new WaitForSecondsRealtime(.4f);
        if(movieSkipRoot.activeSelf || foregroundMovie.gameObject.activeSelf || originalUi.Root.transform.Find("Center/Top/Hud").gameObject.activeInHierarchy)
            throw new Exception("Background cinematic leaked SKIP/HUD");
        Debug.Log("STORY_BACKGROUND_CINEMA_UI_OK");
        StopEpisodeMovies();
    }
}

#endif
