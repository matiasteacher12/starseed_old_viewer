#if UNITY_EDITOR || VIEWER_BACKEND
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

public sealed partial class StoryScene {
    IEnumerator CheckAwakening(Camera camera,RenderTexture target,string directory) {
        var row=storyMenu.Catalogue.chapters.SelectMany(c=>c.episodes).First(e=>e.scene=="Chapter1_1");
        storyMenu.OpenEpisode(row);
        yield return WaitForEpisodeLoad();
        float deadline=Time.realtimeSinceStartup+600;
        int choicesSeen=0;string capturedLine=null;var videosSeen=new HashSet<string>();var timelineFrames=new HashSet<string>();
        while(!storyMenu.Visible && Time.realtimeSinceStartup<deadline) {
            if(storyVideo && storyVideo.isPlaying && storyVideo.time>1.5 && videosSeen.Add(storyVideo.url)) {
                if(movieBlocking && (!foregroundMovie.gameObject.activeInHierarchy || !movieSkipRoot.activeInHierarchy))
                    throw new Exception("Full movie missing its surface or SKIP: "+storyVideo.url);
                if(!movieBlocking && movieSkipRoot && movieSkipRoot.activeInHierarchy)
                    throw new Exception("Background video leaked movie SKIP: "+storyVideo.url);
                Debug.Log("STORY_VIDEO_UI_AUDIT file="+Path.GetFileName(storyVideo.url)+" blocking="+movieBlocking+" movieSkip="+(movieSkipRoot&&movieSkipRoot.activeInHierarchy));
                RenderTalkCamera();Canvas.ForceUpdateCanvases();camera.Render();
                SaveStoryTarget(target,Path.Combine(directory,"awakening-video-"+videosSeen.Count+".png"));
                // Decode/render each original video, then seek near its natural end.
                if(movieBlocking && storyVideo.length>4)storyVideo.time=storyVideo.length-1;
            }
            if(cinematicPlaying) {
                if(movieSkipRoot&&movieSkipRoot.activeInHierarchy)throw new Exception("Timeline leaked movie SKIP");
                var d=cinematicScene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<UnityEngine.Playables.PlayableDirector>()).FirstOrDefault(d=>d.name.StartsWith("prologue_opening_scenario"));
                if(d && d.time>1 && timelineFrames.Add(d.name)) {
                    Canvas.ForceUpdateCanvases();camera.Render();SaveStoryTarget(target,Path.Combine(directory,"awakening-timeline-"+timelineFrames.Count+".png"));
                }
            }
            if(episodeWaiting && !processingCommands && !originalUi.Busy) {
                if(currentTextId!=capturedLine && dialogueTiming.Holding) {
                    foreach(char c in body.text.Where(c=>!char.IsWhiteSpace(c)))
                        if(!body.font.HasCharacter(c,true,true))throw new Exception("Missing dialogue glyph "+currentTextId+" U+"+((int)c).ToString("X4"));
                    capturedLine=currentTextId;RenderTalkCamera();Canvas.ForceUpdateCanvases();camera.Render();
                    SaveStoryTarget(target,Path.Combine(directory,"awakening-"+currentTextId+".png"));
                }
                if(dialogueTiming.Holding && dialogueHoldElapsed>.4f)AdvanceEpisode();
            }
            if(showingChoices && !originalUi.Busy){choicesSeen++;originalUi.Select(0);}
            yield return new WaitForSecondsRealtime(.1f);
        }
        if(!storyMenu.Visible)throw new Exception("Awakening stalled at "+currentGroup+" / "+currentTextId);
        if(episodeLines!=73 || choicesSeen!=30 || originalUi.EpisodeLogCount!=103 || timelineFrames.Count!=2 || videosSeen.Count!=10)
            throw new Exception("Incomplete Awakening coverage lines="+episodeLines+" choices="+choicesSeen+" timelines="+timelineFrames.Count+" videos="+videosSeen.Count);
        if(globalStoryEffects.Count!=0 || ambienceAudio.isPlaying || cinematicPlaying)throw new Exception("Awakening resources leaked into menu");
        Debug.Log("STORY_AWAKENING_OK lines="+episodeLines+" choices="+choicesSeen+" logRows="+originalUi.EpisodeLogCount+" timelines="+timelineFrames.Count+" videos="+videosSeen.Count);
    }
}

#endif
