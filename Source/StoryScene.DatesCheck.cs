#if UNITY_EDITOR || VIEWER_BACKEND
using System;
using System.Collections;
using System.IO;
using System.Linq;
using UnityEngine;

public sealed partial class StoryScene {
    IEnumerator CheckDates(){
        if(!Application.isBatchMode)yield break;
        var args=Environment.GetCommandLineArgs();var native=viewerHome.MessageUi;
        var camera=new GameObject("Dates offscreen camera",typeof(Camera)).GetComponent<Camera>();camera.cullingMask=~(1<<17);camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.black;camera.orthographic=true;camera.orthographicSize=360;camera.transform.position=new Vector3(0,0,-10);
        var target=new RenderTexture(1280,720,24);target.Create();camera.targetTexture=target;var canvas=stage.GetComponentInParent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=camera;canvas.planeDistance=1;
        var folder=Path.GetFullPath(Path.Combine(Application.dataPath,"../../RenderChecks/Dates092"));Directory.CreateDirectory(folder);
        Action<string> capture=name=>{RenderTalkCamera();Canvas.ForceUpdateCanvases();camera.Render();SaveStoryTarget(target,Path.Combine(folder,name+".png"));};
        if(args.Contains("-messages-check")){yield return native.CheckMessages(capture);Application.Quit(0);yield break;}
        int pi=Array.IndexOf(args,"-dates-suite");var suite=BackendJson.FromJson<MainSuite>(File.ReadAllText(args[pi+1]));Time.timeScale=8;bool connect=args.Contains("-dates-connect");
        foreach(var entry in suite.entries){
            Debug.Log("DATES_ROUTE_BEGIN "+entry.id);native.PrepareDateCheck(entry.episode);yield return WaitForEpisodeLoad();if(episode.id!=entry.episode)throw new Exception("Date identity mismatch");
            int choices=0;float next=0,deadline=Time.realtimeSinceStartup+180;bool captured=false;
            while(!native.Visible&&Time.realtimeSinceStartup<deadline){
                if(Time.realtimeSinceStartup>next){next=Time.realtimeSinceStartup+.03f;
                    if(!captured&&(episodeWaiting||explicitClickWait)&&!processingCommands&&!originalUi.Busy&&!string.IsNullOrEmpty(currentTextId)&&!string.IsNullOrEmpty(body.text)){
                        if(dialogueTiming.IsTyping)AdvanceEpisode();yield return new WaitForSeconds(.6f);
                        body.maxVisibleCharacters=int.MaxValue;body.ForceMeshUpdate();if(body.textInfo.characterCount==0)throw new Exception("Empty date dialogue");capture(entry.episode);
                        var pixels=ReadTransparencyPixels(target);if(pixels.Count(p=>p.r>220&&p.b>220&&p.g<40)>pixels.Length/200)throw new Exception("Date shader unsupported");captured=true;
                    }
                    if(connect&&captured){yield return ConfirmProgressSkip();yield return new WaitForSecondsRealtime(.25f);continue;}
                    if(showingChoices&&!originalUi.Busy){if(choices>=entry.plan.choices.Length)throw new Exception("Unexpected date choice");originalUi.Select(entry.plan.choices[choices++]);}
                    else if(explicitClickWait&&!originalUi.Busy)AdvanceEpisode();
                    else if(episodeWaiting&&!processingCommands&&!originalUi.Busy&&motionDelay<=0){
                        AdvanceEpisode();
                    }
                }yield return null;
            }
            if(!native.Visible||(!connect&&(episodeLines!=entry.plan.textIds.Length||choices!=entry.plan.choices.Length)))throw new Exception("Date incomplete "+entry.episode+" lines="+episodeLines+" choices="+choices);
            if(!storyMenu.IsRead(new OriginalStoryMenu.Episode{id=entry.episode})||originalUi.Root.activeSelf||episodeLoading)throw new Exception("Date return/progress failed");
            if(!captured&&episodeLines>0)throw new Exception("Date rendering was not observed");
            Debug.Log((connect?"DATES_CONNECT_OK ":"DATES_ROUTE_OK ")+entry.id+" returnedToMessage=true progress=true");yield return Resources.UnloadUnusedAssets();
        }
        Debug.Log("DATES_SUITE_OK routes="+suite.entries.Length);Application.Quit(0);
    }
}
#endif
