using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
public sealed partial class StoryScene {
    [Serializable] internal class OldCheckPlan {public string scene;public string[] textIds;public int[] choices;}
    internal static OldCheckPlan LoadOldCheckPlan() {
        var args=Environment.GetCommandLineArgs();int i=Array.IndexOf(args,"-old-check-plan");
        return i>=0?JsonUtility.FromJson<OldCheckPlan>(File.ReadAllText(args[i+1])):null;
    }
    internal IEnumerator CheckOldEpisode(bool returnToMenu=false,OldCheckPlan suppliedPlan=null,bool quit=true) {
        if(!Application.isBatchMode)yield break;
        yield return WaitForEpisodeLoad();
        var plan=suppliedPlan??LoadOldCheckPlan();int expectedLines=plan?.textIds.Length??45,expectedChoices=plan?.choices.Length??7;
        var folder=Path.GetFullPath(Path.Combine(Application.dataPath,"../../RenderChecks"));Directory.CreateDirectory(folder);
        if(plan!=null){folder=Path.Combine(folder,plan.scene+"-"+string.Join("",plan.choices));Directory.CreateDirectory(folder);}
        var capture=new GameObject("Old story validation camera",typeof(Camera)).GetComponent<Camera>();
        capture.cullingMask=~(1<<17);capture.clearFlags=CameraClearFlags.SolidColor;capture.backgroundColor=Color.black;
        capture.orthographic=true;capture.orthographicSize=360;capture.transform.position=new Vector3(0,0,-10);
        var target=new RenderTexture(1280,720,24);target.Create();capture.targetTexture=target;
        var canvas=stage.GetComponentInParent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=capture;canvas.planeDistance=1;
        Time.timeScale=quit?3:8;
        if(Environment.GetCommandLineArgs().Contains("-old-controls-check")) {
            yield return CheckOldControls();Application.Quit(0);yield break;
        }
        if(Environment.GetCommandLineArgs().Contains("-old-ui-only")) {
            if(episodeRoutine!=null)StopCoroutine(episodeRoutine);
            StopEpisodeMovies();originalUi.ResetView();originalUi.ResetEpisodeTitle();
            episodeRoutine=StartCoroutine(RunGroup(episode.entry,2));
        }
        if(Environment.GetCommandLineArgs().Contains("-old-timeline-only")) {
            if(episodeRoutine!=null)StopCoroutine(episodeRoutine);
            yield return PlayStoryTimeline(episode.timelines[0].key);
            Debug.Log("OLD_TIMELINE_CHECK_OK");Application.Quit(0);yield break;
        }
        float deadline=Time.realtimeSinceStartup+600,next=0;int choicesMade=0;var captured=new HashSet<string>();
        bool checkVoice=Environment.GetCommandLineArgs().Contains("-recovered-voice-check"),voiceChecked=false;
        while(!(returnToMenu?storyMenu.Visible:previewComplete) && Time.realtimeSinceStartup<deadline) {
            if(checkVoice&&!voiceChecked&&currentTextId=="Text_Chapter1_8_2_027"&&episodeWaiting&&!processingCommands&&!originalUi.Busy) {
                yield return CheckRecoveredVoice();voiceChecked=true;
            }
            if(storyVideo&&storyVideo.isPrepared&&storyVideo.canSetPlaybackSpeed)storyVideo.playbackSpeed=3;
            if(Time.realtimeSinceStartup>next) {
                next=Time.realtimeSinceStartup+(quit?.3f:.04f);
                if(showingChoices && !originalUi.Busy) {
                    if(choicesMade==0) {
                        RenderTalkCamera();Canvas.ForceUpdateCanvases();capture.Render();SaveStoryTarget(target,Path.Combine(folder,"first-choice.png"));
                    }
                    if(choicesMade>=expectedChoices)throw new Exception("Unexpected choice "+currentGroup);
                    int selection=plan?.choices[choicesMade]??0;
                    Debug.Log("OLD_CHECK_CHOICE "+currentGroup);choicesMade++;originalUi.Select(selection);
                } else if(!quit && episodeWaiting && !processingCommands && dialogueTiming.IsTyping && !originalUi.Busy) {
                    // Exercise the ordinary first click to reveal text during route coverage.
                    AdvanceEpisode();
                } else if(episodeWaiting && !processingCommands && dialogueTiming.Holding && !originalUi.Busy && motionDelay<=0) {
                    if(captured.Add(currentGroup)) {
                        RenderTalkCamera();Canvas.ForceUpdateCanvases();capture.Render();SaveStoryTarget(target,Path.Combine(folder,currentGroup+".png"));
                        Debug.Log("OLD_CHECK_CAPTURE "+currentGroup+" text="+currentTextId);
                        if(currentGroup==episode.entry) {
                            if(!body.font || body.textInfo.characterCount==0)throw new Exception("Korean dialogue did not render");
                            originalUi.ToggleLog();yield return new WaitForSeconds(1.2f);
                            if(!originalUi.ModalOpen)throw new Exception("Story log did not open");
                            var popup=stage.Cast<Transform>().First(t=>t.name.StartsWith("Popup_StoryLog"));
                            Debug.Log("OLD_LOG_STATE pos="+popup.position+" scale="+popup.localScale+" groups="+string.Join(";",popup.GetComponentsInChildren<CanvasGroup>(true).Select(g=>g.name+":"+g.alpha+":"+g.gameObject.activeInHierarchy)));
                            Canvas.ForceUpdateCanvases();capture.Render();SaveStoryTarget(target,Path.Combine(folder,"story-log.png"));
                            originalUi.ToggleLog();yield return new WaitForSeconds(.5f);
                            if(originalUi.ModalOpen)throw new Exception("Story log did not close");
                            Debug.Log("OLD_LOG_CHECK_OK");
                            if(Environment.GetCommandLineArgs().Contains("-old-ui-only")){Application.Quit(0);yield break;}
                        }
                    }
                    AdvanceEpisode();
                }
            }
            yield return null;
        }
        if(!(returnToMenu?storyMenu.Visible:previewComplete)||episodeLines!=expectedLines||choicesMade!=expectedChoices||originalUi.EpisodeLogCount!=expectedLines+expectedChoices)throw new Exception("Old episode incomplete: lines="+episodeLines+" choices="+choicesMade+" group="+currentGroup);
        if(checkVoice&&!voiceChecked)throw new Exception("Recovered voice line was not checked");
        Debug.Log("OLD_STORY_PLAYTHROUGH_OK scene="+episode.scene+" lines="+episodeLines+" choices="+choicesMade+" groups="+captured.Count);
        if(returnToMenu) {
            yield return new WaitForSecondsRealtime(1);
            if(!stage.Find("Root_StoryChapter").gameObject.activeSelf || originalUi.Root.activeSelf || (storyVideo&&storyVideo.isPlaying) || movieTarget || music.clip!=StoryContent.Load<AudioClip>("Original/menu-bgm"))throw new Exception("Menu return cleanup failed");
            Canvas.ForceUpdateCanvases();capture.Render();SaveStoryTarget(target,Path.Combine(folder,"runtime-return.png"));
            stage.Find("Root_StoryChapter/Hud/Hud/Left/Btn_Lobby").GetComponent<UnityEngine.UI.Button>().onClick.Invoke();
            if(viewerHome) {
                if(!viewerHome.Visible||storyMenu.Visible)throw new Exception("Viewer home button failed");
                stage.Find("Viewer home/Button 스토리").GetComponent<UnityEngine.UI.Button>().onClick.Invoke();
                if(viewerHome.Visible||!stage.Find("Root_Story").gameObject.activeSelf)throw new Exception("Viewer home story button failed");
            } else if(!stage.Find("Root_Story").gameObject.activeSelf)throw new Exception("Home button failed");
            Debug.Log("OLD_MENU_FLOW_OK scene="+episode.scene+" pendingBlocked=true fullPlayback=true returnedToChapter=true home=true");
        }
        Time.timeScale=1;target.Release();Destroy(target);Destroy(capture.gameObject);
        if(quit)Application.Quit(0);
    }
    IEnumerator CheckOldControls() {
        string previousMovie="";
        for(int i=0;i<2;i++) {
            float deadline=Time.realtimeSinceStartup+45;
            while((!movieBlocking||!storyVideo||!storyVideo.isPrepared||storyVideo.url==previousMovie||storyVideo.time<.5) && Time.realtimeSinceStartup<deadline)yield return null;
            if(!movieBlocking||storyVideo.url==previousMovie)throw new Exception("Movie skip control did not become ready");
            previousMovie=storyVideo.url;
            // Keep the check silent while exercising the real master routing.
            movieAudio.mute=true;HandleAudioHotkey(KeyCode.M);
            if(AudioListener.volume<=0)throw new Exception("Master unmute failed");
            HandleAudioHotkey(KeyCode.M);
            if(AudioListener.volume!=0)throw new Exception("Master mute failed");
            originalUi.ToggleSummary();yield return new WaitForSeconds(1.2f);
            if(!originalUi.ModalOpen)throw new Exception("Movie skip confirmation did not open "+i);
            var popup=stage.Cast<Transform>().First(t=>t.name.StartsWith("Popup_Summary"));
            popup.Find("Window/Button/Btn_Ok").GetComponent<UnityEngine.UI.Button>().onClick.Invoke();
            yield return new WaitForSeconds(1.2f);
            Debug.Log("OLD_VIDEO_SKIP_CONTROL_OK "+i);
        }
        float talkDeadline=Time.realtimeSinceStartup+45;
        while((!episodeWaiting||processingCommands||originalUi.Busy) && Time.realtimeSinceStartup<talkDeadline)yield return null;
        if(!episodeWaiting)throw new Exception("No dialogue after skipping movies");
        originalUi.ToggleSummary();yield return new WaitForSeconds(1.2f);
        if(!originalUi.ModalOpen)throw new Exception("Story skip confirmation did not open");
        stage.Cast<Transform>().First(t=>t.name.StartsWith("Popup_Summary")).Find("Window/Button/Btn_Ok").GetComponent<UnityEngine.UI.Button>().onClick.Invoke();
        yield return new WaitForSeconds(1.2f);
        if(!episodeSkipped)throw new Exception("Story skip did not land on a source boundary");
        ReplayEpisode();
        if(episodeLines!=0||episodeSkipped)throw new Exception("Replay did not reset state");
        Debug.Log("OLD_CONTROLS_CHECK_OK movieSkip=2 storySkip=true replay=true mute=true");
    }
}
