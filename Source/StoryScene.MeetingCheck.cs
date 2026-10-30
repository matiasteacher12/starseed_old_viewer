#if UNITY_EDITOR || VIEWER_BACKEND
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

public sealed partial class StoryScene {
    IEnumerator CheckMeetingEpisodes(Camera camera,RenderTexture target,string directory,bool nextFive=false) {
        var folder=Path.Combine(directory,nextFive?"MeetingFive":"MeetingNext");Directory.CreateDirectory(folder);
        effectAuditCamera=camera;effectAuditTarget=target;
        effectAuditDirectory=Path.Combine(folder,"Effects");Directory.CreateDirectory(effectAuditDirectory);
        foreach(var scene in nextFive?new[]{"Chapter1_7","Chapter1_8","Chapter1_9","Chapter1_10","Chapter1_11"}:new[]{"Chapter1_4","Chapter1_5","Chapter1_6"}) {
            var row=storyMenu.Catalogue.chapters.SelectMany(c=>c.episodes).First(e=>e.scene==scene);
            var source=BackendJson.FromJson<StoryEpisode>(StoryContent.Load<TextAsset>(loadingCatalogue.episodes.Single(e=>e.scene==scene).resource).text);
            var selectionBlocks=source.groups.SelectMany(g=>g.steps).Where(s=>s.commands.Any(c=>c.name=="Select")).ToArray();
            // These three episodes have at most two choices, and all alternatives
            // within each block converge. First+last paths cover every edge.
            if(selectionBlocks.Any(s=>s.commands.Count(c=>c.name=="Select")>2 || s.commands.Where(c=>c.name=="Select").Select(c=>c.args[1]).Distinct().Count()!=1))
                throw new Exception("Meeting test needs explicit divergent routes: "+scene);
            var selected=new HashSet<string>();bool gateCaptured=false,waveCaptured=false;var commands=new HashSet<string>();var movies=new HashSet<string>();
            for(int route=0;route<2;route++) {
                storyMenu.OpenEpisode(row);
                yield return WaitForEpisodeLoad();
                float deadline=Time.realtimeSinceStartup+480;string capturedLine=null;int choicesSeen=0;
                while(!storyMenu.Visible && Time.realtimeSinceStartup<deadline) {
                    if(storyVideo&&storyVideo.isPlaying&&storyVideo.time>.5&&movies.Add(storyVideo.url)) {
                        if(!movieBlocking&&movieSkipRoot&&movieSkipRoot.activeInHierarchy)throw new Exception("Background movie received blocking movie UI");
                        RenderTalkCamera();Canvas.ForceUpdateCanvases();camera.Render();
                        SaveStoryTarget(target,Path.Combine(folder,scene+"-video-"+movies.Count+".png"));
                        Debug.Log("STORY_MEETING_VIDEO "+scene+" "+Path.GetFileName(storyVideo.url)+" frame="+storyVideo.frame);
                    }
                    if(storyUiAnimation&&!gateCaptured) {
                        var animation=storyUiAnimation.GetComponentInChildren<Animation>();
                        if(animation&&animation["Bg_AsniaGate"].time>.45f){Canvas.ForceUpdateCanvases();camera.Render();SaveStoryTarget(target,Path.Combine(folder,"AsniaGate.png"));gateCaptured=true;}
                    }
                    if(movieBlocking&&route==1&&storyVideo&&storyVideo.time>1&&movieSkipRoot.activeSelf) {
                        originalUi.MovieSkipConfirmed.Invoke();
                    }
                    if(episodeWaiting&&!processingCommands&&!originalUi.Busy&&dialogueTiming.Holding&&motionDelay<=0) {
                        if(currentTextId!=capturedLine) {
                            capturedLine=currentTextId;
                            if(currentTextId=="Text_Chapter1_9_002") {
                                var wave=body.GetComponent<StoryGlyphWave>();
                                if(!wave||!wave.enabled||wave.MaximumOffset<.1f)throw new Exception("Wave glyph animation missing");
                                waveCaptured=true;
                            }
                            foreach(char ch in body.text.Where(c=>!char.IsWhiteSpace(c)))if(!body.font.HasCharacter(ch,true,true))throw new Exception("Missing glyph "+currentTextId);
                            if(route==0) {
                                RenderTalkCamera();Canvas.ForceUpdateCanvases();camera.Render();
                                SaveStoryTarget(target,Path.Combine(folder,currentTextId+".png"));
                            }
                            Debug.Log("STORY_MEETING_LINE "+scene+" route="+route+" text="+currentTextId+" camera="+activeCamera+" closeUp="+closeUpSlot);
                        }
                        if(dialogueHoldElapsed>.3f)AdvanceEpisode();
                    }
                    if(showingChoices&&!originalUi.Busy) {
                        int choice=route==0?0:pendingChoices.Length-1;
                        selected.Add(pendingChoices[choice].args[0]);choicesSeen++;
                        if(route==0){Canvas.ForceUpdateCanvases();camera.Render();SaveStoryTarget(target,Path.Combine(folder,scene+"-choice-"+choicesSeen+".png"));}
                        originalUi.Select(choice);
                    }
                    yield return new WaitForSecondsRealtime(.1f);
                }
                if(!storyMenu.Visible)throw new Exception("Meeting stalled: "+scene+" "+currentGroup+" "+currentTextId);
                if(episodeLines!=source.dialogueCount||choicesSeen!=selectionBlocks.Length||originalUi.EpisodeLogCount!=episodeLines+choicesSeen)
                    throw new Exception("Incomplete meeting route "+scene+" lines="+episodeLines+" choices="+choicesSeen+" logs="+originalUi.EpisodeLogCount);
                if(globalStoryEffects.Count!=0||storyUiAnimation||storyBackground||storyVideo&&storyVideo.isPlaying||voice.isPlaying)
                    throw new Exception("Meeting resources leaked into menu "+scene);
                Debug.Log("STORY_MEETING_ROUTE_OK "+scene+" route="+route+" lines="+episodeLines+" choiceBlocks="+choicesSeen);
            }
            int expectedOptions=selectionBlocks.Sum(s=>s.commands.Count(c=>c.name=="Select"));
            int expectedMovies=source.groups.SelectMany(g=>g.steps).SelectMany(s=>s.commands).Where(c=>!string.IsNullOrEmpty(c.video)).Select(c=>c.video).Distinct().Count();
            if(selected.Count!=expectedOptions||movies.Count!=expectedMovies)throw new Exception("Uncovered options/media "+scene+" options="+selected.Count+" movies="+movies.Count);
            if(scene=="Chapter1_7"&&!gateCaptured||scene=="Chapter1_9"&&!waveCaptured)throw new Exception("New animation not exercised "+scene);
            Debug.Log("STORY_MEETING_EPISODE_OK "+scene+" options="+selected.Count+" movies="+movies.Count);
        }
        if(nextFive)yield return CheckMeetingFiveSkips(camera,target,folder);
        else yield return CheckMeetingSkips(camera,target,folder);
    }
    IEnumerator CheckMeetingSkips(Camera camera,RenderTexture target,string folder) {
        var row=storyMenu.Catalogue.chapters.SelectMany(c=>c.episodes).First(e=>e.scene=="Chapter1_6");
        storyMenu.OpenEpisode(row);
        yield return WaitForEpisodeLoad();
        string anchors=string.Join(",",StorySkipPlan.Build(episode).Select(a=>a.group+":"+a.step));
        if(anchors!="Chapter1_6_03:1,Chapter1_6_05:3")throw new Exception("Wrong new skip anchors: "+anchors);
        for(int skip=0;skip<3;skip++) {
            float deadline=Time.realtimeSinceStartup+35;
                while((!episodeWaiting||processingCommands||originalUi.Busy||establishing)&&Time.realtimeSinceStartup<deadline) {
                    if(showingChoices&&!originalUi.Busy)originalUi.Select(0);
                    yield return null;
                }
            if(!episodeWaiting)throw new Exception("Skip test did not reach dialogue");
            originalUi.ToggleSummary();yield return new WaitForSecondsRealtime(.25f);
            var popup=stage.Cast<Transform>().Single(t=>t.gameObject.activeInHierarchy&&t.Find("Window/Contents/Skip")!=null);
            popup.Find("Window/Button/Btn_Ok").GetComponent<UnityEngine.UI.Button>().onClick.Invoke();
            yield return new WaitForSecondsRealtime(.8f);
            if(skip<2) {
                string expected=skip==0?"Chapter1_6_03":"Chapter1_6_05";
                if(currentGroup!=expected||storyMenu.Visible)throw new Exception("Wrong skip destination "+currentGroup);
                deadline=Time.realtimeSinceStartup+35;
                while((!episodeWaiting||processingCommands||establishing)&&Time.realtimeSinceStartup<deadline) {
                    if(showingChoices&&!originalUi.Busy)originalUi.Select(0);
                    yield return null;
                }
                if(!episodeWaiting)throw new Exception("Skip landing failed to continue");
                RenderTalkCamera();Canvas.ForceUpdateCanvases();camera.Render();SaveStoryTarget(target,Path.Combine(folder,"skip-"+skip+".png"));
            }
            else {
                deadline=Time.realtimeSinceStartup+5;while(!storyMenu.Visible&&Time.realtimeSinceStartup<deadline)yield return null;
                if(!storyMenu.Visible)throw new Exception("Final skip failed to return to menu");
            }
        }
        Debug.Log("STORY_MEETING_SKIP_OK "+anchors);
    }
}

#endif
