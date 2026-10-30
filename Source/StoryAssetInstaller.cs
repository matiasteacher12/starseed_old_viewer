using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

// Installs only the immutable catalog shipped with this app. No login/token,
// moving latest branch, original game server, or arbitrary archive paths.
public sealed class StoryAssetInstaller : IDisposable {
    [Serializable] public class Plan {public int schema;public string version,catalogSha256,bootstrapSha256;public Part[] parts;public StoryContent.FileEntry startupMovie;}
    [Serializable] public class Part {public string name,url,sha256;public long bytes;public string[] objects;}
    readonly Plan plan;readonly StoryContent.Manifest catalog;readonly string bootstrap,root,store,downloads;
    readonly string mediaBootstrap;
    readonly string catalogText;readonly Dictionary<string,string> metadataText=new Dictionary<string,string>();
    readonly StoryContentVerification verification;readonly Dictionary<string,StoryContent.FileEntry> objects;
    readonly CancellationTokenSource cancel=new CancellationTokenSource();
    public string ContentRoot=>root;
    public string BootstrapBundle=>Path.Combine(mediaBootstrap,"startup.bundle");
    public string BootstrapHash=>plan.bootstrapSha256;
    public volatile string Phase="데이터 확인 중…";
    public long Done,Total;public int Requests;
    public bool NeededDownload {get;private set;}
    public volatile string ReadyMovie;
    readonly bool mediaProbe;
    readonly ManualResetEventSlim mediaProbeGate=new ManualResetEventSlim(false);
    public void ReleaseMediaProbe()=>mediaProbeGate.Set();
    public static StoryAssetInstaller Create(){
        string app=Path.GetFullPath(Path.Combine(Application.dataPath,".."));
        string config=Path.Combine(ViewerShell.BackendDirectory??Path.Combine(app,"Bootstrap"),"install.json"),destination=Path.Combine(app,"Content");
        var args=Environment.GetCommandLineArgs();int p=Array.IndexOf(args,"-download-plan"),d=Array.IndexOf(args,"-download-directory");
        if(Application.isBatchMode&&p>=0){config=Path.GetFullPath(args[p+1]);if(d<0)throw new Exception("Isolated download directory required");destination=Path.GetFullPath(args[d+1]);}
        return File.Exists(config)?new StoryAssetInstaller(config,destination):null;
    }
    StoryAssetInstaller(string config,string destination){
        mediaBootstrap=ViewerShell.Instance?Path.Combine(Path.GetFullPath(Path.Combine(Application.dataPath,"..")),"Bootstrap"):Path.GetDirectoryName(config);
        bootstrap=Path.GetDirectoryName(config);root=Path.Combine(destination,"Assets");store=Path.Combine(destination,"asset-store");downloads=Path.Combine(destination,".downloads");
        plan=JsonUtility.FromJson<Plan>(File.ReadAllText(config));
        string manifestPath=Path.Combine(bootstrap,"content.json");
        byte[] manifestBytes=File.ReadAllBytes(manifestPath);string digest;
        using(var hash=System.Security.Cryptography.SHA256.Create())digest=BitConverter.ToString(hash.ComputeHash(manifestBytes)).Replace("-","").ToLowerInvariant();
        if(plan==null||plan.schema!=1||digest!=plan.catalogSha256)throw new InvalidDataException("Invalid installation catalog");
        catalogText=System.Text.Encoding.UTF8.GetString(manifestBytes);
        catalog=JsonUtility.FromJson<StoryContent.Manifest>(catalogText);
        foreach(string name in new[]{"index.json","source-metadata.json"}){string p=Path.Combine(bootstrap,name);if(File.Exists(p))metadataText[name]=File.ReadAllText(p);}
        if(catalog==null||catalog.contentApi!=StoryContent.ContentApi||catalog.assetStore!="../asset-store"||!Regex.IsMatch(catalog.storeId??"","^[A-Za-z0-9_-]+$"))throw new InvalidDataException("Invalid installation store");
        objects=catalog.files.GroupBy(f=>f.sha256).ToDictionary(g=>g.Key,g=>g.First());
        foreach(var f in catalog.files)if(!Regex.IsMatch(f.sha256??"","^[0-9a-f]{64}$")||f.bytes<0||objects[f.sha256].bytes!=f.bytes)throw new InvalidDataException("Invalid object");
        if(!objects.ContainsKey(plan.bootstrapSha256)||objects[plan.bootstrapSha256].path!="Startup/startup.bundle")throw new InvalidDataException("Bootstrap not in catalog");
        var covered=new HashSet<string>{plan.bootstrapSha256};var names=new HashSet<string>();
        foreach(var part in plan.parts){
            if(!Regex.IsMatch(part.name??"","^[a-zA-Z0-9_.-]+\\.zip$")||!names.Add(part.name)||!Regex.IsMatch(part.sha256??"","^[0-9a-f]{64}$")||part.bytes<=0)throw new InvalidDataException("Invalid part");
            var uri=new Uri(part.url);bool test=Application.isBatchMode&&uri.Scheme=="http"&&uri.IsLoopback;
            if(!test&&(uri.Scheme!="https"||uri.Host!="github.com"||!uri.AbsolutePath.StartsWith("/matiasteacher12/starseed_old_data/releases/download/")))throw new InvalidDataException("Untrusted asset endpoint");
            foreach(string h in part.objects)if(!objects.ContainsKey(h)||!covered.Add(h))throw new InvalidDataException("Duplicate/unknown part object");
        }
        if(covered.Count!=objects.Count)throw new InvalidDataException("Incomplete download plan");
        if(plan.startupMovie!=null&&(plan.startupMovie.path!="intro-preview.mp4"||plan.startupMovie.bytes<=0||plan.startupMovie.bytes>=100L*1024*1024||!Regex.IsMatch(plan.startupMovie.sha256??"","^[0-9a-f]{64}$")))throw new InvalidDataException("Invalid startup movie");
        verification=new StoryContentVerification(root,plan.catalogSha256);
        mediaProbe=Application.isBatchMode&&Environment.GetCommandLineArgs().Contains("-startup-media-check");
    }
    string ObjectPath(string h)=>Path.Combine(store,"referenced",h.Substring(0,2),h);
    public Task<string> VerifyStartupMovie()=>Task.Run(()=>{
        var entry=plan.startupMovie;if(entry==null)return null;
        string path=Path.Combine(mediaBootstrap,entry.path);
        try{
            var before=new FileInfo(path);if(!before.Exists||before.Length!=entry.bytes)return null;
            long write=before.LastWriteTimeUtc.Ticks;string digest=StoryContent.Hash(path);var after=new FileInfo(path);
            return digest==entry.sha256&&after.Length==entry.bytes&&after.LastWriteTimeUtc.Ticks==write?path:null;
        }catch(IOException){return null;}catch(UnauthorizedAccessException){return null;}
    });
    void CheckCancel(){cancel.Token.ThrowIfCancellationRequested();}
    bool Verified(string h){
        string p=ObjectPath(h);var f=objects[h];
        if(!File.Exists(p)||new FileInfo(p).Length!=f.bytes)return false;
        try{if(!verification.IsVerified(p,f))verification.Accept(f,verification.VerifyBytes(p,f));return true;}
        catch(InvalidDataException){return false;}
    }
    public Task Install()=>Task.Run(()=>{
        Phase="데이터 확인 중…";
        Directory.CreateDirectory(store);Directory.CreateDirectory(downloads);Directory.CreateDirectory(root);
        // One installer per destination; the ordinary asset byte lock is also
        // held for every publication, shared with the renderer/import tools.
        using(var owner=new FileStream(Path.Combine(downloads,"install.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None)){
            string lockPath=Path.Combine(store,"store.lock");using(var l=new FileStream(lockPath,FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.ReadWrite)){if(l.Length==0)l.WriteByte(0);}
            string identity=Path.Combine(store,"store.json");string expected="{\"schemaVersion\":1,\"storeId\":\""+catalog.storeId+"\"}";
            using(StoryAssetStore.AcquireDirectory(store)){if(!File.Exists(identity))AtomicText(identity,expected);}
            // Constructor verifies the existing identity instead of replacing it.
            var checkedStore=new StoryAssetStore(root,"../asset-store",catalog.storeId);
            if(StoryContent.Hash(BootstrapBundle)!=plan.bootstrapSha256)throw new InvalidDataException("Bootstrap hash mismatch");
            if(!Verified(plan.bootstrapSha256)){string tmp=Path.Combine(downloads,plan.bootstrapSha256+".object");File.Copy(BootstrapBundle,tmp,true);PublishObject(tmp,plan.bootstrapSha256);}
            // Verify the installed intro first, on this same worker/cache. The
            // screen can play that immutable object while other hashes run.
            var intro=catalog.files.FirstOrDefault(f=>f.path=="Startup/intro_background.mp4");
            bool introReady=intro!=null&&Verified(intro.sha256);
            if(introReady)PublishMovie(checkedStore,intro);
            if(mediaProbe&&introReady&&!mediaProbeGate.Wait(TimeSpan.FromSeconds(30),cancel.Token))throw new TimeoutException("Startup media probe not released");
            var missing=new HashSet<string>();long checkedCount=0;Interlocked.Exchange(ref Total,objects.Count);Interlocked.Exchange(ref Done,0);
            foreach(var h in objects.Keys){CheckCancel();if(!(introReady&&h==intro.sha256)&&!Verified(h))missing.Add(h);Interlocked.Exchange(ref Done,++checkedCount);}
            var parts=plan.parts.Where(p=>p.objects.Any(missing.Contains)).OrderByDescending(p=>intro!=null&&p.objects.Contains(intro.sha256)).ToArray();NeededDownload=parts.Length>0;
            Interlocked.Exchange(ref Total,parts.Sum(p=>p.bytes));Interlocked.Exchange(ref Done,0);long completed=0;
            foreach(var part in parts){
                CheckCancel();Phase="애셋 다운로드 중…";string archive=Path.Combine(downloads,part.name);
                if(!File.Exists(archive)||new FileInfo(archive).Length!=part.bytes||StoryContent.Hash(archive)!=part.sha256){
                    if(File.Exists(archive))File.Move(archive,archive+".invalid-"+Guid.NewGuid().ToString("N"));
                    Download(part,archive+".part",completed);
                    Phase="받은 파일 확인 중…";string partial=archive+".part";
                    if(new FileInfo(partial).Length!=part.bytes||StoryContent.Hash(partial)!=part.sha256){File.Move(partial,partial+".invalid-"+Guid.NewGuid().ToString("N"));throw new InvalidDataException("Download SHA-256 mismatch");}
                    File.Move(partial,archive);
                }
                completed+=part.bytes;Interlocked.Exchange(ref Done,completed);Phase="애셋 설치 중…";
                using(var zip=ZipFile.OpenRead(archive)){
                    var expectedObjects=new HashSet<string>(part.objects);
                    if(zip.Entries.Count!=expectedObjects.Count)throw new InvalidDataException("Unexpected archive entries");
                    foreach(var entry in zip.Entries){
                        CheckCancel();string h=entry.FullName;
                        if(!expectedObjects.Remove(h)||entry.Length!=objects[h].bytes)throw new InvalidDataException("Unsafe/mismatched archive entry");
                        if(!missing.Contains(h))continue;
                        string tmp=Path.Combine(downloads,h+".object");
                        using(var input=entry.Open())using(var output=new FileStream(tmp,FileMode.Create,FileAccess.Write,FileShare.None)){var buffer=new byte[1024*1024];int n;while((n=input.Read(buffer,0,buffer.Length))>0){CheckCancel();output.Write(buffer,0,n);}output.Flush(true);}
                        PublishObject(tmp,h);missing.Remove(h);
                        if(intro!=null&&h==intro.sha256)PublishMovie(checkedStore,intro);
                    }
                }
            }
            if(missing.Count!=0)throw new InvalidDataException("Uninstalled objects");
            CheckCancel();using(StoryAssetStore.AcquireDirectory(store)){
                // Publish the catalog last. An interrupted install never makes
                // an incomplete new revision look ready to the player.
                // Keep the exact verified snapshot even if an app update edits
                // Bootstrap while the long download is still in progress.
                AtomicText(Path.Combine(root,"content.json"),catalogText);
                foreach(var metadata in metadataText)AtomicText(Path.Combine(store,metadata.Key),metadata.Value);
            }
            Phase="데이터 준비 완료";
        }
    },cancel.Token);
    void PublishMovie(StoryAssetStore checkedStore,StoryContent.FileEntry intro){
        using(checkedStore.Acquire()){
            string path=checkedStore.Resolve(intro);
            try {if(!verification.IsVerified(path,intro))verification.Accept(intro,verification.VerifyBytes(path,intro));}
            catch(InvalidDataException){
                string quarantine=Path.Combine(store,"quarantine");Directory.CreateDirectory(quarantine);
                File.Move(path,Path.Combine(quarantine,intro.sha256+"-view-"+Guid.NewGuid().ToString("N")+".mp4"));
                path=checkedStore.Resolve(intro);verification.Accept(intro,verification.VerifyBytes(path,intro));
            }
            ReadyMovie=path;
        }
    }
    void PublishObject(string temporary,string h){
        if(new FileInfo(temporary).Length!=objects[h].bytes||StoryContent.Hash(temporary)!=h)throw new InvalidDataException("Object SHA-256 mismatch");
        using(StoryAssetStore.AcquireDirectory(store)){
            string target=ObjectPath(h);Directory.CreateDirectory(Path.GetDirectoryName(target));
            if(File.Exists(target)){
                if(StoryContent.Hash(target)==h){File.Delete(temporary);verification.Accept(objects[h],verification.VerifyBytes(target,objects[h]));return;}
                string quarantine=Path.Combine(store,"quarantine");Directory.CreateDirectory(quarantine);
                File.Move(target,Path.Combine(quarantine,h+"-"+Guid.NewGuid().ToString("N")));
            }
            // A movie view may still link the old damaged inode. Preserve that
            // alias in quarantine; never clear immutable file attributes or
            // keep serving the old view after publishing a repaired object.
            string view=Path.Combine(store,"media-views",h+".mp4");
            if(File.Exists(view)){string quarantine=Path.Combine(store,"quarantine");Directory.CreateDirectory(quarantine);File.Move(view,Path.Combine(quarantine,h+"-view-"+Guid.NewGuid().ToString("N")+".mp4"));}
            File.Move(temporary,target);File.SetAttributes(target,File.GetAttributes(target)|FileAttributes.ReadOnly);
            verification.Accept(objects[h],verification.VerifyBytes(target,objects[h]));
        }
    }
    void Download(Part part,string partial,long completed){
        for(int attempt=0;attempt<3;attempt++){
            CheckCancel();long offset=File.Exists(partial)?new FileInfo(partial).Length:0;
            if(offset==part.bytes){Interlocked.Exchange(ref Done,completed+offset);return;}
            if(offset>part.bytes){File.Move(partial,partial+".invalid-"+Guid.NewGuid().ToString("N"));offset=0;}
            var request=(HttpWebRequest)WebRequest.Create(part.url);request.UserAgent="StarseedOldViewer/0.7.6";request.Timeout=30000;request.ReadWriteTimeout=30000;if(offset>0)request.AddRange(offset);
            Interlocked.Increment(ref Requests);
            try{
                using(cancel.Token.Register(request.Abort))using(var response=(HttpWebResponse)request.GetResponse()){
                    if(offset>0&&response.StatusCode==HttpStatusCode.OK)offset=0; // server ignored Range: safely restart
                    else if(offset>0&&(response.StatusCode!=HttpStatusCode.PartialContent||!(response.Headers["Content-Range"]??"").StartsWith("bytes "+offset+"-")))throw new InvalidDataException("Invalid resume response");
                    else if(offset==0&&response.StatusCode!=HttpStatusCode.OK)throw new InvalidDataException("Invalid download response");
                    using(var input=response.GetResponseStream())using(var output=new FileStream(partial,offset==0?FileMode.Create:FileMode.Append,FileAccess.Write,FileShare.Read)){
                        var buffer=new byte[256*1024];int n;long received=offset;
                        while((n=input.Read(buffer,0,buffer.Length))>0){CheckCancel();received+=n;if(received>part.bytes)throw new InvalidDataException("Download exceeded declared size");output.Write(buffer,0,n);Interlocked.Exchange(ref Done,completed+received);}
                        output.Flush(true);if(received!=part.bytes)throw new IOException("Interrupted download");
                    }
                }
                return;
            }catch(Exception e)when(e is WebException||e is IOException){if(attempt==2)throw;CheckCancel();}
        }
    }
    static void AtomicText(string target,string text){string tmp=target+"."+Guid.NewGuid().ToString("N")+".tmp";File.WriteAllText(tmp,text);if(File.Exists(target))File.Replace(tmp,target,null);else File.Move(tmp,target);}
    public void SaveVerification()=>verification.Save(); // JsonUtility stays on the Unity main thread.
    public void Cleanup(){
        // Only verified transport archives; no source assets or user saves.
        foreach(var part in plan.parts){string p=Path.Combine(downloads,part.name);if(File.Exists(p)&&new FileInfo(p).Length==part.bytes&&StoryContent.Hash(p)==part.sha256)File.Delete(p);}
    }
    public void Dispose(){cancel.Cancel();}
}
