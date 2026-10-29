using System;
using System.Collections;
using System.IO;
using System.Linq;
using UnityEngine;

public sealed partial class StoryScene {
    IEnumerator CheckOldBackgrounds() {
        if(!Application.isBatchMode)yield break;
        var selected=storyMenu.Catalogue.chapters.SelectMany(c=>c.episodes).First(e=>e.scene=="Chapter1_3");
        storyMenu.OpenEpisode(selected);
        yield return WaitForEpisodeLoad();
        float deadline=Time.realtimeSinceStartup+60;
        while(!(episodeWaiting&&!processingCommands&&!originalUi.Busy)&&Time.realtimeSinceStartup<deadline)yield return null;
        if(!episodeWaiting)throw new Exception("Background check setup failed");
        if(episodeRoutine!=null)StopCoroutine(episodeRoutine);
        StopEpisodeMovies();originalUi.HideAllDialogue();foreach(var a in episodeActors.Values)a.gameObject.SetActive(false);
        var folder=Path.GetFullPath(Path.Combine(Application.dataPath,"../../RenderChecks/Backgrounds"));Directory.CreateDirectory(folder);
        var map=JsonUtility.FromJson<OldBackgroundMap>(StoryContent.Load<TextAsset>("story-background-particles").text);
        foreach(var row in map.backgrounds) {
            ClearStoryBackground();ShowStoryBackground(row.asset);yield return new WaitForSecondsRealtime(1.5f);
            RenderTalkCamera();
            var texture=new Texture2D(animatedBackground.Target.width,animatedBackground.Target.height,TextureFormat.RGBAFloat,false);
            RenderTexture.active=animatedBackground.Target;texture.ReadPixels(new Rect(0,0,texture.width,texture.height),0,0);texture.Apply();RenderTexture.active=null;
            float maximum=texture.GetPixels().Max(c=>Mathf.Max(c.r,Mathf.Max(c.g,c.b)));
            if(maximum<.05f)throw new Exception("Empty animated background "+row.asset);
            Destroy(texture);
            string name=Path.GetFileNameWithoutExtension(row.asset);
            StoryBloomFeature.Enabled=false;RenderTalkCamera();SaveStoryTarget(renderTexture,Path.Combine(folder,name+"-no-bloom.png"));
            StoryBloomFeature.Enabled=true;RenderTalkCamera();SaveStoryTarget(renderTexture,Path.Combine(folder,name+"-bloom.png"));
            int particles=storyBackground.GetComponentsInChildren<ParticleSystem>().Sum(p=>p.particleCount);
            if(!string.IsNullOrEmpty(row.root)&&particles==0)throw new Exception("Background particles did not run "+row.asset);
            Debug.Log("OLD_BACKGROUND_RENDER_OK "+name+" hdrMax="+maximum+" particles="+particles);
        }
        ClearStoryBackground();yield return null;
        if(StoryBloomFeature.AnimatedBackground)throw new Exception("Animated background target was not released");
        Debug.Log("OLD_BACKGROUNDS_CHECK_OK count="+map.backgrounds.Length+" cleanup=true");Application.Quit(0);
    }
}
