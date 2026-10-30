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
        var preview=installer.VerifyStartupMovie();while(!preview.IsCompleted)yield return null;
        if(preview.IsFaulted)throw preview.Exception.GetBaseException();
        if(preview.Result!=null){Screen.BeginMovie(preview.Result);Debug.Log("STARTUP_LOCAL_MOVIE_VERIFIED beforeDownload=true");}
        else Debug.Log("STARTUP_LOCAL_MOVIE_FALLBACK installedOrDownloadedContent=true");
        if(ViewerShell.Instance){
            yield return ViewerShell.Instance.CheckUpdates(Screen);
            installer.Dispose();
            try{installer=StoryAssetInstaller.Create();}
            catch(Exception){if(!ViewerShell.Instance.Updates.Rollback())throw;Debug.LogWarning("CODE_PLAN_ROLLBACK invalid candidate catalog");installer=StoryAssetInstaller.Create();}
        }
        var args=Environment.GetCommandLineArgs();bool captured=false,mediaChecked=false;
        while(true){
            Task install=installer.Install();
            while(!install.IsCompleted){
                Screen.DownloadProgress(Interlocked.Read(ref installer.Done),Interlocked.Read(ref installer.Total),installer.Phase);
                if(installer.ReadyMovie!=null){
                    Screen.BeginMovie(installer.ReadyMovie);
                    if(Application.isBatchMode&&args.Contains("-startup-media-check")&&!mediaChecked){
                        yield return Screen.CheckDownloadMedia();mediaChecked=true;installer.ReleaseMediaProbe();
                    }
                }
                if(!captured&&installer.Requests>0){Screen.Capture("download-076");captured=true;}
                if(Application.isBatchMode&&args.Contains("-startup-download-media-check")&&!mediaChecked&&installer.Requests>0){
                    yield return Screen.CheckDownloadMedia();
                    if(install.IsCompleted)throw new Exception("Download media check needs an active transfer");
                    mediaChecked=true;Debug.Log("STARTUP_DURING_DOWNLOAD_OK requests="+installer.Requests+" bytes="+Interlocked.Read(ref installer.Done));
                }
                yield return null;
            }
            if(!install.IsFaulted&&!install.IsCanceled)break;
            if(ViewerShell.Instance?.Updates!=null&&ViewerShell.Instance.Updates.Rollback()){
                Debug.LogWarning("CODE_ASSET_ROLLBACK previous backend selected");installer.Dispose();installer=StoryAssetInstaller.Create();continue;
            }
            Debug.LogWarning("ASSET_DOWNLOAD_FAILED "+(install.Exception?.GetBaseException().Message??"Cancelled"));Screen.DownloadError();
            if(Application.isBatchMode){Application.Quit(1);yield break;}
            while(!Input.GetKeyDown(KeyCode.Return)&&!Input.GetKeyDown(KeyCode.KeypadEnter)){
                if(Input.GetKeyDown(KeyCode.Escape)){Application.Quit();yield break;}yield return null;
            }
        }
        if(installer.ReadyMovie!=null)Screen.BeginMovie(installer.ReadyMovie);
        if(Application.isBatchMode&&args.Contains("-startup-download-media-check")&&!mediaChecked)throw new Exception("Download media check did not run");
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
