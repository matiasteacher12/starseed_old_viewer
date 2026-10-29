using System;
using System.Collections;
using System.IO;
using System.Linq;
using UnityEngine;

public sealed partial class StoryScene {
    IEnumerator CheckFilmEffect(Camera camera,RenderTexture target,string directory) {
        storyMenu.OpenEpisode(storyMenu.Catalogue.chapters.SelectMany(c=>c.episodes).First(e=>e.scene=="Chapter1_1"));
        yield return WaitForEpisodeLoad();
        float deadline=Time.realtimeSinceStartup+60;bool sought=false;
        while(!(episodeWaiting && currentTextId=="Text_Chapter1_1_001") && Time.realtimeSinceStartup<deadline) {
            if(!sought && storyVideo && storyVideo.isPlaying && storyVideo.time>1.5 && movieBlocking) {
                storyVideo.time=storyVideo.length-1;sought=true;
            }
            yield return null;
        }
        if(!episodeWaiting || globalStoryEffects.Count!=1)throw new Exception("Film opening not reached");
        for(int i=0;i<6;i++) {
            yield return new WaitForSecondsRealtime(.21f);
            RenderTalkCamera();Canvas.ForceUpdateCanvases();camera.Render();
            SaveStoryTarget(target,Path.Combine(directory,"film-composite-"+i+".png"));
            SaveStoryTarget(renderTexture,Path.Combine(directory,"film-raw-"+i+".png"));
        }
        Debug.Log("FILM_CAMERA "+actorCamera.transform.position+" rotation="+actorCamera.transform.rotation+" fov="+actorCamera.fieldOfView+" lens="+actorCamera.lensShift+" image="+actorImage.color+" active="+actorImage.gameObject.activeInHierarchy);
        ClearGlobalStoryEffects();yield return null;
        RenderTalkCamera();Canvas.ForceUpdateCanvases();camera.Render();
        SaveStoryTarget(target,Path.Combine(directory,"film-cleared.png"));
        if(globalStoryEffects.Count!=0)throw new Exception("Film effect was not released");
    }
}
