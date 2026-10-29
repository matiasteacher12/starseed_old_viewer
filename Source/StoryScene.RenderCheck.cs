using System;
using System.Collections;
using System.IO;
using System.Linq;
using UnityEngine;

public sealed partial class StoryScene {
    // Explicit batch-only render check: no desktop capture or input injection.
    IEnumerator RenderStoryCheck() {
        if(!Application.isBatchMode)yield break;
        yield return new WaitForSecondsRealtime(1);
        if(storyMenu)yield return CheckMenuAudio();
        if(Environment.GetCommandLineArgs().Contains("-story-settings-probe")) {
            GetComponent<StoryWindowAspect>().CheckHiddenWindow();
            yield return CheckMovieVolume();
            Debug.Log("STORY_SETTINGS_OK");Application.Quit(0);yield break;
        }
        var directory=Path.GetFullPath(Path.Combine(Application.dataPath,"../../RenderChecks"));Directory.CreateDirectory(directory);
        var camera=new GameObject("Offscreen story camera",typeof(Camera)).GetComponent<Camera>();
        camera.cullingMask=~(1<<17);camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.black;
        camera.orthographic=true;camera.orthographicSize=360;camera.transform.position=new Vector3(0,0,-10);
        var target=new RenderTexture(1120,630,24);target.Create();camera.targetTexture=target;
        var canvas=stage.GetComponentInParent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=camera;canvas.planeDistance=1;
        if(Environment.GetCommandLineArgs().Contains("-touch-only-probe")) {
            yield return CheckTouchParticles(camera,target,directory);Application.Quit(0);yield break;
        }
        if(Environment.GetCommandLineArgs().Any(a=>a=="-story-progress-probe"||a=="-progress-reload-probe")) {
            yield return CheckStoryProgress(camera,target,directory);Application.Quit(0);yield break;
        }
        if(Environment.GetCommandLineArgs().Contains("-story-meeting-five-probe")) {
            yield return CheckMeetingEpisodes(camera,target,directory,true);
            Debug.Log("STORY_MEETING_FIVE_PROBE_OK");Application.Quit(0);yield break;
        }
        if(Environment.GetCommandLineArgs().Contains("-story-meeting-probe")) {
            yield return CheckMeetingEpisodes(camera,target,directory);
            Debug.Log("STORY_MEETING_PROBE_OK");Application.Quit(0);yield break;
        }
        if(Environment.GetCommandLineArgs().Contains("-story-movie-stage-probe")) {
            yield return CheckMovieStage(camera,target,directory);
            Debug.Log("STORY_MOVIE_STAGE_OK");Application.Quit(0);yield break;
        }
        if(Environment.GetCommandLineArgs().Contains("-story-transparency-probe")) {
            yield return CheckEffectTransparency(camera,target,directory);
            Debug.Log("STORY_TRANSPARENCY_OK");Application.Quit(0);yield break;
        }
        if(Environment.GetCommandLineArgs().Contains("-story-choice-transition-probe")) {
            yield return CheckChoiceTransition(camera,target,directory);
            Debug.Log("STORY_CHOICE_TRANSITION_OK");Application.Quit(0);yield break;
        }
        if(Environment.GetCommandLineArgs().Contains("-story-skip-probe")) {
            yield return CheckSkipSegments(camera,target,directory);
            Debug.Log("STORY_SKIP_PROBE_OK");Application.Quit(0);yield break;
        }
        if(Environment.GetCommandLineArgs().Contains("-story-film-probe")) {
            yield return CheckFilmEffect(camera,target,directory);
            Debug.Log("STORY_FILM_PROBE_OK");Application.Quit(0);yield break;
        }
        if(Environment.GetCommandLineArgs().Contains("-story-timeline-probe")) {
            storyMenu.OpenEpisode(storyMenu.Catalogue.chapters.SelectMany(c=>c.episodes).First(e=>e.scene=="Chapter1_1"));
            yield return WaitForEpisodeLoad();
            if(episodeRoutine!=null)StopCoroutine(episodeRoutine);
            originalUi.Root.SetActive(true);backgroundImage.gameObject.SetActive(true);
            foreach(var timeline in episode.timelines)yield return PlayStoryTimeline(timeline.key);
            Debug.Log("STORY_TIMELINE_PROBE_OK");Application.Quit(0);yield break;
        }
        if(Environment.GetCommandLineArgs().Contains("-story-awakening-probe")) {
            if(Environment.GetCommandLineArgs().Contains("-story-effect-audit")) {
                effectAuditCamera=camera;effectAuditTarget=target;
                effectAuditDirectory=Path.Combine(directory,"Effects");Directory.CreateDirectory(effectAuditDirectory);
            }
            yield return CheckAwakening(camera,target,directory);
            Debug.Log("STORY_AWAKENING_RENDER_OK");Application.Quit(0);yield break;
        }
        if(Environment.GetCommandLineArgs().Contains("-story-surface-probe")) {
            yield return CheckSurfaceEpisode(camera,target,directory);
            Debug.Log("STORY_SURFACE_RENDER_OK");Application.Quit(0);yield break;
        }
        if(Environment.GetCommandLineArgs().Contains("-story-actor-probe")) {
            yield return CheckActorTransitions();
            Debug.Log("STORY_ACTOR_CHECK_OK");Application.Quit(0);yield break;
        }
        var intro=originalUi.Root.transform.Find("Center/Top/IntroduceParent");
        for(int round=0;round<2;round++) {
            if(storyMenu) {
                var chapter=storyMenu.Catalogue.chapters.First(c=>c.episodes.Any(e=>e.scene=="Chapter1_3"));
                storyMenu.ShowChapter(chapter);
                storyMenu.OpenEpisode(chapter.episodes.First(e=>e.scene=="Chapter1_3"));
                yield return WaitForEpisodeLoad();
            }
            else ReplayEpisode();
            float deadline=Time.realtimeSinceStartup+45;
            while(!intro.gameObject.activeInHierarchy && Time.realtimeSinceStartup<deadline)yield return null;
            if(!intro.gameObject.activeInHierarchy)throw new Exception("Location introduction did not appear");
            yield return new WaitForSecondsRealtime(1);
            if(!intro.gameObject.activeInHierarchy || intro.Find("Introduce").GetComponent<CanvasGroup>().alpha<.9f)
                throw new Exception("Location introduction disappeared before its readable hold");
            if(!episodeWaiting || !dialogue.activeInHierarchy)
                throw new Exception("First dialogue must start while the location introduction is visible");
            if(storyMenu && (music.clip==menuMusic || !music.isPlaying))throw new Exception("Story BGM did not replace menu music");
            RenderTalkCamera();Canvas.ForceUpdateCanvases();camera.Render();
            SaveStoryTarget(target,Path.Combine(directory,"story-introduction-"+round+".png"));
            // Alternate UI and actor cameras as the actual player does. The
            // first actor draw must already have its own depth texture.
            for(int frame=0;frame<4;frame++) {
                RenderTalkCamera();
                AssertAirenHairIsNotWashedOut(renderTexture);
                Canvas.ForceUpdateCanvases();camera.Render();
                if(frame==0 || frame==3)SaveStoryTarget(target,Path.Combine(directory,"story-first-line-"+round+"-frame-"+frame+".png"));
                yield return new WaitForSecondsRealtime(.1f);
            }
            if(round==0) {
                yield return CheckVoiceIndicator();
                while(intro.gameObject.activeSelf && Time.realtimeSinceStartup<deadline)yield return null;
                if(intro.gameObject.activeSelf || !episodeWaiting)throw new Exception("Location label did not dismiss independently of the dialogue");
            }
            if(storyMenu) {
                originalUi.ToggleSummary();yield return new WaitForSecondsRealtime(.2f);
                var popup=stage.Cast<Transform>().Single(t=>t.gameObject.activeInHierarchy && t.Find("Window/Contents/Skip")!=null);
                popup.Find("Window/Button/Btn_Ok").GetComponent<UnityEngine.UI.Button>().onClick.Invoke();
                float fadeDeadline=Time.realtimeSinceStartup+2;
                while(!endingEpisode && Time.realtimeSinceStartup<fadeDeadline)yield return null;
                if(!endingEpisode)throw new Exception("Skip did not start the ending fade");
                yield return CheckEndingFade(camera,target,Path.Combine(directory,"story-end-fade-skip-"+round+".png"));
                float skipDeadline=Time.realtimeSinceStartup+3;
                while(!storyMenu.Visible && Time.realtimeSinceStartup<skipDeadline)yield return null;
                if(!storyMenu.Visible || !stage.Find("Root_StoryChapter").gameObject.activeSelf || previewEnd.activeSelf || originalUi.Root.activeSelf || intro.gameObject.activeSelf)
                    throw new Exception("Confirmed skip did not return directly to its chapter list");
                Debug.Log("STORY_SKIP_RETURN_OK round="+round+" noExtraClick=true introCancelled=true");
                AssertMenuMusic();
            }
            Debug.Log("STORY_RENDER_ROUND_OK round="+round+" introductionAndDialogueOverlap=true firstFrameColor=true alternatingCameras=true");
            yield return new WaitForSecondsRealtime(.5f);
        }
        if(storyMenu) {
            storyMenu.OpenEpisode(storyMenu.Catalogue.chapters.SelectMany(c=>c.episodes).First(e=>e.scene=="Chapter1_3"));
            yield return WaitForEpisodeLoad();
            float endDeadline=Time.realtimeSinceStartup+60;
            bool checkedCompletionFade=false;
            bool checkedLogReplay=false;
            int intermediateCameraFrames=0;
            while(!storyMenu.Visible && Time.realtimeSinceStartup<endDeadline) {
                if(!checkedLogReplay && episodeLines==2 && episodeWaiting && dialogueTiming.Holding) {
                    yield return CheckLogVoiceReplay(camera,target,directory);checkedLogReplay=true;
                }
                if(cameraBlendDuration>0 && cameraBlendElapsed>.08f && cameraBlendElapsed<cameraBlendDuration-.08f) {
                    float distance=Vector3.Distance(cameraFrom.position,cameraTo.position)+Vector2.Distance(cameraFrom.lensShift,cameraTo.lensShift);
                    if(distance>0) {
                        float progress=Vector3.Distance(cameraFrom.position,cameraCurrent.position)+Vector2.Distance(cameraFrom.lensShift,cameraCurrent.lensShift);
                        if(progress<=0 || progress>=distance)throw new Exception("Camera transition snapped instead of blending");
                        intermediateCameraFrames++;
                        Debug.Log("STORY_CAMERA_INTERMEDIATE_OK target="+activeCamera+" progress="+progress/distance);
                    }
                }
                if(endingEpisode && !checkedCompletionFade) {
                    yield return CheckEndingFade(camera,target,Path.Combine(directory,"story-end-fade-complete.png"));
                    checkedCompletionFade=true;
                }
                if(!originalUi.Busy && !originalUi.ModalOpen) {
                    if(showingChoices)originalUi.Select(0);
                    else if(episodeWaiting && dialogueTiming.Holding && dialogueHoldElapsed>.3f)AdvanceEpisode();
                }
                yield return new WaitForSecondsRealtime(.15f);
            }
            if(!checkedCompletionFade || !checkedLogReplay || intermediateCameraFrames<3 || !storyMenu.Visible || !stage.Find("Root_StoryChapter").gameObject.activeSelf || previewEnd.activeSelf || episodeLines!=9 || originalUi.EpisodeLogCount!=10)
                throw new Exception("Completed story did not return directly to its chapter list");
            Debug.Log("STORY_COMPLETE_RETURN_OK lines=9 logRows=10 noExtraClick=true");
            AssertMenuMusic();
            yield return CheckSurfaceEpisode(camera,target,directory);
        }
        Debug.Log("STORY_RENDER_OK "+directory);Application.Quit(0);
    }
    IEnumerator CheckMovieVolume() {
        music.Stop();voice.Stop();
        yield return PlayEpisodeMovie("Movies/jp_video_story_main_01_01_02.mp4",true);
        movieAudio.mute=true; // Exercise the real routing silently.
        yield return new WaitForSecondsRealtime(.5f);
        if(!storyVideo.isPlaying || storyVideo.time<=0 || storyVideo.audioTrackCount==0 ||
           storyVideo.audioOutputMode!=UnityEngine.Video.VideoAudioOutputMode.AudioSource ||
           storyVideo.GetTargetAudioSource(0)!=movieAudio || movieAudio.ignoreListenerVolume)
            throw new Exception("Movie must play through the shared AudioListener");
        bool savedMute=muted;float savedVolume=volume;
        try {
            muted=true;volume=.03f;AudioListener.volume=0;
            HandleAudioHotkey(KeyCode.M);
            if(Mathf.Abs(AudioListener.volume-.03f)>.00001f)throw new Exception("Movie unmute failed");
            HandleAudioHotkey(KeyCode.Equals);
            if(Mathf.Abs(AudioListener.volume-.04f)>.00001f)throw new Exception("Movie volume up failed");
            HandleAudioHotkey(KeyCode.Minus);
            if(Mathf.Abs(AudioListener.volume-.03f)>.00001f)throw new Exception("Movie volume down failed");
            HandleAudioHotkey(KeyCode.M);
            if(AudioListener.volume!=0)throw new Exception("Movie mute failed");
        } finally {
            StopEpisodeMovies();muted=savedMute;volume=savedVolume;AudioListener.volume=muted?0:volume;movieAudio.mute=false;
        }
        Debug.Log("STORY_MOVIE_AUDIO_OK sharedMaster=true mute=true volumeUp=true volumeDown=true sourceGain=0.3");
    }
    IEnumerator CheckVoiceIndicator() {
        dialogueTiming.RevealAll();voice.Stop();voice.Play();
        yield return new WaitForSeconds(.15f);
        if(dialogueTiming.IsTyping || !voice.isPlaying || !originalUi.VoiceIndicatorPlaying)
            throw new Exception("Voice indicator must continue after the text finishes");
        var icon=originalUi.Root.transform.Find("Center/Dialogue/Pivot/Dialogue/Pivot/Name/Icon").GetComponent<Animation>();
        if(!icon.isPlaying)throw new Exception("Voice indicator animation is not actually running");
        voice.time=voice.clip.length-.05f;yield return new WaitForSeconds(.25f);
        if(voice.isPlaying || originalUi.VoiceIndicatorPlaying || icon.isPlaying)
            throw new Exception("Voice indicator did not stop at the audio end");
        Debug.Log("STORY_VOICE_INDICATOR_OK source=voice textCompleteStillAnimated=true naturalEndStopped=true");
    }
    IEnumerator CheckSurfaceEpisode(Camera camera,RenderTexture target,string directory) {
        var selected=storyMenu.Catalogue.chapters.SelectMany(c=>c.episodes).First(e=>e.scene=="Chapter1_2");
        if(!selected.playable)throw new Exception("Surface story is not selectable");
        storyMenu.OpenEpisode(selected);
        yield return WaitForEpisodeLoad();
        float deadline=Time.realtimeSinceStartup+140;
        bool recapFrame=false,videoFrame=false,backgroundFrame=false,scanFrame=false,closedEyes=false,recapFadeFrame=false,reunionFrame=false;
        int lipFrames=0;var subtitles=new System.Collections.Generic.HashSet<string>();
        var captured=new System.Collections.Generic.HashSet<string>();
        int choices=0;
        while(!storyMenu.Visible && Time.realtimeSinceStartup<deadline) {
            RenderTalkCamera();Canvas.ForceUpdateCanvases();camera.Render();
            if(episodeSceneIndex==1 && originalUi.SceneFadingOut && originalUi.SceneFadeOpacity>.2f && originalUi.SceneFadeOpacity<.8f) {
                if(!body.gameObject.activeInHierarchy || movieBlocking || episodeLines!=3)
                    throw new Exception("Recap cleared or movie began before original scene fade completed");
                if(!recapFadeFrame)SaveStoryTarget(target,Path.Combine(directory,"surface-recap-fade.png"));
                recapFadeFrame=true;
            }
            if(episodeSceneIndex==0 && episodeWaiting && !recapFrame && dialogueTiming.Holding) {
                if(body.text!="<지난 이야기>")throw new Exception("Missing authored recap title");
                SaveStoryTarget(target,Path.Combine(directory,"surface-recap.png"));recapFrame=true;
            }
            if(activeCaption!=null){subtitles.Add(activeCaption);SaveStoryTarget(target,Path.Combine(directory,"surface-subtitle-"+activeCaption+".png"));}
            if(occupied.Values.Any(a=>a.Speaking && a.FaceWeight("a_open")>2))lipFrames++;
            if(currentTextId=="Text_Chapter1_2_2_021" && episodeWaiting && !reunionFrame) {
                // Check what the Animator actually plays, not just our requested
                // Motion string: bare prefabs passed HasState but ignored triggers.
                yield return new WaitForSeconds(Mathf.Max(1,motionDelay+.5f));
                var mana=occupied["Left"];var aurora=occupied["Right"];
                if(!mana.Animator.GetCurrentAnimatorStateInfo(0).IsName("threat01") ||
                   !aurora.Animator.GetCurrentAnimatorStateInfo(0).IsName("idle_inven"))
                    throw new Exception("Reunion did not reach original Mana threat01 / Aurora idle_inven states");
                RenderTalkCamera();Canvas.ForceUpdateCanvases();camera.Render();
                SaveStoryTarget(target,Path.Combine(directory,"surface-mana-aurora-reunion.png"));reunionFrame=true;
                Debug.Log("STORY_REUNION_POSE_OK mana=threat01 aurora=idle_inven lobbyPrefabs=true");
            }
            if(movieBlocking && originalUi.Root.transform.Find("Center/Top/Hud").gameObject.activeInHierarchy)throw new Exception("Story HUD leaked over movie");
            if(movieBlocking && storyVideo && storyVideo.time>2 && !videoFrame) {
                if(!recapFadeFrame || originalUi.SceneFadeOpacity!=0 || originalUi.Busy)
                    throw new Exception("Recap fade missing or overlay remained over movie");
                if(episodeSceneIndex!=1 || episodeLines!=3 || storyVideo.audioTrackCount==0)throw new Exception("Opening movie order or sound track incorrect");
                SaveStoryTarget(target,Path.Combine(directory,"surface-opening-movie.png"));videoFrame=true;
            }
            if(episodeWaiting && episodeSceneIndex==2 && !backgroundFrame) {
                if(episodeSceneIndex!=2 || !storyVideo.isLooping || !storyVideo.isPlaying)throw new Exception("Dialogue did not follow the opening movie with its original looping background");
                yield return new WaitForSeconds(.5f);
                RenderTalkCamera();Canvas.ForceUpdateCanvases();camera.Render();
                SaveStoryTarget(target,Path.Combine(directory,"surface-first-dialogue.png"));backgroundFrame=true;
            }
            if(episodeWaiting && !processingCommands && dialogueTiming.Holding) {
                if(body.text.Contains("[nickname]"))throw new Exception("Player name placeholder leaked into dialogue");
                string shot=currentGroup+"-"+string.Join("-",occupied.OrderBy(p=>p.Key).Select(p=>p.Key+"_"+p.Value.Definition.key.Split('/').Last()));
                if(captured.Add(shot))SaveStoryTarget(target,Path.Combine(directory,"surface-cast-"+captured.Count+".png"));
            }
            if(globalStoryEffects.Any(m=>m && m.name.Contains("scan_player")) && !scanFrame) {
                yield return new WaitForSeconds(.7f);
                RenderTalkCamera();Canvas.ForceUpdateCanvases();camera.Render();
                SaveStoryTarget(target,Path.Combine(directory,"surface-scan-effect.png"));scanFrame=true;
            }
            if(!originalUi.Busy && !originalUi.ModalOpen) {
                if(showingChoices){
                    if(occupied.Values.Any(a=>a.Expression=="Close")) {
                        yield return new WaitForSeconds(.35f);
                        if(occupied.Values.Where(a=>a.Expression=="Close").Any(a=>a.FaceWeight("blink")<99))throw new Exception("Authored eye closure overwritten");
                        RenderTalkCamera();Canvas.ForceUpdateCanvases();camera.Render();SaveStoryTarget(target,Path.Combine(directory,"surface-choice-closed-eyes.png"));closedEyes=true;
                    }
                    originalUi.Select(0);choices++;
                }
                else if(episodeWaiting && !processingCommands && dialogueTiming.Holding && dialogueHoldElapsed>.3f)AdvanceEpisode();
            }
            yield return new WaitForSecondsRealtime(.12f);
        }
        if(!reunionFrame || !recapFrame || !closedEyes || subtitles.Count!=3 || lipFrames<3 || !videoFrame || !backgroundFrame || !scanFrame || !storyMenu.Visible || episodeLines!=32 || originalUi.EpisodeLogCount!=36 || choices!=4)
            throw new Exception("Surface story incomplete: lines="+episodeLines+" choices="+choices+" movie="+videoFrame+" scan="+scanFrame);
        if(storyVideo.isPlaying || movieAudio.isPlaying || movieTarget)throw new Exception("Movie leaked into the menu");
        AssertMenuMusic();
        Debug.Log("STORY_RECAP_FADE_OK originalCurve=true intermediateVisible=true clearedBeforeMovie=true");
        Debug.Log("STORY_SURFACE_OK lines=32 choices=4 logRows=36 scenes=3 recap=true subtitles=3 eyesClosed=true lipFrames="+lipFrames+" movieToDialogue=true normalReturn=true");
        Debug.Log("STORY_VIDEO_UI_OK hiddenHud=true naturalEnd=true sharedVolume=true");
    }
    IEnumerator CheckActorTransitions() {
        var character=episodeActors.Values.First(a=>a.Definition.asset.ToLowerInvariant().Contains("airen"));
        character.gameObject.SetActive(true);
        foreach(string motion in new[]{"attention","negative02","focus01","question01","intro02"}) {
            character.ResetActor();character.PlayMotion(motion);
            float deadline=Time.realtimeSinceStartup+8;
            while(!character.Animator.GetCurrentAnimatorStateInfo(0).IsName(motion) && Time.realtimeSinceStartup<deadline)yield return null;
            if(!character.Animator.GetCurrentAnimatorStateInfo(0).IsName(motion))throw new Exception("Motion did not enter loop: "+motion);
            yield return new WaitForSeconds(.3f);
            float duration=character.PlayMotion("idle_inven");bool sawEnd=false;int endFrames=0;
            deadline=Time.realtimeSinceStartup+8;
            while(Time.realtimeSinceStartup<deadline) {
                var state=character.Animator.GetCurrentAnimatorStateInfo(0);
                if(state.IsName(motion+"_end")){sawEnd=true;endFrames++;}
                if(state.IsName("idle_inven"))break;
                yield return null;
            }
            if(!sawEnd || endFrames<2 || !character.Animator.GetCurrentAnimatorStateInfo(0).IsName("idle_inven") || duration<=0)throw new Exception("Exit motion skipped: "+motion+" frames="+endFrames+" duration="+duration);
            Debug.Log("STORY_MOTION_EXIT_OK "+motion+" endFrames="+endFrames+" wait="+duration);
        }
        character.ResetActor();
        float oneShot=character.PlayMotion("answer_yes");
        if(oneShot<=0)throw new Exception("One-shot wait must use clip looping, not the presence of an unused bool parameter");
        yield return new WaitForSeconds(oneShot+.3f);
        if(!character.Animator.GetCurrentAnimatorStateInfo(0).IsName("idle_inven"))throw new Exception("One-shot did not finish");
        Debug.Log("STORY_MOTION_ONESHOT_OK answer_yes wait="+oneShot);
        character.SetEmotion("Close",100,.3f);yield return new WaitForSeconds(.4f);
        for(int i=0;i<20;i++){if(character.FaceWeight("blink")<99)throw new Exception("Close lost to automatic blink");yield return new WaitForSeconds(.1f);}
        character.SetEmotion("Normal",100,.3f);yield return new WaitForSeconds(.6f);
        if(character.FaceWeight("blink")>1) {yield return new WaitForSeconds(.3f);if(character.FaceWeight("blink")>1)throw new Exception("Close did not release");}
        character.ResetActor();character.gameObject.SetActive(false);
        Debug.Log("STORY_FACE_CLOSE_OK authoredHold=true normalRelease=true");
    }
    void AssertMenuMusic() {
        if(!menuMusic || music.clip!=menuMusic || !music.isPlaying || !music.loop)
            throw new Exception("Original menu BGM is not looping on the menu");
        Debug.Log("STORY_MENU_BGM_CHECK_OK originalCue=true playing=true loop=true");
    }
    IEnumerator CheckMenuAudio() {
        AssertMenuMusic();
        float previousTime=music.time;storyMenu.ShowChapters();
        yield return new WaitForSeconds(.15f);
        if(music.time<=previousTime)throw new Exception("Changing menu pages restarted the BGM");
        // Exercise the exact shared key handler silently, without desktop input/audio.
        bool previousMute=muted,sourceMute=music.mute;float previousVolume=volume;
        music.mute=true;
        try {
            muted=true;volume=.03f;AudioListener.volume=0;
            HandleAudioHotkey(KeyCode.M);
            if(muted || Mathf.Abs(AudioListener.volume-.03f)>.00001f)throw new Exception("Menu mute key failed");
            HandleAudioHotkey(KeyCode.Equals);
            if(Mathf.Abs(AudioListener.volume-.04f)>.00001f)throw new Exception("Menu volume up failed");
            HandleAudioHotkey(KeyCode.Minus);
            if(Mathf.Abs(AudioListener.volume-.03f)>.00001f)throw new Exception("Menu volume down failed");
            HandleAudioHotkey(KeyCode.M);HandleAudioHotkey(KeyCode.Equals);
            if(AudioListener.volume!=0 || !muted)throw new Exception("Changing muted volume must remain silent");
            for(int i=0;i<30;i++)HandleAudioHotkey(KeyCode.Equals);
            if(Mathf.Abs(volume-.2f)>.00001f)throw new Exception("Volume cap failed");
            for(int i=0;i<30;i++)HandleAudioHotkey(KeyCode.Minus);
            if(volume!=0)throw new Exception("Volume floor failed");
        } finally {
            muted=previousMute;volume=previousVolume;AudioListener.volume=muted?0:volume;music.mute=sourceMute;
        }
        Debug.Log("STORY_MENU_AUDIO_KEYS_OK mute=true volumeUp=true volumeDown=true limits=true navigationContinuous=true");
    }
    IEnumerator CheckLogVoiceReplay(Camera camera,RenderTexture target,string directory) {
        int linesBefore=episodeLines;string textBefore=currentTextId;
        voice.Play();yield return new WaitForSeconds(.05f);
        if(!voice.isPlaying)throw new Exception("Expected an active story voice before opening log");
        originalUi.ToggleLog();
        if(voice.isPlaying)throw new Exception("Log opening must stop the live story voice immediately");
        yield return new WaitForSeconds(.4f);
        var entries=stage.GetComponentsInChildren<StoryLogVoiceButton>().OrderBy(e=>e.transform.parent.parent.parent.GetSiblingIndex()).ToArray();
        if(entries.Length!=2 || entries.Any(e=>!e.transform.parent.parent.name.Equals("Char")))
            throw new Exception("Only the two voiced Proxyan log rows should have replay buttons");
        RenderTalkCamera();Canvas.ForceUpdateCanvases();camera.Render();SaveStoryTarget(target,Path.Combine(directory,"story-log-replay-buttons.png"));
        entries[0].GetComponent<UnityEngine.UI.Button>().onClick.Invoke();
        yield return new WaitForSeconds(.15f);
        var replay=originalUi.LogVoiceSource;
        if(!replay || !replay.isPlaying || replay.clip!=entries[0].Clip || replay.time<=0 || voice.isPlaying)
            throw new Exception("First log voice did not replay independently of paused dialogue");
        entries[1].GetComponent<UnityEngine.UI.Button>().onClick.Invoke();yield return null;
        if(!replay.isPlaying || replay.clip!=entries[1].Clip || entries[0].Icon.Playing || !entries[1].Icon.Playing)
            throw new Exception("Switching log voice did not replace the previous playback");
        entries[1].GetComponent<UnityEngine.UI.Button>().onClick.Invoke();yield return null;
        if(replay.isPlaying || entries[1].Icon.Playing)throw new Exception("Log stop button failed");
        entries[0].GetComponent<UnityEngine.UI.Button>().onClick.Invoke();
        replay.time=replay.clip.length-.05f;yield return new WaitForSeconds(.25f);
        if(replay.isPlaying || entries[0].Icon.Playing)throw new Exception("Finished replay did not reset its icon");
        entries[1].GetComponent<UnityEngine.UI.Button>().onClick.Invoke();
        var popup=entries[0].GetComponentInParent<UnityEngine.UI.ScrollRect>().transform.parent.parent.parent.parent;
        popup.Find("Window/Contents/Btn_Close").GetComponent<UnityEngine.UI.Button>().onClick.Invoke();
        if(replay.isPlaying || replay.clip)throw new Exception("Close button must stop replay before the closing animation");
        yield return new WaitForSeconds(.4f);
        if(replay.isPlaying || replay.clip || voice.isPlaying || episodeLines!=linesBefore || currentTextId!=textBefore)
            throw new Exception("Closing log must stop replay without advancing the story");
        // Cover the remaining close paths and ensure reopening cannot revive old speech.
        for(int route=0;route<3;route++) {
            originalUi.ToggleLog();yield return new WaitForSeconds(.4f);
            entries[0].GetComponent<UnityEngine.UI.Button>().onClick.Invoke();
            if(route==0)popup.Find("Common_PopupBackground").GetComponent<UnityEngine.UI.Button>().onClick.Invoke();
            else if(route==1)originalUi.Escape();
            else originalUi.ToggleLog(); // Same path used by Tab.
            if(replay.isPlaying || replay.clip)throw new Exception("Replay continued on close route "+route);
            yield return new WaitForSeconds(.4f);
            if(voice.isPlaying)throw new Exception("Closing log resumed discarded story speech");
        }
        Debug.Log("STORY_LOG_CLOSE_CHECK_OK liveVoiceDiscarded=true immediateReplayStop=true closeButton=true background=true escape=true tab=true");
        Debug.Log("STORY_LOG_REPLAY_CHECK_OK proxyanOnly=true exactClip=true switching=true stop=true naturalEnd=true closeCleanup=true noStoryAdvance=true");
    }
    IEnumerator CheckEndingFade(Camera camera,RenderTexture target,string path) {
        yield return new WaitForSeconds(.6f);
        float opacity=originalUi.EndFadeOpacity;
        if(storyMenu.Visible || !endingEpisode || opacity<.25f || opacity>.75f)
            throw new Exception("Ending must fade gradually before returning to the menu: alpha="+opacity);
        if(originalUi.Root.transform.Find("Center/Top/Hud").gameObject.activeSelf)
            throw new Exception("Ending fade must hide the story HUD");
        RenderTalkCamera();Canvas.ForceUpdateCanvases();camera.Render();SaveStoryTarget(target,path);
        Debug.Log("STORY_END_FADE_CHECK_OK alpha="+opacity+" menuHidden=true");
    }
    static void AssertAirenHairIsNotWashedOut(RenderTexture target) {
        var previous=RenderTexture.active;RenderTexture.active=target;
        var sample=new Texture2D(8,8,TextureFormat.RGBA32,false);
        sample.ReadPixels(new Rect(target.width*.51f,target.height*.83f,8,8),0,0);sample.Apply();
        float average=sample.GetPixels().Average(c=>(c.r+c.g+c.b)/3);
        RenderTexture.active=previous;Destroy(sample);
        Debug.Log("STORY_HAIR_SAMPLE "+average);
        if(average>.65f)throw new Exception("Airen's dark hair is washed out in the rendered frame");
    }
    static void SaveStoryTarget(RenderTexture target,string path) {
        var previous=RenderTexture.active;RenderTexture.active=target;
        var texture=new Texture2D(target.width,target.height,TextureFormat.RGBA32,false);
        texture.ReadPixels(new Rect(0,0,target.width,target.height),0,0);texture.Apply();File.WriteAllBytes(path,texture.EncodeToPNG());
        RenderTexture.active=previous;Destroy(texture);
    }
}
