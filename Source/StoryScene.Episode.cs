#if UNITY_EDITOR || VIEWER_BACKEND
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

public sealed partial class StoryScene {
    StoryEpisode episode;
    int episodeSceneIndex;
    bool touchBlocked,processingCommands;
    bool explicitClickWait;
    readonly Dictionary<string,StoryEpisodeActor> episodeActors=new Dictionary<string,StoryEpisodeActor>();
    readonly Dictionary<string,StoryEpisodeActor> occupied=new Dictionary<string,StoryEpisodeActor>();
    readonly List<GameObject> moods=new List<GameObject>();
    StoryEpisode.Command[] pendingChoices;
    int choiceContinuationStep;
    Coroutine episodeRoutine,fadeRoutine;
    Coroutine endRoutine;
    bool endingEpisode,episodeSkipped;
    int currentCommandIndex;
    bool episodeWaiting;
    float motionDelay;
    int episodeLines;
    string currentGroup,currentTextId,shotSlot="Center";
    Image episodeFade;
    AudioSource effectsAudio,ambienceAudio;
    bool verifyEpisode;
    int verifyBranch;
    float verifyNext;

    [Serializable] class EpisodeCatalogue {public string[] resources;}
    void InitializeEpisode() {
        episodeFade=Box(stage,"Story transition",new Rect(0,0,1280,720),Color.clear,false).GetComponent<Image>();
        episodeFade.transform.SetSiblingIndex(originalUi.Root.transform.GetSiblingIndex());
        effectsAudio=gameObject.AddComponent<AudioSource>();effectsAudio.volume=.35f;
        ambienceAudio=gameObject.AddComponent<AudioSource>();ambienceAudio.volume=.3f;ambienceAudio.loop=true;
        previewEnd.GetComponent<Text>().text="스토리 끝 · 클릭하거나 R을 눌러 다시 보기";
        verifyEpisode=Environment.GetCommandLineArgs().Contains("-story-verify");
    }
    static float Number(string text,float fallback=0) => float.TryParse(text,NumberStyles.Float,CultureInfo.InvariantCulture,out var v)?v:fallback;
    static Vector3 Offset(string text) {var a=text.Split('&');return new Vector3(Number(a[0]),Number(a[1]),Number(a[2],1));}
    T OriginalAsset<T>(string asset) where T:UnityEngine.Object {
        foreach(var bundle in bundles)if(bundle.GetAllAssetNames().Any(n=>n.Equals(asset,StringComparison.OrdinalIgnoreCase)))return bundle.LoadAsset<T>(asset);
        throw new InvalidOperationException("Missing episode asset "+asset);
    }
    void ReplayEpisode() {
        ClearStoryUiAnimation();
        if(endRoutine!=null){StopCoroutine(endRoutine);endRoutine=null;}
        endingEpisode=false;episodeSkipped=false;currentGroup=episode.entry;currentCommandIndex=-1;
        StopStoryTimeline();StopEpisodeMovies();ClearStoryBackground();ClearGlobalStoryEffects();touchBlocked=false;processingCommands=false;episodeSceneIndex=0;explicitClickWait=false;
        if(episodeRoutine!=null)StopCoroutine(episodeRoutine);
        if(fadeRoutine!=null)StopCoroutine(fadeRoutine);
        if(focusRoutine!=null)StopCoroutine(focusRoutine);
        ClearMoods();occupied.Clear();
        foreach(var a in episodeActors.Values){a.ResetActor();a.gameObject.SetActive(false);}
        originalUi.ResetView();originalUi.BeginEpisodeLog();originalUi.HideIntroduce();originalUi.ResetEpisodeTitle();originalUi.SetEpisodeSummary(episode.title,episode.summary);
        body=originalUi.Body;ready=true;establishing=true;hidden=false;previewComplete=false;showingChoices=false;
        episodeWaiting=false;episodeLines=0;currentLine="";currentTextId="";
        previewEnd.SetActive(false);voice.Stop();music.Stop();effectsAudio.Stop();ambienceAudio.Stop();
        episodeFade.color=Color.clear;SetFocusAmount(0);ResetEpisodeCamera();
        Debug.Log("EPISODE_BEGIN "+episode.id+" branch="+verifyBranch);
        episodeRoutine=StartCoroutine(StartEpisode());
    }
    IEnumerator StartEpisode() {
        var scene=episode.scenes[episodeSceneIndex];
        establishing=true;episodeWaiting=false;processingCommands=true;
        if(scene.hasFade) {
            if(scene.fade!=0)throw new InvalidOperationException("Unimplemented original scene fade "+scene.fade);
            yield return originalUi.PlaySceneFade(true,scene.fadeColor);
        }
        // Original StartScene awaits Out before clearing the previous scene,
        // then awaits In before the title/command runner starts (including Video).
        originalUi.HideCenteredDialogue();dialogue.SetActive(false);
        backgroundImage.texture=Texture2D.blackTexture;
        Debug.Log("EPISODE_SCENE "+episodeSceneIndex+" entry="+scene.entry);
        if(scene.hasFade)yield return originalUi.PlaySceneFade(false,scene.fadeColor);
        if(!string.IsNullOrEmpty(scene.title))yield return originalUi.PlayEpisodeTitle(scene.title,scene.number);
        yield return RunGroup(scene.entry);
    }
    IEnumerator RunGroup(string groupKey,int startStep=0) {
        currentGroup=groupKey;
        var group=episode.groups.First(g=>g.key==groupKey);
        if(episode.missingGroups!=null&&episode.missingGroups.Contains(groupKey))Debug.LogWarning("STORY_SOURCE_GROUP_MISSING "+episode.id+" "+groupKey+"; preserved data ends here");
        int flatIndex=group.steps.Take(startStep).Sum(s=>s.commands.Length);
        for(int step=startStep;step<group.steps.Length;step++) {
            establishing=true;episodeWaiting=false;processingCommands=true;motionDelay=0;
            ClearMoods();
            var commands=group.steps[step].commands;
            var talk=commands.FirstOrDefault(c=>c.name=="Talk");
            var lineVoice=commands.FirstOrDefault(c=>c.name=="Voice");
            string speaker="",portrait="";int dialogueMode=0;bool consumedClick=false;
            pendingChoices=commands.Where(c=>c.name=="Select").ToArray();
            originalUi.SetScriptInteraction(talk!=null);
            foreach(var command in commands) {
                currentCommandIndex=flatIndex++;
                var a=command.args;
                switch(command.name) {
                    case "Goto":
                        if(talk!=null&&!consumedClick) {
                            processingCommands=false;establishing=false;
                            while(episodeWaiting)yield return null;
                            voice.Stop();
                        }
                        yield return RunGroup(a[0]);yield break;
                    case "SilentTalk": originalUi.HideAllDialogue();originalUi.SetRevealing(false);voice.Stop();break;
                    case "Video":
                        ClearStoryBackground();yield return PlayEpisodeMovie(command.video,false,true,a[2],Number(a[1]));break;
                    case "Timeline":ClearStoryBackground();yield return PlayStoryTimeline(a[0]);break;
                    case "UIAnimation":yield return PlayStoryUiAnimation(a[0],a[1]);break;
                    case "Scanline":SetStoryScanline(a[0],a[1]=="True");break;
                    case "Image":originalUi.ShowCenterImage(command.resource);break;
                    case "TextAnimation":originalUi.PlayGlyphWave(body,a[0],a[1]);break;
                    case "BackgroundBlur":ApplyStoryZoom(a[0]=="True"?"explicit blur":null);break;
                    case "SetPastScene":originalUi.SetPastScene(a[0]=="True",(int)Number(a[1]));break;
                    case "BlockTouch":touchBlocked=a[0]=="True";break;
                    case "GlobalEffect":ShowGlobalStoryEffect(a[0],(int)Number(a[2]));
                        foreach(var clip in command.audio)effectsAudio.PlayOneShot(StoryContent.Load<AudioClip>(clip));break;
                    case "ReleaseEffect":ClearGlobalStoryEffects();ClearMoods();break;
                    case "SkipBlock":Debug.Log("STORY_SKIP_BOUNDARY "+groupKey);break;
                    case "StartTalk_Center":case "StartTalkKeep_Center":
                        dialogueMode=1;body=originalUi.PrepareDialogue("","",1,Number(a[0],24),command.name=="StartTalkKeep_Center");break;
                    case "StartTalk":case "StartTalk_Portrait":
                        speaker=command.speaker;portrait=a[1];dialogueMode=command.name=="StartTalk_Portrait"?2:0;
                        body=originalUi.PrepareDialogue(speaker,portrait,dialogueMode,Number(a[2],24),false);break;
                    case "TextFade":case "ShakeText":originalUi.PlayTextEffect(a[0]);break;
                    case "BounceText":originalUi.PlayTextEffect("Dialogue_Bounce_01");break;
                    case "ShakeScreen":StartStoryShake(a[0]);break;
                    case "ClickWait":
                        explicitClickWait=true;processingCommands=false;establishing=false;episodeWaiting=true;
                        while(episodeWaiting)yield return null;
                        explicitClickWait=false;processingCommands=true;consumedClick=true;break;
                    case "SetCharacter":case "SetCharacterFast":SetEpisodeCharacter(a[0],a[1],a[2]);break;
                    case "Light":
                        foreach(var pair in occupied)pair.Value.Brightness=Number(a[Array.IndexOf(new[]{"Left","Center","Right"},pair.Key)],1);
                        break;
                    case "CameraMove":MoveEpisodeCamera(a[0]);break;
                    case "HighlightMove":MoveHighlight(a[0]=="True",a[0]=="True"?Offset(a[1]):Vector3.zero,Number(a[2]));break;
                    case "CloseUp":CloseUpEpisodeCamera(a[0]);break;
                    case "Emotion": {
                        var values=a[2].Split('&');
                        foreach(var target in occupied.Where(p=>a[0]=="All"||p.Key==a[0]).Select(p=>p.Value))target.SetEmotion(a[1],Number(values[0]),Number(values.ElementAtOrDefault(1),.3f));break;
                    }
                    case "Animation": {
                        // Root_TalkScene.PlayAnimation returns zero when TryGetValue
                        // finds no character (also authored after video transitions).
                        var motionActors=occupied.Where(p=>a[0]=="All"||p.Key==a[0]).Select(p=>p.Value).ToArray();
                        if(motionActors.Length==0){Debug.Log("STORY_EMPTY_ANIMATION "+currentGroup+" slot="+a[0]);break;}
                        float duration=motionActors.Max(actorEntry=>actorEntry.PlayMotion(a[1]));
                        // Third argument is wait-for-animation, not loop or look-at.
                        // Original Process stores max(nextDelay, animation duration).
                        if(a[2]=="True")motionDelay=Mathf.Max(motionDelay,duration);
                        break;
                    }
                    case "Makeup": {
                        var values=a[2].Split('&');if(occupied.TryGetValue(a[0],out var makeupActor))makeupActor.SetMakeup(int.Parse(a[1]),values[0]=="True",Number(values.ElementAtOrDefault(1),.3f));break;
                    }
                    case "PivotAnimation":
                        // CharacterRenderCamera.PlayPivotAnim checks the loaded
                        // character array for null before touching its parent.
                        if(occupied.TryGetValue(string.IsNullOrEmpty(a[0])?"Left":a[0],out var pivotActor))pivotActor.PlayPivot(episode.pivots.First(p=>p.key==a[1]),Number(a[2],1));
                        else Debug.Log("STORY_EMPTY_PIVOT "+currentGroup+" slot="+a[0]+" animation="+a[1]);
                        break;
                    case "Mood":ShowEpisodeMood(a[0],a[1],a[2]);break;
                    case "ZoomIn":ZoomEpisodeCamera(a[0]);break;
                    case "Voice":
                        foreach(var actorEntry in episodeActors.Values)actorEntry.Speaking=false;
                        if(command.audioMode=="missing-source-cue") {voice.Stop();Debug.LogWarning("STORY_SOURCE_VOICE_MISSING "+episode.id+" "+a[1]);break;}
                        if(occupied.TryGetValue(a[0],out var voiceActor))voiceActor.Speaking=true;
                        voice.Stop();voice.clip=StoryContent.Load<AudioClip>(command.audio[0]);
                        if(!voice.clip)throw new InvalidOperationException("Missing voice "+command.audio[0]);voice.Play();break;
                    case "SE":
                        if(string.IsNullOrEmpty(a[0])||string.IsNullOrEmpty(a[1])){effectsAudio.Stop();break;}
                        if(command.audio.Length==0)throw new InvalidOperationException("Missing SE "+a[1]);
                        if(command.audioMode=="simultaneous")foreach(var clip in command.audio)effectsAudio.PlayOneShot(StoryContent.Load<AudioClip>(clip));
                        else effectsAudio.PlayOneShot(StoryContent.Load<AudioClip>(command.audio[UnityEngine.Random.Range(0,command.audio.Length)]));break;
                    case "BGM":music.Stop();if(command.audio.Length>0){music.clip=StoryContent.Load<AudioClip>(command.audio[0]);music.Play();}break;
                    case "Ambience":ambienceAudio.Stop();if(command.audio.Length>0){ambienceAudio.clip=StoryContent.Load<AudioClip>(command.audio[0]);ambienceAudio.Play();}break;
                    case "SetBackground":
                        StopEpisodeMovies();ClearStoryBackground();
                        if(!string.IsNullOrEmpty(command.video))yield return PlayEpisodeMovie(command.video,a[2]!="False",false);
                        else if(command.resource.EndsWith(".prefab"))ShowStoryBackground(command.resource);
                        else {backgroundImage.texture=string.IsNullOrEmpty(command.resource)?Texture2D.blackTexture:StoryContent.Load<Texture2D>(command.resource);SetFocusAmount(focusAmount);}
                        break;
                    case "FadeIn":case "FadeOut":
                        if(fadeRoutine!=null)StopCoroutine(fadeRoutine);
                        var color=Color.black;if(!string.IsNullOrEmpty(a[0]))ColorUtility.TryParseHtmlString("#"+a[0],out color);fadeRoutine=StartCoroutine(EpisodeFade(color,command.name=="FadeOut"?1:0,Number(a[1])));break;
                    case "FadeOutOverlay":yield return originalUi.PlaySceneFade(true,a[0]);break;
                    case "Introduce":originalUi.ShowIntroduce(command.text);break;
                    case "Wait":
                        if(talk!=null && !commands.Any(c=>c.name=="ClickWait") && command==commands.LastOrDefault(c=>c.name=="Wait"))break;
                        // Scene-only transitions continue after their authored delay.
                        // Do not add an extra click gate between dialogue sections.
                        yield return EpisodeDelay(Number(a[0]));
                        break;
                    case "Talk":
                        currentTextId=a[0];currentLine=StoryTextDisplay.PlayerText(StoryTextDisplay.Format(command.text),originalUi.PlayerName);
                        dialogueTiming=new OriginalDialogueTiming(currentLine.Length,Number(a[1],.025f),Number(commands.LastOrDefault(c=>c.name=="Wait")?.args[0],.3f));
                        body.text=currentLine;body.maxVisibleCharacters=0;dialogueHoldElapsed=0;
                        originalUi.AppendLog(speaker,currentLine,portrait,voiceClip:lineVoice?.audio.Length>0?StoryContent.Load<AudioClip>(lineVoice.audio[0]):null);
                        originalUi.SetRevealing(true);
                        episodeWaiting=true;establishing=false;
                        episodeLines++;
                        Debug.Log("EPISODE_LINE "+currentTextId+" speaker="+speaker+" actors="+string.Join(";",occupied.Select(p=>p.Key+":"+p.Value.Definition.key+":"+p.Value.Motion+":"+p.Value.Expression+":"+p.Value.transform.parent.position)));
                        break;
                    case "Select":break;
                    default:throw new InvalidOperationException("Unsupported original command "+command.name);
                }
            }
            processingCommands=false;
            if(pendingChoices.Length>0) {
                choiceContinuationStep=step+1;
                // Select must obey the same authored animation wait as Talk.
                while(motionDelay>0)yield return null;
                originalUi.SetScriptInteraction(true);
                originalUi.ConfigureChoices(pendingChoices.Select(c=>c.text).ToArray());originalUi.ShowChoices();
                showingChoices=true;establishing=false;voice.Stop();
                yield break; // Selection follows the command's explicit target group.
            }
            if(talk!=null && !consumedClick) {
                establishing=false;episodeWaiting=true;
                while(episodeWaiting)yield return null;
                voice.Stop();
            }
        }
        if(++episodeSceneIndex<episode.scenes.Length)yield return StartEpisode();
        else FinishEpisode(false);
    }
    IEnumerator EpisodeDelay(float duration) {
        float elapsed=0;while(elapsed<duration){if(!originalUi.ModalOpen&&!hidden)elapsed+=Time.deltaTime;yield return null;}
    }
    IEnumerator EpisodeFade(Color color,float target,float duration) {
        float from=episodeFade.color.a,elapsed=0;
        do {if(!originalUi.ModalOpen)elapsed+=Time.deltaTime;color.a=Mathf.Lerp(from,target,duration<=0?1:elapsed/duration);episodeFade.color=color;yield return null;}while(elapsed<duration);
    }
    void SetEpisodeCharacter(string slot,string key,string offset) {
        if(occupied.TryGetValue(slot,out var previous)&&previous.Definition.key!=key){previous.gameObject.SetActive(false);occupied.Remove(slot);}
        if(string.IsNullOrEmpty(key))return;
        var character=episodeActors[key];
        foreach(var oldSlot in occupied.Where(p=>p.Value==character && p.Key!=slot).Select(p=>p.Key).ToArray())occupied.Remove(oldSlot);
        if(!character.gameObject.activeSelf)character.ResetActor();
        character.Place(episode.slots.First(s=>s.key==slot),Offset(offset));character.gameObject.SetActive(true);occupied[slot]=character;
    }
    void ShowEpisodeMood(string slot,string key,string bone) {
        if(string.IsNullOrEmpty(key)) {
            if(occupied.TryGetValue(slot,out var owner))foreach(var mood in moods.Where(m=>m&&m.transform.IsChildOf(owner.transform)).ToArray()){moods.Remove(mood);Destroy(mood);}
            return;
        }
        if(!occupied.TryGetValue(slot,out var moodActor)){Debug.Log("STORY_EMPTY_MOOD "+currentGroup+" slot="+slot);return;}
        var target=moodActor.Bone(bone);if(!target)throw new InvalidOperationException("Missing mood bone "+bone);
        var effect=Instantiate(OriginalAsset<GameObject>(episode.effects.First(e=>e.key==key).asset),target,false);
        ConfigureEffectComposite(effect);
        foreach(var tr in effect.GetComponentsInChildren<Transform>(true))tr.gameObject.layer=17;
        foreach(var ps in effect.GetComponentsInChildren<ParticleSystem>(true))ps.Play();
        moods.Add(effect);
        AuditEffectIfRequested(effect,key);
    }
    void ClearMoods(){foreach(var mood in moods)if(mood)Destroy(mood);moods.Clear();}
    void AdvanceEpisode() {
        if(endingEpisode)return;
        if(!ready||originalUi.ModalOpen||originalUi.Busy)return;
        if(hidden){ToggleHidden();return;}
        if(previewComplete){if(storyMenu){ReturnToStoryMenu(true);return;}ReplayEpisode();return;}
        if(establishing||showingChoices||!episodeWaiting||touchBlocked||processingCommands)return;
        if(dialogueTiming.IsTyping){dialogueTiming.RevealAll();body.maxVisibleCharacters=currentLine.Length;return;}
        if(motionDelay>0&&!explicitClickWait)return;
        episodeWaiting=false;
    }
    void ChooseEpisode(int selected) {
        if(endingEpisode)return;
        if(selected<0){SkipEpisodeSegment();return;}
        string target=pendingChoices[selected].args[1];showingChoices=false;
        Debug.Log("EPISODE_BRANCH choice="+selected+" target="+target);
        // Native Select increments the current script index even with no target.
        // Empty targets continue this group (the new episode's final choice ends it).
        episodeRoutine=StartCoroutine(string.IsNullOrEmpty(target)?RunGroup(currentGroup,choiceContinuationStep):RunGroup(target));
    }
    void FinishEpisode(bool skipped) {
        if(endingEpisode)return;
        skipped|=episodeSkipped;
        episodeWaiting=false;establishing=false;previewComplete=true;showingChoices=false;
        voice.Stop();originalUi.SetVoicePlaying(false);originalUi.SetRevealing(false);originalUi.ResetEpisodeTitle();originalUi.HideIntroduce();choices.SetActive(false);
        Debug.Log("EPISODE_END skipped="+skipped+" lines="+episodeLines+" logRows="+originalUi.EpisodeLogCount);
        if(storyMenu){
            endingEpisode=true;previewComplete=false;
            if(fadeRoutine!=null){StopCoroutine(fadeRoutine);fadeRoutine=null;}
            endRoutine=StartCoroutine(FadeBackToStoryMenu());return;
        }
        dialogue.SetActive(false);ClearMoods();
        previewEnd.SetActive(true);
    }
    void SkipEpisodeSegment() {
        ClearStoryUiAnimation();
        var landing=StorySkipPlan.Next(episode,currentGroup,currentCommandIndex);
        if(episodeRoutine!=null)StopCoroutine(episodeRoutine);
        if(landing==null){FinishEpisode(true);return;}
        // All audited native anchors begin a block (SilentTalk). Do not replay
        // an invented prefix to repair a wrongly interpreted boundary.
        if(landing.command!=0)
            throw new InvalidOperationException("Unreviewed mid-block skip landing: "+landing.group);
        StopStoryTimeline();StopEpisodeMovies();ClearMoods();ClearGlobalStoryEffects();
        if(fadeRoutine!=null){StopCoroutine(fadeRoutine);fadeRoutine=null;}
        voice.Stop();effectsAudio.Stop();ambienceAudio.Stop();
        originalUi.ResetView();originalUi.ResetEpisodeTitle();originalUi.HideIntroduce();
        episodeFade.color=Color.clear;episodeWaiting=false;showingChoices=false;touchBlocked=false;
        explicitClickWait=false;processingCommands=false;previewComplete=false;episodeSkipped=true;
        episodeSceneIndex=landing.scene;body=originalUi.Body;
        Debug.Log("STORY_SKIP_LANDING group="+landing.group+" step="+landing.step+" originalIndex="+landing.flatIndex);
        episodeRoutine=StartCoroutine(RunGroup(landing.group,landing.step));
    }
    IEnumerator FadeBackToStoryMenu() {
        yield return originalUi.PlayEndFade();
        ClearMoods();ClearGlobalStoryEffects();endingEpisode=false;ReturnToStoryMenu(true);endRoutine=null;
    }
    void ReturnToStoryMenu(bool completed=false) {
        ClearStoryUiAnimation();
        if(fadeRoutine!=null){StopCoroutine(fadeRoutine);fadeRoutine=null;}
        if(focusRoutine!=null){StopCoroutine(focusRoutine);focusRoutine=null;}
        originalUi.ResetView();originalUi.Root.SetActive(false);previewEnd.SetActive(false);
        voice.Stop();music.Stop();effectsAudio.Stop();ambienceAudio.Stop();
        StopStoryTimeline();StopEpisodeMovies();ClearStoryBackground();ClearGlobalStoryEffects();
        ClearEpisodeActors();
        backgroundImage.gameObject.SetActive(false);actorImage.gameObject.SetActive(false);
        storyMenu.ReturnFromEpisode(completed);
    }
    void UpdateEpisode() {
        UpdateRenderTexture();ApplyEpisodeShot();
        if(endingEpisode)return;
        if(Input.GetKeyDown(KeyCode.F2)&&!hidden)originalUi.OpenNameSettings();
        if(!originalUi.NameInputCaptured) {
            if(Input.GetKeyDown(KeyCode.R))ReplayEpisode();
            if(Input.GetKeyDown(KeyCode.Space)||Input.GetKeyDown(KeyCode.Return))AdvanceEpisode();
            if(Input.GetKeyDown(KeyCode.Tab))originalUi.ToggleLog();
            if(Input.GetKeyDown(KeyCode.Escape))originalUi.Escape();
            if(Input.GetKeyDown(KeyCode.LeftShift))originalUi.CycleAuto();
            if(showingChoices&&Input.GetKeyDown(KeyCode.Alpha1))originalUi.Select(0);
            if(showingChoices&&Input.GetKeyDown(KeyCode.Alpha2))originalUi.Select(1);
            if(showingChoices&&Input.GetKeyDown(KeyCode.Alpha3))originalUi.Select(2);
        }
        bool paused=originalUi.ModalOpen||hidden;
        if(!paused)motionDelay=Mathf.Max(0,motionDelay-Time.deltaTime);
        PauseEpisodeMovie(paused);
        foreach(var a in episodeActors.Values)a.Paused=paused;
        if(paused!=wasModal){if(paused){voice.Pause();effectsAudio.Pause();}else{voice.UnPause();effectsAudio.UnPause();}wasModal=paused;}
        voice.pitch=originalUi.PlaybackSpeed;
        if(episodeWaiting&&!paused) {
            if(dialogueTiming.Holding)dialogueHoldElapsed+=Time.deltaTime;
            dialogueTiming.Tick(Time.deltaTime,originalUi.AutoMode,voice.isPlaying);body.maxVisibleCharacters=dialogueTiming.VisibleCharacters;
            originalUi.SetRevealing(!dialogueTiming.Holding);
            if(originalUi.AutoMode>0&&dialogueTiming.Holding&&dialogueHoldElapsed>.3f)AdvanceEpisode();
        }
        if(verifyEpisode&&Time.time>verifyNext&&!originalUi.Busy) {
            verifyNext=Time.time+.15f;
            if(previewComplete) {
                if(episodeLines!=9||originalUi.EpisodeLogCount!=10)throw new InvalidOperationException("Incomplete episode validation");
                if(verifyBranch==0){verifyBranch=1;ReplayEpisode();}
                else {verifyEpisode=false;Debug.Log("EPISODE_VERIFY_OK both_choices=true lines_per_branch=9 log_rows=10");if(Application.isBatchMode)Application.Quit(0);else ReplayEpisode();}
            } else if(showingChoices)originalUi.Select(verifyBranch);
            else if(episodeWaiting&&dialogueTiming.Holding&&dialogueHoldElapsed>.4f)AdvanceEpisode();
        }
    }
}

#endif
