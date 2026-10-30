#if UNITY_EDITOR || VIEWER_BACKEND
using UnityEngine;
using UnityEngine.UI;

public sealed partial class OriginalStoryUi {
    AudioSource logVoice;
    StoryLogVoiceButton playingLogButton;
    public AudioSource LogVoiceSource => logVoice;

    void AddLogVoiceButton(Transform row,AudioClip clip) {
        var portrait=row.Find("Char/Img");
        portrait.Find("Icon").gameObject.SetActive(false);
        var go=new GameObject("Replay voice",typeof(RectTransform),typeof(Image),typeof(Button),typeof(StoryLogVoiceButton));
        go.transform.SetParent(portrait,false);
        var rect=(RectTransform)go.transform;
        rect.anchorMin=rect.anchorMax=new Vector2(.5f,0);
        rect.pivot=new Vector2(.5f,1);rect.anchoredPosition=new Vector2(0,-8);rect.sizeDelta=new Vector2(40,32);
        var background=go.GetComponent<Image>();background.color=new Color(.24f,.32f,.38f,.12f);
        var button=go.GetComponent<Button>();button.targetGraphic=background;
        button.navigation=new Navigation{mode=Navigation.Mode.None};
        var iconGo=new GameObject("Play icon",typeof(RectTransform),typeof(StoryLogVoiceIcon));
        iconGo.transform.SetParent(go.transform,false);
        var iconRect=(RectTransform)iconGo.transform;iconRect.sizeDelta=new Vector2(14,14);
        var icon=iconGo.GetComponent<StoryLogVoiceIcon>();icon.color=new Color(.26f,.33f,.39f);icon.raycastTarget=false;
        var entry=go.GetComponent<StoryLogVoiceButton>();entry.Clip=clip;entry.Icon=icon;
        button.onClick.AddListener(()=>ToggleLogVoice(entry));
    }
    void ToggleLogVoice(StoryLogVoiceButton entry) {
        if(!log.activeInHierarchy || modalRoutine!=null)return;
        bool stop=playingLogButton==entry && logVoice && logVoice.isPlaying;
        StopLogVoice();if(stop)return;
        if(!logVoice){logVoice=gameObject.AddComponent<AudioSource>();logVoice.playOnAwake=false;logVoice.loop=false;logVoice.volume=1;logVoice.spatialBlend=0;}
        logVoice.clip=entry.Clip;logVoice.Play();playingLogButton=entry;entry.Icon.Playing=true;
        Debug.Log("STORY_LOG_VOICE_PLAY "+entry.Clip.name);
    }
    void StopLogVoice() {
        if(logVoice){logVoice.Stop();logVoice.clip=null;}
        if(playingLogButton)playingLogButton.Icon.Playing=false;
        playingLogButton=null;
    }
    void Update() {
        if(playingLogButton && (!log.activeInHierarchy || !logVoice.isPlaying))StopLogVoice();
    }
    void OnDisable(){StopLogVoice();}
}

#endif
