using System;
using System.IO;
using System.Threading;
using System.Runtime.InteropServices;
using UnityEngine;

// Immutable SHA-256 objects. Python importers and the player use the same byte
// range lock; catalog snapshots are read atomically and objects never overwrite.
public sealed class StoryAssetStore {
    [Serializable] class Identity {public int schemaVersion;public string storeId;}
    readonly string directory;
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)]
    static extern bool CreateHardLink(string fileName,string existingFile,IntPtr securityAttributes);
    public StoryAssetStore(string contentDirectory,string relative,string expectedId) {
        if(string.IsNullOrEmpty(relative)||Path.IsPathRooted(relative)||relative.Contains(":"))throw new InvalidDataException("Invalid asset store location");
        directory=Path.GetFullPath(Path.Combine(contentDirectory,relative));
        if(Path.GetFileName(directory)!="asset-store")throw new InvalidDataException("Expected asset-store directory");
        using(Acquire()) {
            var identity=JsonUtility.FromJson<Identity>(File.ReadAllText(Path.Combine(directory,"store.json")));
            if(identity==null||identity.schemaVersion!=1||identity.storeId!=expectedId)throw new InvalidDataException("Different asset store identity");
        }
    }
    public IDisposable Acquire()=>new Lease(Path.Combine(directory,"store.lock"));
    public static IDisposable AcquireDirectory(string directory)=>new Lease(Path.Combine(directory,"store.lock"));
    public string Resolve(StoryContent.FileEntry entry) {
        if(entry.sha256==null||!System.Text.RegularExpressions.Regex.IsMatch(entry.sha256,"^[0-9a-f]{64}$"))throw new InvalidDataException("Invalid object hash");
        // No lookup at the old logical path, even when a compatibility link exists.
        string canonical=Path.Combine(directory,"referenced",entry.sha256.Substring(0,2),entry.sha256);
        if(!entry.path.EndsWith(".mp4",StringComparison.OrdinalIgnoreCase))return canonical;
        // Windows Media Foundation needs a recognized extension. This view is
        // another name for the same NTFS file, not a second copy of the movie.
        // Resolve is called with the store lease held; readers verify the view
        // against the catalog just like every other resolved file.
        if(!File.Exists(canonical))throw new FileNotFoundException("Missing canonical movie",canonical);
        string views=Path.Combine(directory,"media-views");Directory.CreateDirectory(views);
        string view=Path.Combine(views,entry.sha256+".mp4");
        if(!File.Exists(view)&&!CreateHardLink(view,canonical,IntPtr.Zero))
            throw new IOException("Cannot create movie reference (Windows error "+Marshal.GetLastWin32Error()+"). Extract the viewer onto a writable NTFS drive.");
        return view;
    }
    sealed class Lease:IDisposable {
        FileStream stream;
        public Lease(string path) {
            stream=new FileStream(path,FileMode.Open,FileAccess.ReadWrite,FileShare.ReadWrite);
            var deadline=DateTime.UtcNow.AddSeconds(30);
            while(true) {
                try {stream.Lock(0,1);break;}
                catch(IOException) {if(DateTime.UtcNow>=deadline){stream.Dispose();throw new IOException("Asset store import is busy. Retry after it completes.");}Thread.Sleep(20);}
            }
        }
        public void Dispose(){if(stream==null)return;try{stream.Unlock(0,1);}finally{stream.Dispose();stream=null;}}
    }
}
