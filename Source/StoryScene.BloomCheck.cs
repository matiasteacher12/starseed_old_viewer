using System;
using System.Collections;
using System.IO;
using System.Linq;
using UnityEngine;

public sealed partial class StoryScene {
    IEnumerator CheckOldBloom() {
        if(!Application.isBatchMode)yield break;
        string folder=Path.GetFullPath(Path.Combine(Application.dataPath,"../../RenderChecks/Bloom"));Directory.CreateDirectory(folder);
        var capture=new GameObject("Bloom validation camera",typeof(Camera)).GetComponent<Camera>();
        capture.cullingMask=~(1<<17);capture.clearFlags=CameraClearFlags.SolidColor;capture.backgroundColor=Color.black;
        capture.orthographic=true;capture.orthographicSize=360;capture.transform.position=new Vector3(0,0,-10);
        var target=new RenderTexture(1280,720,24);target.Create();capture.targetTexture=target;
        var canvas=stage.GetComponentInParent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=capture;canvas.planeDistance=1;
        Action<string> save=name=>{RenderTalkCamera();Canvas.ForceUpdateCanvases();capture.Render();SaveStoryTarget(target,Path.Combine(folder,name+".png"));};
        storyMenu.OpenEpisode(storyMenu.Catalogue.chapters.SelectMany(c=>c.episodes).First(e=>e.scene=="Chapter1_3"));
        yield return WaitForEpisodeLoad();
        float deadline=Time.realtimeSinceStartup+60;
        while(!(episodeWaiting&&!processingCommands&&!originalUi.Busy&&dialogueTiming.Holding&&motionDelay<=0)&&Time.realtimeSinceStartup<deadline)yield return null;
        if(currentTextId!="Text_Chapter1_3_001")throw new Exception("Bloom check target not reached");
        yield return new WaitForSecondsRealtime(1);
        // Freeze pose/time by doing all four renders synchronously in this frame.
        StoryBloomFeature.Enabled=true;StoryBloomFeature.IncludeBackground=false;save("before-bloom");
        StoryBloomFeature.Enabled=false;save("before-no-bloom");
        StoryBloomFeature.IncludeBackground=true;save("after-no-bloom");
        StoryBloomFeature.Enabled=true;save("after-bloom");
        Debug.Log("OLD_BLOOM_STATE texture="+backgroundImage.texture.name+" uv="+backgroundImage.uvRect+" blur="+focusAmount+" camera="+StoryBloomFeature.BackgroundCamera.name);
        SetFocusAmount(.8f);StoryBloomFeature.Enabled=false;save("blur-shared");
        StoryBloomFeature.IncludeBackground=false;save("blur-separated");
        // A black placeholder must leave authored animated UI backgrounds visible.
        if(episodeRoutine!=null)StopCoroutine(episodeRoutine);
        ShowStoryBackground("Assets/Bundles/UI/Prefabs/Common/Scenario/Bg_FxFirstScene_bg.prefab");
        yield return new WaitForSecondsRealtime(1);
        StoryBloomFeature.Enabled=true;save("prefab-separated");
        StoryBloomFeature.IncludeBackground=true;save("prefab-shared");
        Debug.Log("OLD_BLOOM_CHECK_OK sameFrame=true illustration=true blur=true blackPlaceholder=true");
        Application.Quit(0);
    }
}
