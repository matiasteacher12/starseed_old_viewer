using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

// Fixed shell ABI 1. Downloaded code is trusted publisher code, never game code.
// No loaded DLL is overwritten. A candidate becomes active only after STORY_READY.
public sealed class ViewerCodeUpdate : IDisposable {
    public const string Feed="https://raw.githubusercontent.com/matiasteacher12/starseed_old_viewer/main/UpdateFeed/stable.json";
    public const int Api=1;
    static readonly string[] Names={"StoryBackend.dll","install.json","content.json","index.json","source-metadata.json"};
    [Serializable] public class Manifest {public int schema,shellApi;public string version,shellSha256;public FileRecord[] files;}
    [Serializable] public class FileRecord {public string name,sha256,url;public long bytes;}
    [Serializable] class State {public string active,previous,pending,rejected;}
    public string DirectoryPath {get;private set;}
    public string Version {get;private set;}
    public volatile string Phase="앱 업데이트 확인 중…";
    public long Done,Total;public string Warning {get;private set;}
    readonly string app,cache,feed,shellHash;readonly bool test;
    readonly CancellationTokenSource cancel=new CancellationTokenSource();
    FileStream owner;State state;string selected;Manifest manifest;
    public ViewerCodeUpdate(string appDirectory,string cacheDirectory=null,string feedUrl=null,bool isolated=false){
        app=Path.GetFullPath(appDirectory);cache=Path.GetFullPath(cacheDirectory??Path.Combine(app,"CodeUpdates"));
        test=isolated;feed=feedUrl??Feed;shellHash=StoryContent.Hash(Path.Combine(app,"StarseedOldStory_Data/Managed/Assembly-CSharp.dll"));
    }
    static bool HashName(string s)=>Regex.IsMatch(s??"","^[0-9a-f]{64}$");
    static string Digest(byte[] bytes){using(var h=System.Security.Cryptography.SHA256.Create())return BitConverter.ToString(h.ComputeHash(bytes)).Replace("-","").ToLowerInvariant();}
    static void Atomic(string path,string value){string tmp=path+".tmp";File.WriteAllText(tmp,value,new UTF8Encoding(false));using(var f=new FileStream(tmp,FileMode.Open,FileAccess.ReadWrite))f.Flush(true);if(File.Exists(path))File.Replace(tmp,path,null);else File.Move(tmp,path);}
    void Save()=>Atomic(Path.Combine(cache,"state.json"),JsonUtility.ToJson(state,true));
    void CheckUri(string url,bool isFeed){
        var u=new Uri(url);if(test&&u.Scheme=="http"&&u.IsLoopback)return;
        if(u.Scheme!="https"||!string.IsNullOrEmpty(u.UserInfo)||!string.IsNullOrEmpty(u.Fragment)||!string.IsNullOrEmpty(u.Query))throw new InvalidDataException("Untrusted update URL");
        if(isFeed?url!=Feed:u.Host!="github.com"||!u.AbsolutePath.StartsWith("/matiasteacher12/starseed_old_viewer/releases/download/code-v",StringComparison.Ordinal))throw new InvalidDataException("Untrusted update repository");
    }
    Manifest Parse(byte[] bytes){
        if(bytes.Length>65536)throw new InvalidDataException("Oversized update manifest");
        var m=JsonUtility.FromJson<Manifest>(Encoding.UTF8.GetString(bytes));
        if(m==null||m.schema!=1||m.shellApi!=Api||m.shellSha256!=shellHash||!System.Version.TryParse(m.version,out var v)||v.Build<0||v.Revision>=0||m.files==null||m.files.Length!=Names.Length)throw new InvalidDataException("Incompatible update shell");
        if(!m.files.Select(f=>f.name).OrderBy(n=>n).SequenceEqual(Names.OrderBy(n=>n)))throw new InvalidDataException("Invalid update files");
        foreach(var f in m.files){if(!HashName(f.sha256)||f.bytes<=0||f.bytes>32*1024*1024)throw new InvalidDataException("Invalid update file");CheckUri(f.url,false);}
        if(m.files.Sum(f=>f.bytes)>64*1024*1024)throw new InvalidDataException("Oversized code update");return m;
    }
    void Verify(string folder,Manifest m){foreach(var f in m.files){string p=Path.Combine(folder,f.name);if(!File.Exists(p)||new FileInfo(p).Length!=f.bytes||StoryContent.Hash(p)!=f.sha256)throw new InvalidDataException("Code update hash mismatch: "+f.name);}}
    bool Select(string id){
        if(id!="bundled"&&!HashName(id))return false;
        try{string dir=id=="bundled"?Path.Combine(app,"Backend"):HashName(id)?Path.Combine(cache,"releases",id):throw new InvalidDataException("Invalid update state");
            byte[] bytes=File.ReadAllBytes(Path.Combine(dir,"manifest.json"));if(id!="bundled"&&Digest(bytes)!=id)throw new InvalidDataException("Changed update manifest");
            var m=Parse(bytes);Verify(dir,m);selected=id;DirectoryPath=dir;Version=m.version;manifest=m;return true;
        }catch(Exception e) when(e is IOException||e is InvalidDataException||e is ArgumentException||e is UnauthorizedAccessException){Warning=e.Message;return false;}
    }
    public Task Prepare(bool online=true)=>Task.Run(()=>{
        Directory.CreateDirectory(cache);owner=new FileStream(Path.Combine(cache,"update.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
        try{state=JsonUtility.FromJson<State>(File.ReadAllText(Path.Combine(cache,"state.json")))??new State();}catch(Exception e) when(e is IOException||e is ArgumentException){state=new State();}
        if(!string.IsNullOrEmpty(state.pending)){state.rejected=state.pending;state.pending=null;Save();}
        if(!Select(state.active)&&!Select(state.previous)&&!Select("bundled"))throw new InvalidDataException("No verified backend available");
        // A new shell bundle may supersede a cached older backend.
        var bundled=Parse(File.ReadAllBytes(Path.Combine(app,"Backend/manifest.json")));
        if(System.Version.Parse(bundled.version)>System.Version.Parse(Version))Select("bundled");
        if(!online)return;
        try {
            CheckUri(feed,true);byte[] bytes=ReadNetwork(feed,65536);var next=Parse(bytes);string id=Digest(bytes);
            if(id==state.rejected||System.Version.Parse(next.version)<=System.Version.Parse(Version))return;
            Phase="앱 업데이트 다운로드 중…";Interlocked.Exchange(ref Total,next.files.Sum(f=>f.bytes));Interlocked.Exchange(ref Done,0);
            string folder=Path.Combine(cache,"releases",id);Directory.CreateDirectory(folder);
            foreach(var file in next.files){cancel.Token.ThrowIfCancellationRequested();string path=Path.Combine(folder,file.name);
                if(File.Exists(path)&&new FileInfo(path).Length==file.bytes&&StoryContent.Hash(path)==file.sha256){Interlocked.Add(ref Done,file.bytes);continue;}
                var data=ReadNetwork(file.url,file.bytes);if(data.LongLength!=file.bytes||Digest(data)!=file.sha256)throw new InvalidDataException("Downloaded code hash mismatch");
                string tmp=path+".part";File.WriteAllBytes(tmp,data);using(var f=new FileStream(tmp,FileMode.Open,FileAccess.ReadWrite))f.Flush(true);
                if(File.Exists(path))File.Replace(tmp,path,null);else File.Move(tmp,path);Interlocked.Add(ref Done,file.bytes);
            }
            Verify(folder,next);Atomic(Path.Combine(folder,"manifest.json"),Encoding.UTF8.GetString(bytes));
            if(!Select(id))throw new InvalidDataException("Candidate verification failed");
        }catch(Exception e) when(e is WebException||e is IOException||e is InvalidDataException||e is ArgumentException||e is UnauthorizedAccessException){Warning=e.Message;Phase="설치된 앱으로 시작합니다…";}
    });
    byte[] ReadNetwork(string url,long limit){
        cancel.Token.ThrowIfCancellationRequested();var req=(HttpWebRequest)WebRequest.Create(url);req.Timeout=8000;req.ReadWriteTimeout=15000;req.UserAgent="StarseedOldViewer/1";
        using(cancel.Token.Register(()=>req.Abort()))using(var response=(HttpWebResponse)req.GetResponse())using(var stream=response.GetResponseStream())using(var output=new MemoryStream()){
            if(response.ContentLength>limit)throw new InvalidDataException("Oversized update response");var buffer=new byte[65536];int n;
            while((n=stream.Read(buffer,0,buffer.Length))>0){cancel.Token.ThrowIfCancellationRequested();if(output.Length+n>limit)throw new InvalidDataException("Oversized update response");output.Write(buffer,0,n);}return output.ToArray();
        }
    }
    public void BeginTrial(){Verify(DirectoryPath,manifest);if(selected!=state.active){state.pending=selected;Save();}}
    public void Healthy(){if(state==null)return;if(state.pending==selected){state.previous=state.active;state.active=selected;state.pending=null;Save();}Debug.Log("CODE_UPDATE_HEALTHY version="+Version);}
    public void CleanExit(){if(state!=null&&state.pending==selected){state.pending=null;Save();}}
    public bool Rollback(){if(state==null||selected==state.active||selected=="bundled")return false;state.rejected=selected;state.pending=null;Save();return Select(state.active)||Select(state.previous)||Select("bundled");}
    public byte[] VerifiedAssembly(){Verify(DirectoryPath,manifest);var bytes=File.ReadAllBytes(Path.Combine(DirectoryPath,"StoryBackend.dll"));if(Digest(bytes)!=manifest.files.Single(f=>f.name=="StoryBackend.dll").sha256)throw new InvalidDataException("Backend changed while loading");return bytes;}
    public void Dispose(){cancel.Cancel();owner?.Dispose();owner=null;}
}
