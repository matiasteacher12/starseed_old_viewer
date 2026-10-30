#if UNITY_EDITOR || VIEWER_BACKEND
using System;
using System.Collections;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

public sealed partial class StoryScene {
    IEnumerator CheckExpansionConnections(){
        if(!Application.isBatchMode)yield break;
        var args=Environment.GetCommandLineArgs();int first=Array.IndexOf(args,"-expansion-start"),amount=Array.IndexOf(args,"-expansion-count");
        var rows=storyMenu.AllChapters.Where(c=>!string.IsNullOrEmpty(c.category)&&c.category!="main").SelectMany(c=>c.episodes.Select(e=>new{chapter=c,entry=e})).ToArray();
        if(rows.Length!=671||rows.Any(r=>r.entry.id.StartsWith("Date_")))throw new Exception("Expansion scope mismatch");
        var selected=rows.Skip(first<0?0:int.Parse(args[first+1])).Take(amount<0?int.MaxValue:int.Parse(args[amount+1])).ToArray();
        Time.timeScale=8;
        var camera=new GameObject("Expansion offscreen camera",typeof(Camera)).GetComponent<Camera>();camera.cullingMask=~(1<<17);camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.black;camera.orthographic=true;camera.orthographicSize=360;camera.transform.position=new Vector3(0,0,-10);
        var target=new RenderTexture(1280,720,24);target.Create();camera.targetTexture=target;var canvas=stage.GetComponentInParent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=camera;canvas.planeDistance=1;
        string folder=Path.GetFullPath(Path.Combine(Application.dataPath,"../../RenderChecks/Expansion091"));Directory.CreateDirectory(folder);
        foreach(var row in selected){
            storyMenu.ShowChapter(row.chapter);int readBefore=storyMenu.ReadCount(row.chapter);storyMenu.OpenEpisode(row.entry);yield return WaitForEpisodeLoad();
            if(episode.id!=row.entry.id)throw new Exception("Wrong episode identity "+row.entry.id);
            float deadline=Time.realtimeSinceStartup+60;
            while(!storyMenu.Visible&&!(episodeWaiting&&!processingCommands&&!originalUi.Busy&&!string.IsNullOrEmpty(currentTextId))&&Time.realtimeSinceStartup<deadline){
                if(showingChoices&&!originalUi.Busy)originalUi.Select(0);
                else if(explicitClickWait&&!originalUi.Busy)AdvanceEpisode();
                yield return null;
            }
            if(storyMenu.Visible||Time.realtimeSinceStartup>=deadline)throw new Exception("No opening dialogue "+row.entry.id);
            // Let the authored dialogue entrance animation expose its glyphs.
            // Reveal through the ordinary click path so Update does not restore
            // the typewriter's zero-visible-character state on the next frame.
            if(dialogueTiming.IsTyping)AdvanceEpisode();
            yield return new WaitForSeconds(.6f);
            body.maxVisibleCharacters=int.MaxValue;body.ForceMeshUpdate();if(body.textInfo.characterCount==0)throw new Exception("Empty rendered dialogue "+row.entry.id);
            RenderTalkCamera();Canvas.ForceUpdateCanvases();camera.Render();
            var pixels=ReadTransparencyPixels(target);int magenta=pixels.Count(p=>p.r>220&&p.b>220&&p.g<40);
            if(magenta>pixels.Length/200)throw new Exception("Possible unsupported visible shader "+row.entry.id+" magentaPixels="+magenta);
            SaveStoryTarget(target,Path.Combine(folder,row.entry.id+".png"));
            Debug.Log("EXPANSION_OPEN_OK "+row.entry.id+" text="+currentTextId+" actors="+episodeActors.Count);
            int skips=0;
            while(!storyMenu.Visible&&skips++<40){
                yield return ConfirmProgressSkip();yield return new WaitForSecondsRealtime(.25f);
                if(!storyMenu.Visible&&endingEpisode)yield return AwaitProgressMenu();
            }
            if(!storyMenu.Visible||!storyMenu.IsRead(row.entry)||storyMenu.ReadCount(row.chapter)!=readBefore+1)throw new Exception("Expansion skip/progress failed "+row.entry.id);
            AssertProgress(row.chapter,readBefore+1);
            if(originalUi.Root.activeSelf||(storyVideo&&storyVideo.isPlaying)||movieTarget)throw new Exception("Expansion cleanup failed "+row.entry.id);
            storyMenu.Back();yield return null;
            Debug.Log("EXPANSION_CONNECT_OK "+row.entry.id+" category="+row.chapter.category);
        }
        Debug.Log("EXPANSION_CONNECTIONS_OK entries="+selected.Length+" isolatedProgress=true");Application.Quit(0);
    }
}
#endif
