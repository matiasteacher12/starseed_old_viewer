using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

// A local performance cache, not an authenticity/security boundary. A changed
// pack, location, size, write time or creation time invalidates the entry.
public sealed class StoryContentVerification {
    [Serializable] class Cache {public string identity;public Entry[] entries;}
    [Serializable] class Entry {public string path,sha256;public long bytes,write,created;}
    readonly Dictionary<string,Entry> entries=new Dictionary<string,Entry>(StringComparer.OrdinalIgnoreCase);
    readonly string identity,path;
    public long HashedBytes {get;private set;}
    public int Hashes {get;private set;}
    public int Hits {get;private set;}
    public StoryContentVerification(string root,string manifestHash) {
        identity=Path.GetFullPath(root).ToUpperInvariant()+"|"+manifestHash+"|2";
        string directory=Path.Combine(Application.persistentDataPath,"ContentChecks");
        var args=Environment.GetCommandLineArgs();int i=Array.IndexOf(args,"-content-cache-dir");
        if(Application.isBatchMode&&i>=0)directory=Path.GetFullPath(args[i+1]);
        string key;using(var h=System.Security.Cryptography.SHA256.Create())key=BitConverter.ToString(h.ComputeHash(System.Text.Encoding.UTF8.GetBytes(identity))).Replace("-","");
        path=Path.Combine(directory,key+".json");
        try {
            if(File.Exists(path)) {
                var cache=JsonUtility.FromJson<Cache>(File.ReadAllText(path));
                if(cache!=null&&cache.identity==identity&&cache.entries!=null)
                    foreach(var e in cache.entries)if(e!=null&&!string.IsNullOrEmpty(e.path))entries[e.path]=e;
            }
        } catch(Exception e) when(e is IOException||e is ArgumentException||e is UnauthorizedAccessException) {Debug.LogWarning("STORY_VERIFY_CACHE_RESET "+e.GetType().Name);}
    }
    static FileInfo Stat(string full,StoryContent.FileEntry expected) {
        var f=new FileInfo(full);
        if(!f.Exists||f.Length!=expected.bytes)throw new InvalidDataException("Asset pack is missing or damaged: "+expected.path+". Reinstall this asset pack.");
        return f;
    }
    public bool IsVerified(string full,StoryContent.FileEntry expected) {
        var f=Stat(full,expected);
        if(entries.TryGetValue(expected.path,out var e)&&e.sha256==expected.sha256&&e.bytes==f.Length&&e.write==f.LastWriteTimeUtc.Ticks&&e.created==f.CreationTimeUtc.Ticks){Hits++;return true;}
        return false;
    }
    public string VerifyBytes(string full,StoryContent.FileEntry expected) {
        var before=Stat(full,expected);long time=before.LastWriteTimeUtc.Ticks,created=before.CreationTimeUtc.Ticks;
        string hash=StoryContent.Hash(full);var after=Stat(full,expected);
        if(hash!=expected.sha256||after.LastWriteTimeUtc.Ticks!=time||after.CreationTimeUtc.Ticks!=created)
            throw new InvalidDataException("Asset pack is missing or damaged: "+expected.path+". Reinstall this asset pack.");
        return time+":"+created;
    }
    public void Accept(StoryContent.FileEntry expected,string stamp) {
        var values=stamp.Split(':');entries[expected.path]=new Entry{path=expected.path,sha256=expected.sha256,bytes=expected.bytes,write=long.Parse(values[0]),created=long.Parse(values[1])};
        HashedBytes+=expected.bytes;Hashes++;
    }
    public void Save() {
        string temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(temp,JsonUtility.ToJson(new Cache{identity=identity,entries=entries.Values.ToArray()}));
            if(File.Exists(path))File.Replace(temp,path,null);else File.Move(temp,path);
        } catch(Exception e) when(e is IOException||e is UnauthorizedAccessException) {Debug.LogWarning("STORY_VERIFY_CACHE_NOT_SAVED "+e.GetType().Name);}
        finally {try {if(File.Exists(temp))File.Delete(temp);}catch(Exception e) when(e is IOException||e is UnauthorizedAccessException){}}
    }
}
