#if UNITY_EDITOR || VIEWER_BACKEND
using System;
using System.Collections;
using System.IO;
using System.Linq;
using UnityEngine;

public sealed partial class StoryScene {
    Color32[] ReadTransparencyPixels(RenderTexture target) {
        var prev=RenderTexture.active;RenderTexture.active=target;
        var image=new Texture2D(target.width,target.height,TextureFormat.RGBA32,false);
        image.ReadPixels(new Rect(0,0,target.width,target.height),0,0);image.Apply();
        var pixels=image.GetPixels32();Destroy(image);RenderTexture.active=prev;return pixels;
    }
    IEnumerator CheckEffectTransparency(Camera camera,RenderTexture target,string directory) {
        storyMenu.OpenEpisode(storyMenu.Catalogue.chapters.SelectMany(c=>c.episodes).First(e=>e.scene=="Chapter1_1"));
        yield return WaitForEpisodeLoad();
        if(episodeRoutine!=null)StopCoroutine(episodeRoutine);
        StopEpisodeMovies();ClearGlobalStoryEffects();originalUi.ResetView();
        foreach(var a in episodeActors.Values)a.gameObject.SetActive(false);
        yield return PlayEpisodeMovie("Movies/jp_spine_story_main_01_00_04.mp4",true,false);
        yield return new WaitForSecondsRealtime(.5f);PauseEpisodeMovie(true);
        foreach(var key in new[]{"Effect/Prefab/05.UI/EFF_talk_scan_player","Effect/Prefab/05.UI/EFF_talk_scan_player_new"}) {
        string captureName=key.EndsWith("_new")?"scan-new":"scan";
        var effect=Instantiate(OriginalAsset<GameObject>(episode.effects.First(e=>e.key==key).asset));
        effect.transform.position=actorCamera.transform.position;
        foreach(var tr in effect.GetComponentsInChildren<Transform>(true))tr.gameObject.layer=17;
        foreach(var ps in effect.GetComponentsInChildren<ParticleSystem>(true))ps.Play();
        yield return new WaitForSecondsRealtime(2.4f);
        var renderers=effect.GetComponentsInChildren<Renderer>(true);
        var materials=renderers.SelectMany(r=>r.materials).ToArray();
        var originalShaders=materials.Select(m=>m.shader).ToArray();
        RenderTalkCamera();Canvas.ForceUpdateCanvases();camera.Render();
        SaveStoryTarget(target,Path.Combine(directory,captureName+"-transparency-before.png"));
        ConfigureEffectComposite(effect);
        RenderTalkCamera();Canvas.ForceUpdateCanvases();camera.Render();
        SaveStoryTarget(target,Path.Combine(directory,captureName+"-transparency-after.png"));
        var correctedShaders=materials.Select(m=>m.shader).ToArray();
        // Isolate the full-screen additive quad. It must add the exact same RGB
        // while preserving zero occlusion alpha on a transparent camera target.
        foreach(var r in renderers)r.enabled=r.sharedMaterials.Any(m=>m.name.StartsWith("MT_screen_quad"));
        if(!renderers.Any(r=>r.enabled))throw new Exception("Original additive screen quad missing");
        for(int i=0;i<materials.Length;i++)materials[i].shader=originalShaders[i];
        RenderTalkCamera();var before=ReadTransparencyPixels(actorCamera.targetTexture);
        for(int i=0;i<materials.Length;i++)materials[i].shader=correctedShaders[i];
        RenderTalkCamera();var after=ReadTransparencyPixels(actorCamera.targetTexture);
        long beforeAlpha=0,afterAlpha=0,rgbDifference=0;
        for(int i=0;i<before.Length;i++) {
            beforeAlpha+=before[i].a;afterAlpha+=after[i].a;
            rgbDifference+=Math.Abs(before[i].r-after[i].r)+Math.Abs(before[i].g-after[i].g)+Math.Abs(before[i].b-after[i].b);
        }
        Debug.Log("STORY_EFFECT_ALPHA key="+key+" before="+beforeAlpha+" after="+afterAlpha+" rgbDifference="+rgbDifference);
        if(beforeAlpha<1000 || afterAlpha!=0 || rgbDifference!=0)
            throw new Exception("Additive camera alpha coverage/RGB preservation check failed");
        Destroy(effect);yield return null;
        }
        StopEpisodeMovies();
    }
}

#endif
