using System;
using System.Collections;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

public sealed partial class OriginalStoryUi {
    [Serializable] class FadeKey { public float time,value,inSlope,outSlope; }
    [Serializable] class FadeData { public FadeKey[] outKeys,inKeys; }
    CanvasGroup sceneFade;
    Image sceneFadeImage;
    FadeData sceneFadeData;
    public float SceneFadeOpacity => sceneFade ? sceneFade.alpha : 0;
    public bool SceneFadingOut { get; private set; }

    // ScreenFade.Out/In animate Screen/Bg CanvasGroup.m_Alpha. Reuse the
    // extracted keys (including tangents), above all story UI, not actor-only.
    public IEnumerator PlaySceneFade(bool fadeOut,string hex) {
        if(!sceneFade) {
            sceneFadeData=JsonUtility.FromJson<FadeData>(StoryContent.Load<TextAsset>("story-scene-fade").text);
            var node=new GameObject("Original Normal screen fade",typeof(RectTransform),typeof(CanvasGroup),typeof(Image));
            node.transform.SetParent(Root.transform,false);
            var rect=(RectTransform)node.transform;
            rect.anchorMin=Vector2.zero;rect.anchorMax=Vector2.one;rect.offsetMin=rect.offsetMax=Vector2.zero;
            sceneFade=node.GetComponent<CanvasGroup>();sceneFadeImage=node.GetComponent<Image>();
        }
        Busy=true;SceneFadingOut=fadeOut;
        sceneFade.gameObject.SetActive(true);sceneFade.transform.SetAsLastSibling();
        Color color=Color.black;
        if(!string.IsNullOrEmpty(hex) && !ColorUtility.TryParseHtmlString("#"+hex.TrimStart('#'),out color))
            throw new InvalidOperationException("Invalid original scene fade color "+hex);
        sceneFadeImage.color=color;
        var keys=fadeOut?sceneFadeData.outKeys:sceneFadeData.inKeys;
        var curve=new AnimationCurve(keys.Select(k=>new Keyframe(k.time,k.value,k.inSlope,k.outSlope)).ToArray());
        float duration=keys.Last().time;
        Debug.Log("STORY_SCENE_FADE_BEGIN direction="+(fadeOut?"out":"in")+" duration="+duration);
        for(float elapsed=0;elapsed<duration;elapsed+=Time.deltaTime) {
            sceneFade.alpha=curve.Evaluate(elapsed);yield return null;
        }
        sceneFade.alpha=curve.Evaluate(duration);SceneFadingOut=false;
        if(!fadeOut){sceneFade.gameObject.SetActive(false);Busy=false;}
        Debug.Log("STORY_SCENE_FADE_END direction="+(fadeOut?"out":"in"));
    }
    void ResetSceneFade() {
        if(sceneFade){sceneFade.alpha=0;sceneFade.gameObject.SetActive(false);}
        SceneFadingOut=false;
    }
}
