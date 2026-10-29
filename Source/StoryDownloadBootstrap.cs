using System;
using System.Collections;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

public sealed class StoryDownloadBootstrap:MonoBehaviour {
    StoryAssetInstaller installer;
    public StartupLoadingScreen Screen {get;private set;}
    public bool Installed {get;private set;}
    public bool Configured {get;private set;}
    public IEnumerator Run(){
        installer=StoryAssetInstaller.Create();if(installer==null)yield break;Configured=true;
        var hash=Task.Run(()=>StoryContent.Hash(installer.BootstrapBundle));while(!hash.IsCompleted)yield return null;
        if(hash.IsFaulted)throw hash.Exception.GetBaseException();if(hash.Result!=installer.BootstrapHash)throw new Exception("Startup UI hash mismatch");
        Screen=gameObject.AddComponent<StartupLoadingScreen>();yield return Screen.Prepare(installer.BootstrapBundle);
        var args=Environment.GetCommandLineArgs();bool captured=false;
        while(true){
            Task install=installer.Install();
            while(!install.IsCompleted){
                Screen.DownloadProgress(Interlocked.Read(ref installer.Done),Interlocked.Read(ref installer.Total),installer.Phase);
                if(!captured&&installer.Requests>0){Screen.Capture("download-076");captured=true;}
                yield return null;
            }
            if(!install.IsFaulted&&!install.IsCanceled)break;
            Debug.LogWarning("ASSET_DOWNLOAD_FAILED "+(install.Exception?.GetBaseException().Message??"Cancelled"));Screen.DownloadError();
            if(Application.isBatchMode){Application.Quit(1);yield break;}
            while(!Input.GetKeyDown(KeyCode.Return)&&!Input.GetKeyDown(KeyCode.KeypadEnter)){
                if(Input.GetKeyDown(KeyCode.Escape)){Application.Quit();yield break;}yield return null;
            }
        }
        installer.SaveVerification();StoryContent.InstalledRoot=installer.ContentRoot;Installed=true;
        Debug.Log("ASSET_INSTALL_OK networkRequests="+installer.Requests+" downloaded="+installer.NeededDownload+" root="+installer.ContentRoot);
        if(Application.isBatchMode&&args.Contains("-download-only-check")){Screen.Close();Application.Quit(0);yield break;}
        Screen.DownloadMode(false);
    }
    public void BeginMovie(){if(Installed)Screen.BeginMovie();}
    public void Ready(){if(installer!=null)StartCoroutine(Cleanup());}
    IEnumerator Cleanup(){var task=Task.Run(()=>installer.Cleanup());while(!task.IsCompleted)yield return null;if(task.IsFaulted)Debug.LogWarning("DOWNLOAD_CACHE_RETAINED "+task.Exception.GetBaseException().Message);else Debug.Log("DOWNLOAD_CACHE_CLEANED");}
    void OnApplicationQuit(){installer?.Dispose();}
}
