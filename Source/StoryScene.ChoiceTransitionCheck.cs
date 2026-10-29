using System;
using System.Collections;
using System.IO;
using System.Linq;
using UnityEngine;

public sealed partial class StoryScene {
    IEnumerator CheckChoiceTransition(Camera camera,RenderTexture target,string directory) {
        storyMenu.OpenEpisode(storyMenu.Catalogue.chapters.SelectMany(c=>c.episodes).First(e=>e.scene=="Chapter1_1"));
        yield return WaitForEpisodeLoad();
        if(episodeRoutine!=null)StopCoroutine(episodeRoutine);
        StopEpisodeMovies();originalUi.ResetView();
        yield return PlayEpisodeMovie("Movies/jp_video_story_intro_02_02.mp4",true,false);
        episodeRoutine=StartCoroutine(RunGroup("Chapter1_1_02",1));
        if(!showingChoices || !originalUi.Choices.activeInHierarchy)throw new Exception("Initial choice missing");
        originalUi.Select(0);
        yield return new WaitForSecondsRealtime(1.1f);
        // A choice immediately followed by another choice must remain visible.
        if(currentGroup!="Chapter1_1_03" || !originalUi.Choices.activeInHierarchy || pendingChoices.Length!=2)
            throw new Exception("Consecutive choices disappeared");
        originalUi.Select(1);
        yield return new WaitForSecondsRealtime(.2f);
        if(!originalUi.Busy || !originalUi.Choices.activeInHierarchy)
            throw new Exception("Selection animation was cut short");
        yield return new WaitForSecondsRealtime(.9f);
        if(currentGroup!="Chapter1_1_04")throw new Exception("Incorrect destination group");
        bool stale=originalUi.Choices.activeInHierarchy;
        Debug.Log("STORY_CHOICE_TRANSITION_SELECTED staleChoices="+stale);
        float deadline=Time.realtimeSinceStartup+25;bool captured=false;
        while(!(episodeWaiting&&currentTextId=="Text_Chapter1_1_009") && Time.realtimeSinceStartup<deadline) {
            stale|=originalUi.Choices.activeInHierarchy;
            if(!captured && storyVideo && storyVideo.url.EndsWith("jp_spine_story_main_01_00_00.mp4") && storyVideo.time>1) {
                RenderTalkCamera();Canvas.ForceUpdateCanvases();camera.Render();
                SaveStoryTarget(target,Path.Combine(directory,"choice-video-transition.png"));captured=true;
            }
            yield return null;
        }
        if(stale)throw new Exception("Stale selected choices remained over the next background video");
        if(!captured || currentTextId!="Text_Chapter1_1_009" || !episodeWaiting)
            throw new Exception("Background transition did not reach the original next dialogue");
        Debug.Log("STORY_CHOICE_TRANSITION_VERIFIED consecutiveChoices=true fullSelectionEffect=true videoWithoutChoices=true nextDialogue=true");
    }
}
