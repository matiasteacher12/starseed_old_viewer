#if UNITY_EDITOR || VIEWER_BACKEND
using System;
using System.Collections;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

public sealed partial class StoryScene {
    IEnumerator CheckRecoveredVoice() {
        const string resource="Original/Episode/b9d02070568255d9f7eb3a5c2e2e8164d9b494e434ef6ff4f79c87eaed5907c1";
        var clip=StoryContent.Load<AudioClip>(resource);
        if(!clip||Mathf.Abs(clip.length-2.9062f)>.03f||voice.clip!=clip||!voice.isPlaying)
            throw new Exception("Recovered voice did not start on its original dialogue");
        int sample=voice.timeSamples;
        yield return new WaitForSecondsRealtime(.15f);
        if(!voice.isPlaying||voice.timeSamples<=sample)throw new Exception("Recovered voice playback did not advance");
        Debug.Log("RECOVERED_VOICE_PLAY_OK text="+currentTextId+" clip="+clip.name+" seconds="+clip.length);
        originalUi.ToggleLog();yield return new WaitForSeconds(1.2f);
        if(!originalUi.ModalOpen)throw new Exception("Recovered voice log did not open");
        var entry=stage.GetComponentsInChildren<StoryLogVoiceButton>(true).Single(b=>b.Clip==clip);
        entry.GetComponent<Button>().onClick.Invoke();yield return new WaitForSecondsRealtime(.15f);
        var replay=originalUi.LogVoiceSource;
        if(!replay||replay.clip!=clip||!replay.isPlaying||replay.timeSamples<=0)throw new Exception("Recovered voice log replay failed");
        originalUi.ToggleLog();yield return new WaitForSeconds(.5f);
        if(originalUi.ModalOpen||replay.isPlaying||replay.clip)throw new Exception("Recovered voice log close cleanup failed");
        Debug.Log("RECOVERED_VOICE_LOG_OK replay=true closeStopped=true");
    }
}

#endif
