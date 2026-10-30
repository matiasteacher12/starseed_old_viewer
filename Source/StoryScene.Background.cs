#if UNITY_EDITOR || VIEWER_BACKEND
using System;
using System.Collections;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

public sealed partial class StoryScene {
    [Serializable] class OldBackgroundMap {public OldBackgroundEntry[] backgrounds;}
    [Serializable] class OldBackgroundEntry {public string asset,root;}
    GameObject storyBackground;
    StoryAnimatedBackground animatedBackground;
    Coroutine storyShake;
    void RenderTalkCamera(){Canvas.ForceUpdateCanvases();if(animatedBackground)animatedBackground.Camera.Render();actorCamera.Render();}
    void ClearStoryBackground(){if(animatedBackground){animatedBackground.gameObject.SetActive(false);Destroy(animatedBackground.gameObject);}animatedBackground=null;if(storyBackground)Destroy(storyBackground);storyBackground=null;}
    void ShowStoryBackground(string asset) {
        backgroundImage.texture=Texture2D.blackTexture;
        animatedBackground=new GameObject("Old animated talk background").AddComponent<StoryAnimatedBackground>();
        animatedBackground.Initialize(stage,backgroundImage,actorCamera);
        var source=OriginalAsset<GameObject>(asset);
        storyBackground=Instantiate(source,animatedBackground.Stage,false);
        var rect=storyBackground.GetComponent<RectTransform>();
        if(rect){rect.anchorMin=Vector2.zero;rect.anchorMax=Vector2.one;rect.offsetMin=rect.offsetMax=Vector2.zero;rect.localScale=source.transform.localScale;}
        foreach(var image in storyBackground.GetComponentsInChildren<UnityEngine.UI.Graphic>(true)) {
            image.raycastTarget=false;
            if(image.material && image.material.shader.name=="M_FX/Panner/S_uv_distortion_panner_ab") {
                var material=new Material(image.material);material.shader=OriginalAsset<Shader>("story-background-ui.shader");image.material=material;
            }
        }
        foreach(var animation in storyBackground.GetComponentsInChildren<Animation>(true))if(animation.clip)animation.Play();
        var manifest=StoryContent.Load<TextAsset>("story-background-particles").text;
        var row=BackendJson.FromJson<OldBackgroundMap>(manifest).backgrounds.Single(r=>r.asset==asset);
        if(!string.IsNullOrEmpty(row.root)) {
            var shaders=BackendJson.FromJson<EffectShaderMap>(manifest);
            foreach(var renderer in storyBackground.GetComponentsInChildren<ParticleSystemRenderer>(true))
                foreach(var material in renderer.materials) {
                    var shader=shaders.shaders.Single(s=>s.source==material.shader.name);
                    material.shader=OriginalAsset<Shader>(shader.asset);
                    if(!material.shader.isSupported)throw new InvalidOperationException("Unsupported old background shader "+material.shader.name);
                }
            var fx=storyBackground.transform.Find(row.root);
            if(!fx)throw new InvalidOperationException("Missing audited old background root "+row.root);
            fx.SetAsLastSibling();
            var particles=fx.gameObject.AddComponent<Coffee.UIExtensions.UIParticle>();
            particles.scale3D=Vector3.one;particles.autoScalingMode=Coffee.UIExtensions.UIParticle.AutoScalingMode.Transform;
            particles.RefreshParticles();particles.Play();
        }
        foreach(var tr in storyBackground.GetComponentsInChildren<Transform>(true))tr.gameObject.layer=StoryAnimatedBackground.Layer;
        Debug.Log("STORY_BACKGROUND_PREFAB "+asset+" sharedTalkBloom=true hdr=true");
    }
    void StartStoryShake(string name) {
        if(storyShake!=null)StopCoroutine(storyShake);
        storyShake=StartCoroutine(PlayStoryShake(name));
    }
    IEnumerator PlayStoryShake(string name) {
        // Source legacy UI animation is preserved. It drives the common
        // CharacterRender plane, keeping foreground and background together.
        AnimationClip clip=null;
        foreach(var animation in originalUi.Root.GetComponentsInChildren<Animation>(true))
            foreach(AnimationState state in animation)if(state.name==name)clip=state.clip;
        if(!clip) {
            Debug.LogWarning("STORY_SHAKE_UNAVAILABLE "+name);yield break;
        }
        var animationHost=new GameObject("Story shake adapter",typeof(RectTransform),typeof(Animation));
        animationHost.transform.SetParent(stage,false);
        var plane=new GameObject("CharacterRender",typeof(RectTransform)).GetComponent<RectTransform>();plane.SetParent(animationHost.transform,false);
        var player=animationHost.GetComponent<Animation>();player.AddClip(clip,name);player.Play(name);
        var actorStart=actorImage.rectTransform.anchoredPosition;var bgStart=backgroundImage.rectTransform.anchoredPosition;
        while(player.isPlaying){actorImage.rectTransform.anchoredPosition=actorStart+plane.anchoredPosition;backgroundImage.rectTransform.anchoredPosition=bgStart+plane.anchoredPosition;yield return null;}
        actorImage.rectTransform.anchoredPosition=actorStart;backgroundImage.rectTransform.anchoredPosition=bgStart;Destroy(animationHost);storyShake=null;
    }
}

#endif
