using System;
using UnityEngine;

public sealed partial class StoryScene {
    AudioClip menuMusic;
    void PlayMenuMusic() {
        if(!menuMusic)menuMusic=StoryContent.Load<AudioClip>("Original/menu-bgm");
        if(!menuMusic)throw new InvalidOperationException("Missing original story-menu BGM");
        // Keep the same cue running when navigating within the story menus.
        if(music.clip==menuMusic && music.isPlaying)return;
        music.Stop();music.clip=menuMusic;music.loop=true;music.pitch=1;music.Play();
        Debug.Log("STORY_MENU_BGM Bgm_Story_Lobby_01");
    }
    void UpdateAudioHotkeys() {
        if(Input.GetKeyDown(KeyCode.Minus))HandleAudioHotkey(KeyCode.Minus);
        if(Input.GetKeyDown(KeyCode.Equals))HandleAudioHotkey(KeyCode.Equals);
        if(Input.GetKeyDown(KeyCode.M))HandleAudioHotkey(KeyCode.M);
    }
    void HandleAudioHotkey(KeyCode key) {
        switch(key) {
            case KeyCode.Minus:volume=Mathf.Max(0,volume-.01f);break;
            case KeyCode.Equals:volume=Mathf.Min(.2f,volume+.01f);break;
            case KeyCode.M:muted=!muted;break;
            default:return;
        }
        AudioListener.volume=muted?0:volume;
        StoryAudioPreferences.Save(muted,volume);
    }
    void CheckAudioPreferences(){
        if(!Application.isBatchMode)return;
        var args=Environment.GetCommandLineArgs();int i=Array.IndexOf(args,"-audio-settings-check");if(i<0)return;
        string mode=args[i+1];bool expected=mode.EndsWith("muted",StringComparison.Ordinal);
        if(mode.StartsWith("write-",StringComparison.Ordinal)){
            if(muted!=expected)HandleAudioHotkey(KeyCode.M);
            volume=.03f;HandleAudioHotkey(KeyCode.Equals);
        }
        if(muted!=expected||Mathf.Abs(volume-.04f)>.00001f||Mathf.Abs(AudioListener.volume-(expected?0:.04f))>.00001f)throw new InvalidOperationException("Audio preference persistence failed");
        Debug.Log("AUDIO_PREFERENCES_OK "+mode+" volume="+volume);
    }
}
