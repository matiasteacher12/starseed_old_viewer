using System;
using System.IO;
using UnityEngine;

// Startup and story playback share these preferences. Batch probes never write
// the user's PlayerPrefs; optional persistence probes use an explicit test file.
public static class StoryAudioPreferences {
    [Serializable] class State {public bool muted;public float volume=.03f;}
    static string TestPath {
        get {var args=Environment.GetCommandLineArgs();int i=Array.IndexOf(args,"-audio-settings-file");return Application.isBatchMode&&i>=0?Path.GetFullPath(args[i+1]):null;}
    }
    public static void Load(out bool muted,out float volume){
        if(Application.isBatchMode){
            var path=TestPath;var state=path!=null&&File.Exists(path)?JsonUtility.FromJson<State>(File.ReadAllText(path)):new State{muted=true};
            muted=state.muted;volume=state.volume;
        } else {muted=PlayerPrefs.GetInt("viewer.audio.muted",0)!=0;volume=PlayerPrefs.GetFloat("viewer.audio.volume",.03f);}
        if(float.IsNaN(volume)||float.IsInfinity(volume))volume=.03f;volume=Mathf.Clamp(volume,0,.2f);
    }
    public static void Save(bool muted,float volume){
        if(Application.isBatchMode){var path=TestPath;if(path!=null){Directory.CreateDirectory(Path.GetDirectoryName(path));string tmp=path+".tmp";File.WriteAllText(tmp,JsonUtility.ToJson(new State{muted=muted,volume=volume}));if(File.Exists(path))File.Replace(tmp,path,null);else File.Move(tmp,path);}return;}
        PlayerPrefs.SetInt("viewer.audio.muted",muted?1:0);PlayerPrefs.SetFloat("viewer.audio.volume",volume);PlayerPrefs.Save();
    }
}
