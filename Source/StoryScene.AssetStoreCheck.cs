#if UNITY_EDITOR || VIEWER_BACKEND
using System;
using System.Collections;
using System.Linq;
using UnityEngine;
using UnityEngine.Video;

public sealed partial class StoryScene {
    IEnumerator CheckAssetStoreMedia() {
        if(!Application.isBatchMode||!StoryContent.UsesAssetStore)throw new Exception("Store-only validation required");
        int count=0;
        foreach(var path in StoryContent.DeclaredFiles.Where(p=>p.EndsWith(".mp4",StringComparison.OrdinalIgnoreCase))) {
            var go=new GameObject("Canonical movie probe");var player=go.AddComponent<VideoPlayer>();
            player.playOnAwake=false;player.audioOutputMode=VideoAudioOutputMode.None;player.renderMode=VideoRenderMode.RenderTexture;
            var target=new RenderTexture(64,64,0);target.Create();player.targetTexture=target;player.url=StoryContent.FilePath(path);
            string error=null;player.errorReceived+=(p,message)=>error=message;
            player.Prepare();float deadline=Time.realtimeSinceStartup+30;
            while(!player.isPrepared&&error==null&&Time.realtimeSinceStartup<deadline)yield return null;
            if(error!=null||!player.isPrepared)throw new Exception("Canonical video prepare failed "+path+" "+error);
            player.Play();deadline=Time.realtimeSinceStartup+30;
            while(player.frame<1&&error==null&&Time.realtimeSinceStartup<deadline)yield return null;
            if(error!=null||player.frame<1)throw new Exception("Canonical video decode failed "+path+" "+error);
            Debug.Log("ASSET_STORE_VIDEO_OK "+path+" frame="+player.frame+" tracks="+player.audioTrackCount);
            player.Stop();Destroy(go);target.Release();Destroy(target);count++;yield return null;
        }
        if(count==0)throw new Exception("No videos tested");
        Debug.Log("ASSET_STORE_MEDIA_OK videos="+count+" strict=true");Application.Quit(0);
    }
}

#endif
