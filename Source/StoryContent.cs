using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Collections;
using System.Threading.Tasks;
using UnityEngine;

// Playback reads installed, verified content only. A future downloader installs
// the same layout; it does not become a dependency of the story renderer.
public static class StoryContent {
    public const int ContentApi=2;
    [Serializable] public class Manifest {
        public int schemaVersion,contentApi;public string packId,version,minAppVersion,resourceBundle,assetStore,storeId;
        public Resource[] resources;public FileEntry[] files;public BundleInfo[] resourceBundles;
    }
    [Serializable] public class Resource {public string key,asset,bundle;}
    [Serializable] public class FileEntry {public string path,sha256;public long bytes;}
    [Serializable] public class BundleInfo {public string name;public string[] dependencies;}
    static string root;static AssetBundle resources;
    static Dictionary<string,Resource> resourceNames;
    static readonly Dictionary<string,AssetBundle> contentBundles=new Dictionary<string,AssetBundle>();
    static string defaultResourceBundle;
    static HashSet<string> files;
    static Dictionary<string,FileEntry> fileEntries;
    static Dictionary<string,BundleInfo> bundleInfo;
    static StoryContentVerification verification;
    static StoryAssetStore store;
    static readonly HashSet<string> storeReads=new HashSet<string>();
    public static bool UsesAssetStore=>store!=null;
    public static string CatalogHash {get;private set;}
    public static string[] DeclaredFiles=>fileEntries.Keys.ToArray();
    public static long HashedBytes => verification?.HashedBytes??0;
    public static int Hashes => verification?.Hashes??0;
    public static int CacheHits => verification?.Hits??0;
    public static bool External => resources!=null;
    public static string InstalledRoot {get;set;}
    public static void Initialize(bool loadCommon=true) {
        var required=UnityEngine.Resources.Load<TextAsset>("external-content-required");
        if(!required)return; // Existing integrated development build.
        root=InstalledRoot??Path.GetFullPath(Path.Combine(Application.dataPath,"../../Assets"));
        var args=Environment.GetCommandLineArgs();int contentArg=Array.IndexOf(args,"-content-root");
        if(Application.isBatchMode&&contentArg>=0)root=Path.GetFullPath(args[contentArg+1]);
        string manifestPath=Path.Combine(root,"content.json");
        if(!File.Exists(manifestPath))throw new InvalidDataException("Asset pack missing. Extract all asset ZIP files beside the app. Expected: "+manifestPath);
        byte[] manifestBytes=File.ReadAllBytes(manifestPath);
        var manifest=JsonUtility.FromJson<Manifest>(System.Text.Encoding.UTF8.GetString(manifestBytes));
        if(manifest==null||manifest.schemaVersion!=1||manifest.contentApi!=ContentApi)throw new InvalidDataException("Incompatible asset pack. Update the app and asset pack together.");
        if(!Version.TryParse(manifest.minAppVersion,out var minimum)||!Version.TryParse(Application.version,out var current)||current<minimum)throw new InvalidDataException("This asset pack requires app "+manifest.minAppVersion);
        if(manifest.files==null||manifest.resources==null)throw new InvalidDataException("Incomplete asset manifest");
        if(!string.IsNullOrEmpty(manifest.assetStore)) {
            store=new StoryAssetStore(root,manifest.assetStore,manifest.storeId);
            using(store.Acquire()) {
                byte[] locked=File.ReadAllBytes(manifestPath);
                if(!manifestBytes.SequenceEqual(locked))throw new IOException("Asset catalog changed during startup. Restart the viewer.");
            }
        }
        using(var hash=SHA256.Create())CatalogHash=BitConverter.ToString(hash.ComputeHash(manifestBytes)).Replace("-","").ToLowerInvariant();
        if(store!=null)Debug.Log("ASSET_STORE_CATALOG "+CatalogHash+" store="+manifest.storeId+" strict=true");
        files=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var item in manifest.files) {
            string path=SafePath(root,item.path);
            if(!files.Add(item.path))throw new InvalidDataException("Duplicate asset path: "+item.path);
            if(item.bytes<0||item.sha256==null||item.sha256.Length!=64)throw new InvalidDataException("Invalid file record: "+item.path);
        }
        resourceNames=manifest.resources.ToDictionary(r=>r.key,r=>r,StringComparer.OrdinalIgnoreCase);
        fileEntries=manifest.files.ToDictionary(r=>r.path,StringComparer.OrdinalIgnoreCase);
        bundleInfo=(manifest.resourceBundles??new BundleInfo[0]).ToDictionary(b=>b.name,StringComparer.OrdinalIgnoreCase);
        verification=new StoryContentVerification(root,CatalogHash);
        defaultResourceBundle=manifest.resourceBundle;
        if(loadCommon){foreach(var name in ResourceClosure(new[]{defaultResourceBundle}))LoadBundle(name);resources=contentBundles[defaultResourceBundle];
            if(!resources)throw new InvalidDataException("Cannot load the installed resource pack");}
        verification.Save();
        Debug.Log("STORY_CONTENT_INDEXED pack="+manifest.packId+" version="+manifest.version+" files="+files.Count+" verifiedOnUse=true");
    }
    public static IEnumerator InitializeCommon(Action<float> progress) {
        if(root==null)yield break;
        var names=ResourceClosure(new[]{defaultResourceBundle});int completed=0;
        foreach(var name in names){
            yield return VerifyAsync(name);
            if(!contentBundles.ContainsKey(name)){
                var request=AssetBundle.LoadFromFileAsync(FilePath(name));yield return request;
                if(!request.assetBundle)throw new InvalidDataException("Cannot load resource bundle "+name);
                contentBundles.Add(name,request.assetBundle);
            }
            progress?.Invoke(++completed/(float)names.Length);yield return null;
        }
        resources=contentBundles[defaultResourceBundle];SaveVerification();
    }
    public static T Load<T>(string key) where T:UnityEngine.Object {
        if(External&&resourceNames.TryGetValue(key,out var entry)) {
            string name=string.IsNullOrEmpty(entry.bundle)?defaultResourceBundle:entry.bundle;
            if(!contentBundles.TryGetValue(name,out var bundle)) {
                foreach(var dependency in ResourceClosure(new[]{name}))LoadBundle(dependency);
                bundle=contentBundles[name];
            }
            return bundle.LoadAsset<T>(entry.asset);
        }
        return UnityEngine.Resources.Load<T>(key);
    }
    public static string FilePath(string relative) {
        if(root==null)return Path.Combine(Application.streamingAssetsPath,relative);
        if(files==null||!files.Contains(relative))throw new InvalidDataException("File not declared by asset pack: "+relative);
        string full=PhysicalPath(relative);
        var entry=fileEntries[relative];
        if(!verification.IsVerified(full,entry)){verification.Accept(entry,verification.VerifyBytes(full,entry));verification.Save();}
        if(store!=null&&Application.isBatchMode&&storeReads.Add(relative))Debug.Log("ASSET_STORE_READ "+entry.sha256+" "+relative);
        return full;
    }
    public static bool HasFile(string relative) => root==null?File.Exists(Path.Combine(Application.streamingAssetsPath,relative)):files.Contains(relative);
    static string PhysicalPath(string relative) {
        if(store==null)return SafePath(root,relative);
        using(store.Acquire())return store.Resolve(fileEntries[relative]);
    }
    public static string PackFilePath(string directory,Manifest manifest,FileEntry entry) {
        if(string.IsNullOrEmpty(manifest.assetStore))return SafePath(directory,entry.path);
        var storage=new StoryAssetStore(directory,manifest.assetStore,manifest.storeId);
        using(storage.Acquire())return storage.Resolve(entry);
    }
    static void LoadBundle(string name) {
        if(contentBundles.ContainsKey(name))return;
        var bundle=AssetBundle.LoadFromFile(FilePath(name));
        if(!bundle)throw new InvalidDataException("Cannot load resource bundle "+name);
        contentBundles.Add(name,bundle);
    }
    static string[] ResourceClosure(IEnumerable<string> names) {
        var result=new List<string>();var visiting=new HashSet<string>();
        Action<string> visit=null;visit=name=>{if(result.Contains(name))return;if(!visiting.Add(name))throw new InvalidDataException("Resource dependency cycle "+name);
            if(bundleInfo.TryGetValue(name,out var b))foreach(var d in b.dependencies)visit(d);visiting.Remove(name);result.Add(name);};
        foreach(var name in names)visit(name);return result.ToArray();
    }
    public static IEnumerator VerifyAsync(string relative) {
        if(root==null)yield break;
        if(!fileEntries.TryGetValue(relative,out var entry))throw new InvalidDataException("Undeclared file "+relative);
        string full=PhysicalPath(relative);
        if(verification.IsVerified(full,entry))yield break;
        var task=Task.Run(()=>verification.VerifyBytes(full,entry));
        while(!task.IsCompleted)yield return null;
        if(task.IsFaulted)throw task.Exception.InnerException;
        verification.Accept(entry,task.Result);
    }
    public static IEnumerator PrepareResources(IEnumerable<string> keys) {
        if(root==null)yield break;
        var names=keys.Distinct().Select(k=>resourceNames.TryGetValue(k,out var r)?(string.IsNullOrEmpty(r.bundle)?defaultResourceBundle:r.bundle):null).Where(n=>n!=null);
        foreach(var name in ResourceClosure(names)) {
            yield return VerifyAsync(name);
            if(contentBundles.ContainsKey(name))continue;
            var request=AssetBundle.LoadFromFileAsync(FilePath(name));yield return request;
            if(!request.assetBundle)throw new InvalidDataException("Cannot load resource bundle "+name);
            contentBundles.Add(name,request.assetBundle);
        }
        SaveVerification();
    }
    public static void SaveVerification(){verification?.Save();}
    public static void ReleaseTransientResources() {
        if(root==null)return;
        var keep=new HashSet<string>(ResourceClosure(new[]{defaultResourceBundle}));
        foreach(var name in contentBundles.Keys.Where(n=>!keep.Contains(n)).ToArray()){contentBundles[name].Unload(true);contentBundles.Remove(name);}
    }
    public static string SafePath(string basePath,string relative) {
        if(string.IsNullOrEmpty(relative)||Path.IsPathRooted(relative)||relative.Contains(":")||relative.Contains("\\")||relative.Split('/').Any(p=>p==".."||p=="."||p==""))throw new InvalidDataException("Invalid asset path");
        var path=Path.GetFullPath(Path.Combine(basePath,relative));
        if(!path.StartsWith(Path.GetFullPath(basePath).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Asset path escapes pack directory");
        return path;
    }
    public static string Hash(string path) {using(var hash=SHA256.Create())using(var stream=File.OpenRead(path))return BitConverter.ToString(hash.ComputeHash(stream)).Replace("-","").ToLowerInvariant();}
}

public sealed class StoryContentError : MonoBehaviour {
    public string Message;
    void OnGUI() {
        var rect=new Rect(30,30,Mathf.Max(300,Screen.width-60),Mathf.Max(200,Screen.height-60));
        GUI.Box(rect,"Starseed Story Viewer - Asset pack");
        GUI.Label(new Rect(55,80,rect.width-50,rect.height-120),Message,new GUIStyle(GUI.skin.label){wordWrap=true,fontSize=18});
        if(GUI.Button(new Rect(55,Screen.height-90,140,35),"Close"))Application.Quit();
    }
}
