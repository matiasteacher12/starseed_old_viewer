using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;

// Only this fixed assembly boots. The backend has no player startup attribute.
public sealed class ViewerShell:MonoBehaviour {
    public static ViewerShell Instance {get;private set;}
    public StoryDownloadBootstrap Downloader {get;private set;}
    public ViewerCodeUpdate Updates {get;private set;}
    public static string BackendVersion=>Instance?.Updates?.Version??Application.version;
    public static string BackendDirectory=>Instance?.Updates?.DirectoryPath;
    bool backendStarted,failed;bool muted;float volume;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot(){
#if !UNITY_EDITOR
        new GameObject("Viewer shell").AddComponent<ViewerShell>();
#endif
    }
    IEnumerator Start(){
        Instance=this;Application.targetFrameRate=60;StoryAudioPreferences.Load(out muted,out volume);AudioListener.volume=muted?0:volume;gameObject.AddComponent<AudioListener>();
        Application.logMessageReceived+=(message,stack,type)=>{if(type==LogType.Exception){failed=true;if(Application.isBatchMode)Application.Quit(1);}};
        Downloader=gameObject.AddComponent<StoryDownloadBootstrap>();Exception failure=null;
        yield return Guard(Downloader.Run(),e=>failure=e);
        if(failure!=null){Fail(failure);yield break;}if(!Downloader.Installed)yield break;
        if(Application.isBatchMode&&Environment.GetCommandLineArgs().Contains("-download-only-check"))yield break;
        try{
            Updates.BeginTrial();var assembly=Assembly.Load(Updates.VerifiedAssembly());
            if(assembly.GetName().Version!=new Version(Updates.Version+".0"))throw new InvalidDataException("Backend version differs from manifest");
            var type=assembly.GetType("StoryScene",true);if(!typeof(MonoBehaviour).IsAssignableFrom(type))throw new InvalidDataException("Invalid backend entry");
            backendStarted=true;gameObject.AddComponent(type);Debug.Log("CODE_BACKEND_LOADED version="+Updates.Version+" assembly="+assembly.GetName().Name);
        }catch(Exception e){Fail(e);}
    }
    public IEnumerator CheckUpdates(StartupLoadingScreen screen){
        string app=Path.GetFullPath(Path.Combine(Application.dataPath,".."));var args=Environment.GetCommandLineArgs();
        string cache=null,feed=null;int c=Array.IndexOf(args,"-code-cache"),f=Array.IndexOf(args,"-code-feed");
        if(Application.isBatchMode&&c>=0)cache=Path.GetFullPath(args[c+1]);
        if(Application.isBatchMode&&f>=0){if(cache==null)throw new InvalidDataException("Isolated code cache required");feed=args[f+1];}
        Updates=new ViewerCodeUpdate(app,cache,feed,Application.isBatchMode&&cache!=null);
        bool online=!Application.isBatchMode||f>=0||args.Contains("-code-public-check");
        var task=Updates.Prepare(online);while(!task.IsCompleted){screen.DownloadProgress(System.Threading.Interlocked.Read(ref Updates.Done),System.Threading.Interlocked.Read(ref Updates.Total),Updates.Phase);yield return null;}
        if(task.IsFaulted)throw task.Exception.GetBaseException();
        if(Updates.Warning!=null)Debug.LogWarning("CODE_UPDATE_FALLBACK "+Updates.Warning);
        Debug.Log("CODE_UPDATE_SELECTED version="+Updates.Version);
        if(Application.isBatchMode&&args.Contains("-code-trial-interrupt")){Updates.BeginTrial();Application.Quit(0);yield return new WaitForSecondsRealtime(10);}
    }
    public static void Healthy(){Instance?.Updates?.Healthy();}
    void Update(){if(backendStarted)return;bool changed=false;if(Input.GetKeyDown(KeyCode.M)){muted=!muted;changed=true;}if(Input.GetKeyDown(KeyCode.Minus)){volume=Mathf.Max(0,volume-.01f);changed=true;}if(Input.GetKeyDown(KeyCode.Equals)){volume=Mathf.Min(.2f,volume+.01f);changed=true;}if(changed){AudioListener.volume=muted?0:volume;StoryAudioPreferences.Save(muted,volume);}}
    void Fail(Exception e){failed=true;Debug.LogError("CODE_START_FAILED "+e);if(Downloader?.Screen)Downloader.Screen.Close();if(Application.isBatchMode)Application.Quit(1);else gameObject.AddComponent<StoryContentError>().Message="앱을 시작하지 못했습니다. 종료 후 다시 실행하면 이전 정상 버전으로 복구합니다.\n"+e.Message;}
    static IEnumerator Guard(IEnumerator routine,Action<Exception> failed){while(true){object next;try{if(!routine.MoveNext())yield break;next=routine.Current;}catch(Exception e){failed(e);yield break;}if(next is IEnumerator inner){Exception failure=null;yield return Guard(inner,e=>failure=e);if(failure!=null){failed(failure);yield break;}}else yield return next;}}
    void OnApplicationQuit(){if(!failed&&!Environment.GetCommandLineArgs().Contains("-code-trial-interrupt"))Updates?.CleanExit();Updates?.Dispose();}
}
