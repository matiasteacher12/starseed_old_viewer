#if UNITY_EDITOR || VIEWER_BACKEND
using System;
using System.Collections;
using System.IO;
using System.Linq;
using UnityEngine;

public sealed partial class StoryScene {
    IEnumerator CheckSkipSegments(Camera camera,RenderTexture target,string directory) {
        var row=storyMenu.Catalogue.chapters.SelectMany(c=>c.episodes).First(e=>e.scene=="Chapter1_1");
        storyMenu.OpenEpisode(row);
        yield return WaitForEpisodeLoad();
        var anchors=StorySkipPlan.Build(episode);
        string actual=string.Join(",",anchors.Select(p=>p.group+":"+p.step));
        const string expected="Chapter1_1_01:4,Chapter1_1_04:1,Chapter1_1_13:1,Chapter1_1_13:6,Chapter1_1_17:3,Chapter1_1_17:4,Chapter1_1_30:5";
        if(actual!=expected)throw new Exception("Original SkipBlock anchors differ: "+actual);
        if(StorySkipPlan.Next(episode,"Chapter1_1_26",int.MaxValue)?.group!="Chapter1_1_30" ||
           StorySkipPlan.Next(episode,"Chapter1_1_31",int.MaxValue)!=null)
            throw new Exception("Skip boundary selection is incorrect");
        foreach(var old in loadingCatalogue.episodes.Where(e=>e.scene=="Chapter1_2"||e.scene=="Chapter1_3").Select(p=>BackendJson.FromJson<StoryEpisode>(StoryContent.Load<TextAsset>(p.resource).text)))
            if(StorySkipPlan.Build(old).Count!=0)throw new Exception("Unexpected boundary in previous stories");
        Debug.Log("STORY_SKIP_BOUNDARIES_OK "+actual);
        // Exercise the first landing as well as the ending. The opening movie
        // is before the first SkipBlock anchor. The original restarts at
        // SilentTalk + looping background, NOT the preceding unknown dialogue.
        if(episodeRoutine!=null)StopCoroutine(episodeRoutine);
        StopEpisodeMovies();originalUi.ResetView();
        currentGroup="Chapter1_1_01";currentCommandIndex=-1;
        originalUi.ToggleSummary();yield return new WaitForSecondsRealtime(.25f);
        var firstPopup=stage.Cast<Transform>().Single(t=>t.gameObject.activeInHierarchy && t.Find("Window/Contents/Skip")!=null);
        firstPopup.Find("Window/Button/Btn_Ok").GetComponent<UnityEngine.UI.Button>().onClick.Invoke();
        float firstDeadline=Time.realtimeSinceStartup+20;
        while(!(currentTextId=="Text_Chapter1_1_002"&&episodeWaiting)&&Time.realtimeSinceStartup<firstDeadline)
            yield return new WaitForSecondsRealtime(.1f);
        if(storyMenu.Visible || currentTextId!="Text_Chapter1_1_002" || !episodeWaiting ||
           !storyVideo || !storyVideo.url.EndsWith("jp_video_story_intro_02_02.mp4"))
            throw new Exception("Opening skip did not restore the authored background and dialogue");
        Debug.Log("STORY_SKIP_OPENING_OK dialogue002=true originalBackground=true");
        // Reproduce a late skip without playing every preceding line. Actual
        // summary confirmation -> planner -> remaining commands -> menu runs.
        if(episodeRoutine!=null)StopCoroutine(episodeRoutine);
        StopEpisodeMovies();originalUi.ResetView();
        backgroundImage.texture=Texture2D.blackTexture;
        currentGroup="Chapter1_1_26";currentCommandIndex=int.MaxValue;
        originalUi.ToggleSummary();yield return new WaitForSecondsRealtime(.25f);
        var popup=stage.Cast<Transform>().Single(t=>t.gameObject.activeInHierarchy && t.Find("Window/Contents/Skip")!=null);
        popup.Find("Window/Button/Btn_Ok").GetComponent<UnityEngine.UI.Button>().onClick.Invoke();
        float deadline=Time.realtimeSinceStartup+90;bool tailLine=false,cutOne=false,cutTwo=false;
        bool visibleOne=false,visibleTwo=false;
        while(!storyMenu.Visible && Time.realtimeSinceStartup<deadline) {
            if(currentTextId=="Text_Chapter1_1_095" && episodeWaiting)
                throw new Exception("Skip incorrectly landed on Mana before the native SilentTalk boundary");
            if(currentTextId=="Text_Chapter1_1_096" && episodeWaiting && !tailLine) {
                if(!backgroundImage.texture || backgroundImage.texture==Texture2D.blackTexture)
                    throw new Exception("Post-skip dialogue lost its laboratory background");
                tailLine=true;RenderTalkCamera();Canvas.ForceUpdateCanvases();camera.Render();
                SaveStoryTarget(target,Path.Combine(directory,"skip-remaining-dialogue.png"));
            }
            if(cinematicPlaying) {
                var roots=cinematicScene.GetRootGameObjects();
                cutOne|=roots.Any(r=>r.name.Contains("scenario3_cut_1"));
                cutTwo|=roots.Any(r=>r.name.Contains("scenario3_cut_2"));
                var director=roots.SelectMany(r=>r.GetComponentsInChildren<UnityEngine.Playables.PlayableDirector>(true))
                    .First(d=>d.name.Contains("scenario3_cut_"));
                bool first=director.name.EndsWith("_1");
                if(director.time>.8 && !(first?visibleOne:visibleTwo)) {
                    if(!cinematicSurface || !cinematicSurface.gameObject.activeInHierarchy ||
                       cinematicSurface.transform.GetSiblingIndex()<=episodeFade.transform.GetSiblingIndex())
                        throw new Exception("Timeline is covered by the story transition");
                    if(first && episodeFade.color.a<.95f)throw new Exception("Missing authored black fade precondition");
                    if(first && tailLine)throw new Exception("Laboratory dialogue appeared before the first cutscene");
                    foreach(var c in roots.SelectMany(r=>r.GetComponentsInChildren<Camera>()).Where(c=>c.isActiveAndEnabled).OrderBy(c=>c.depth))c.Render();
                    Canvas.ForceUpdateCanvases();camera.Render();
                    SaveStoryTarget(target,Path.Combine(directory,"skip-composited-"+director.name+".png"));
                    var pixels=ReadTransparencyPixels(target);
                    int visible=pixels.Count(p=>p.r>30||p.g>30||p.b>30);
                    if(visible<pixels.Length/8)throw new Exception("Timeline plays internally but the final screen is black");
                    Debug.Log("STORY_TIMELINE_VISIBLE "+director.name+" pixels="+visible+" fadeBelow="+episodeFade.color.a);
                    if(first)visibleOne=true;else visibleTwo=true;
                }
            }
            if(!originalUi.Busy && !originalUi.ModalOpen) {
                if(showingChoices)originalUi.Select(0);
                else if(episodeWaiting && dialogueTiming.Holding && dialogueHoldElapsed>.3f)AdvanceEpisode();
            }
            yield return new WaitForSecondsRealtime(.1f);
        }
        if(!tailLine || !cutOne || !cutTwo || !visibleOne || !visibleTwo || !storyMenu.Visible || globalStoryEffects.Count!=0 ||
           (cinematicSurface && cinematicSurface.gameObject.activeSelf))
            throw new Exception("Skip discarded the original ending segment or failed to return: "+tailLine+"/"+cutOne+"/"+cutTwo);
        Debug.Log("STORY_SKIP_TAIL_OK noWrongManaLine=true originalCut1=true laboratoryBackground=true originalCut2=true menuReturn=true");
    }
}

#endif
