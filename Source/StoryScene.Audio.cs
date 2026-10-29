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
    }
}
