#if UNITY_EDITOR || VIEWER_BACKEND
using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

public sealed partial class StoryScene {
    GameObject storyUiAnimation;
    void ClearStoryUiAnimation(){if(storyUiAnimation)Destroy(storyUiAnimation);storyUiAnimation=null;}
    // Root_TalkScene.PlayUIAnimation pauses its engine until the popup closes.
    // Root_Scenario_AnimUI.Setup plays the named legacy clip, waits, then closes.
    IEnumerator PlayStoryUiAnimation(string prefab,string clip) {
        if(storyUiAnimation)Destroy(storyUiAnimation);
        storyUiAnimation=Instantiate(OriginalAsset<GameObject>("Assets/Bundles/UI/Prefabs/Common/Scenario/Root_Scenario_AnimUI.prefab"),stage,false);
        storyUiAnimation.transform.SetAsLastSibling();
        var content=Instantiate(OriginalAsset<GameObject>("Assets/Bundles/"+prefab),storyUiAnimation.transform.Find("Center"),false);
        foreach(var graphic in storyUiAnimation.GetComponentsInChildren<Graphic>(true))graphic.raycastTarget=false;
        foreach(var particles in content.GetComponentsInChildren<Coffee.UIExtensions.UIParticle>(true))particles.RefreshParticles();
        var animation=content.GetComponent<Animation>();
        if(!animation||animation[clip]==null)throw new InvalidOperationException("Missing original UI animation "+clip);
        animation.Play(clip);
        Debug.Log("STORY_UI_ANIMATION_BEGIN "+clip+" duration="+animation[clip].length);
        while(animation.IsPlaying(clip))yield return null;
        Destroy(storyUiAnimation);storyUiAnimation=null;
        Debug.Log("STORY_UI_ANIMATION_END "+clip);
    }
}

#endif
