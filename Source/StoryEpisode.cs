#if UNITY_EDITOR || VIEWER_BACKEND
using System;
using UnityEngine;

[Serializable] public sealed class StoryEpisode {
    public string id,title,entry,scene,summary;
    public string[] missingGroups;
    public int episodeNumber,dialogueCount;
    public Scene[] scenes;
    [Serializable] public class Scene {public string entry,title,fadeColor;public int number,fade;public bool hasFade;}
    public Group[] groups; public Actor[] actors; public Effect[] effects;
    public Slot[] slots; public Shot[] cameras; public Pivot[] pivots;
    public Timeline[] timelines;public SoundCue[] soundCues;
    [Serializable] public class Timeline {public string key,director;public float duration;}
    [Serializable] public class SoundCue {public string key;public string[] audio;}
    [Serializable] public class Group {public string key;public Step[] steps;}
    [Serializable] public class Step {public Command[] commands;}
    [Serializable] public class Command {public string name,text,speaker,resource,video,audioMode,sourceGap;public string[] args,audio;}
    [Serializable] public class Actor {public string key,asset;public int[] emotionIndices;public Makeup[] makeup;}
    [Serializable] public class Makeup {public string resource;public Color color;}
    [Serializable] public class Effect {public string key,asset;public Vector3 backgroundPosition;}
    [Serializable] public class Slot {public string key;public Vector3 position;public Quaternion rotation;}
    [Serializable] public class Shot {public string key;public Vector3 position;public Quaternion rotation;public float fov;public Vector2 lensShift;}
    [Serializable] public class Pivot {public string key;public float duration;public Curve[] curves;}
    [Serializable] public class Curve {public string path,property;public Key[] keys;}
    [Serializable] public class Key {public float time,value,inTangent,outTangent;}
}

#endif
