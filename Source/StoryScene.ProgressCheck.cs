#if UNITY_EDITOR || VIEWER_BACKEND
using System;
using System.Collections;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

public sealed partial class StoryScene {
    void AssertProgress(OriginalStoryMenu.Chapter chapter,int count) {
        string actual=stage.Find("Root_StoryChapter/Episode/Center/Right/Info/Progress/Bg/Text_Progress").GetComponent<Text>().text;
        if(!storyMenu.Visible||storyMenu.ReadCount(chapter)!=count||actual!=count+"/"+chapter.episodes.Length)throw new Exception("Progress mismatch "+actual);
        var list=stage.Find("Root_StoryChapter/Episode/Center/Right/Info/ScrollView/Viewport/Content");
        foreach(var ep in chapter.episodes) {
            var row=list.Find(ep.id);
            var titles=row.GetComponentsInChildren<MText>(true).Where(t=>t.valueId=="Name").ToArray();
            if(titles.Length==0||titles.Any(t=>t.text!=ep.title))throw new Exception("Episode title is not plain source title "+ep.id);
        }
    }
    IEnumerator ConfirmProgressSkip() {
        // Arrange the audited command position, then exercise the real popup
        // confirmation and ending callbacks without an opening movie gate.
        if(episodeRoutine!=null){StopCoroutine(episodeRoutine);episodeRoutine=null;}
        StopEpisodeMovies();originalUi.ResetView();
        originalUi.ToggleSummary();yield return new WaitForSecondsRealtime(.3f);
        var popup=stage.Cast<Transform>().Single(t=>t.gameObject.activeInHierarchy&&t.Find("Window/Contents/Skip")!=null);
        popup.Find("Window/Button/Btn_Ok").GetComponent<Button>().onClick.Invoke();
    }
    IEnumerator AwaitProgressMenu() {
        float until=Time.realtimeSinceStartup+30;
        while(!storyMenu.Visible&&Time.realtimeSinceStartup<until)yield return null;
        if(!storyMenu.Visible)throw new Exception("Completion did not return to menu");
    }
    IEnumerator CheckStoryProgress(Camera camera,RenderTexture target,string directory) {
        var chapter=storyMenu.Catalogue.chapters.Single(c=>c.id=="Chapter1");
        var intro=chapter.episodes.Single(e=>e.scene=="Chapter1_1");
        var normal=chapter.episodes.Single(e=>e.scene=="Chapter1_3");
        var interrupted=chapter.episodes.Single(e=>e.scene=="Chapter1_4");
        storyMenu.ShowChapter(chapter);
        if(Environment.GetCommandLineArgs().Contains("-progress-reload-probe")) {
            AssertProgress(chapter,2);
            if(!storyMenu.IsRead(intro)||!storyMenu.IsRead(normal)||storyMenu.IsRead(interrupted))throw new Exception("Reloaded wrong episode identities");
            Debug.Log("STORY_PROGRESS_RELOAD_OK isolatedStore=true count=2/25");yield break;
        }
        AssertProgress(chapter,0);
        storyMenu.OpenEpisode(interrupted);yield return WaitForEpisodeLoad();
        if(storyMenu.IsRead(interrupted))throw new Exception("Opening marked complete");
        if(episodeRoutine!=null)StopCoroutine(episodeRoutine);
        ReturnToStoryMenu(false);AssertProgress(chapter,0);
        storyMenu.OpenEpisode(intro);yield return WaitForEpisodeLoad();
        // Old Korean anchors differ from the Japanese case. The next anchor
        // after the opening is Chapter1_1_01 step 2 in this preserved script.
        var next=StorySkipPlan.Next(episode,"Chapter1_1_01",0);
        if(next==null||next.group!="Chapter1_1_01"||next.step!=2)throw new Exception("Old opening anchor changed");
        if(episodeRoutine!=null)StopCoroutine(episodeRoutine);
        currentGroup="Chapter1_1_01";currentCommandIndex=0;
        yield return ConfirmProgressSkip();yield return new WaitForSecondsRealtime(.2f);
        if(storyMenu.Visible||storyMenu.ReadCount(chapter)!=0||!episodeSkipped)throw new Exception("Intermediate skip marked complete");
        Debug.Log("STORY_PROGRESS_MIDDLE_OK count=0");
        if(episodeRoutine!=null)StopCoroutine(episodeRoutine);
        // Exercise the final-summary path after the last Korean skip anchor.
        currentGroup="Chapter1_1_08";currentCommandIndex=int.MaxValue;
        yield return ConfirmProgressSkip();
        if(storyMenu.IsRead(intro))throw new Exception("Saved before ending fade");
        yield return AwaitProgressMenu();AssertProgress(chapter,1);
        storyMenu.OpenEpisode(normal);yield return WaitForEpisodeLoad();
        float until=Time.realtimeSinceStartup+90;
        while(!storyMenu.Visible&&Time.realtimeSinceStartup<until) {
            if(!originalUi.Busy&&!originalUi.ModalOpen&&!processingCommands&&motionDelay<=0) {
                if(showingChoices)originalUi.Select(0);else if(episodeWaiting)AdvanceEpisode();
            }
            yield return new WaitForSecondsRealtime(.1f);
        }
        AssertProgress(chapter,2);if(episodeLines!=5)throw new Exception("Normal route incomplete");
        storyMenu.OpenEpisode(normal);yield return WaitForEpisodeLoad();
        yield return ConfirmProgressSkip();yield return AwaitProgressMenu();AssertProgress(chapter,2);
        storyMenu.ShowHome();storyMenu.ShowChapter(chapter);AssertProgress(chapter,2);
        yield return new WaitForSecondsRealtime(1.5f);
        Canvas.ForceUpdateCanvases();camera.Render();SaveStoryTarget(target,Path.Combine(directory,"progress-2-of-25.png"));
        Debug.Log("STORY_PROGRESS_OK abandoned=0 middleSkip=0 finalSkip=1 normalEnd=2 replay=2 titlesPlain=true");
        yield return CheckTouchParticles(camera,target,directory);
    }
    IEnumerator CheckTouchParticles(Camera camera,RenderTexture target,string directory) {
        var backdrop=Box(stage,"Touch comparison gray",new Rect(0,0,1280,720),new Color(.5f,.5f,.5f,1),false);
        var effect=GetComponent<OriginalScreenTouch>().ShowAt(Vector2.zero);
        // Freeze a deterministic authored simulation state. Real-time waits in
        // headless rendering may skip the short burst while shaders warm up.
        effect.Pause();yield return null;
        foreach(var ps in effect.GetComponentsInChildren<ParticleSystem>(true)) {
            ps.useAutoRandomSeed=false;ps.randomSeed=731;ps.Simulate(.18f,false,true,false);
        }
        effect.Pause();
        var renderer=effect.GetComponentsInChildren<ParticleSystemRenderer>(true).Single(r=>r.name=="par_rec");
        int particles=renderer.GetComponent<ParticleSystem>().particleCount;if(particles==0)throw new Exception("No diamond particles to compare");
        var corrected=renderer.sharedMaterial;
        if(corrected.passCount!=1||!corrected.shader.name.EndsWith("_StoryUI"))throw new Exception("Touch fix not installed");
        var source=bundles.Single(b=>b.GetAllAssetNames().Contains("story-click-shader/2.shader")).LoadAllAssets<Shader>().Single(s=>s.name=="M_FX/HDR/S_effect_hdr_ab");
        var old=new Material(corrected){shader=source};renderer.sharedMaterial=old;effect.RefreshParticles();
        yield return null;yield return null;Canvas.ForceUpdateCanvases();camera.Render();
        SaveStoryTarget(target,Path.Combine(directory,"touch-before.png"));var before=ReadTransparencyPixels(target);
        renderer.sharedMaterial=corrected;effect.RefreshParticles();yield return null;yield return null;
        Canvas.ForceUpdateCanvases();camera.Render();SaveStoryTarget(target,Path.Combine(directory,"touch-after.png"));var after=ReadTransparencyPixels(target);
        int blackBefore=before.Count(c=>c.r<20&&c.g<20&&c.b<20),blackAfter=after.Count(c=>c.r<20&&c.g<20&&c.b<20);
        int blueAfter=after.Count(c=>c.b>c.r+20&&c.b>=c.g);
        // ScreenSpaceCamera can select only the forward pass even on the old
        // shader. Its before image is diagnostic, not proof of overlay parity.
        if(source.passCount!=4||blackAfter!=0||blueAfter<5)throw new Exception("Touch render failed "+blackBefore+"/"+blackAfter+" blue="+blueAfter);
        Debug.Log("STORY_TOUCH_COLOR_OK particles="+particles+" blackBefore="+blackBefore+" blackAfter="+blackAfter+" blueAfter="+blueAfter+" preservedColorPass=true overlayComparisonRequired="+(blackBefore==0));
        effect.Stop();Destroy(old);Destroy(backdrop);
    }
}

#endif
