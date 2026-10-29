using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

// Runtime-only decoded pictures. The immutable content catalog/store remains authoritative.
// Visible rows own leases; only unused least-recent pictures may be evicted.
public sealed class InstarseedImageCache {
    public const long Budget=48L*1024*1024;
    sealed class Entry {public Texture2D texture;public Sprite sprite;public int users;public long bytes,stamp;}
    readonly Dictionary<string,Entry> entries=new Dictionary<string,Entry>();
    long clock;bool retain=true;
    public long Bytes {get;private set;}
    public int Decodes {get;private set;}
    public int Hits {get;private set;}
    public long PeakBytes {get;private set;}
    public Sprite Acquire(string file,out Action release){
        if(!entries.TryGetValue(file,out var e)){
            var texture=new Texture2D(2,2,TextureFormat.RGBA32,false);
            try {if(!texture.LoadImage(File.ReadAllBytes(StoryContent.FilePath(file)),true))throw new InvalidDataException(file);}
            catch {UnityEngine.Object.Destroy(texture);throw;}
            e=new Entry{texture=texture,sprite=Sprite.Create(texture,new Rect(0,0,texture.width,texture.height),new Vector2(.5f,.5f)),bytes=(long)texture.width*texture.height*4};
            entries.Add(file,e);Bytes+=e.bytes;Decodes++;
        }else Hits++;
        e.users++;e.stamp=++clock;Trim();PeakBytes=Math.Max(PeakBytes,Bytes);
        bool released=false;release=()=>{if(released)return;released=true;e.users--;e.stamp=++clock;Trim();};return e.sprite;
    }
    public void Retain(bool value){retain=value;Trim();}
    void Trim(){
        while(Bytes>(retain?Budget:0)){
            var candidate=entries.Where(p=>p.Value.users==0).OrderBy(p=>p.Value.stamp).FirstOrDefault();
            if(candidate.Value==null)break;var e=candidate.Value;entries.Remove(candidate.Key);Bytes-=e.bytes;
            UnityEngine.Object.Destroy(e.sprite);UnityEngine.Object.Destroy(e.texture);
        }
    }
    public void Check(){
        if(entries.Values.Any(e=>e.users<0||!e.sprite||!e.texture||e.texture.isReadable))throw new Exception("Invalid picture cache lease");
        long pinned=entries.Values.Where(e=>e.users>0).Sum(e=>e.bytes);
        if(Bytes>Math.Max(Budget,pinned))throw new Exception("Picture cache exceeds budget");
    }
}
