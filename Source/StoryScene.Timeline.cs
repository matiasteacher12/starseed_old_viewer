#if UNITY_EDITOR || VIEWER_BACKEND
using System;
using System.Collections;
using System.Linq;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering.Universal;

public sealed partial class StoryScene {
    Scene cinematicScene;RenderTexture cinematicTarget;bool cinematicPlaying;
    AsyncOperation cinematicUnload;
    UnityEngine.UI.RawImage cinematicSurface;
    void ShowCinematicSurface(Texture texture) {
        if(!cinematicSurface) {
            var go=new GameObject("Original timeline surface",typeof(RectTransform),typeof(UnityEngine.UI.RawImage));
            go.transform.SetParent(stage,false);cinematicSurface=go.GetComponent<UnityEngine.UI.RawImage>();
            var rect=cinematicSurface.rectTransform;rect.anchorMin=Vector2.zero;rect.anchorMax=Vector2.one;
            rect.offsetMin=rect.offsetMax=Vector2.zero;cinematicSurface.raycastTarget=true;
        }
        // Native PlayTimeline hides the talk scene roots while its own camera
        // runs. Keep the script fade intact underneath, for the later FadeIn.
        cinematicSurface.texture=texture;cinematicSurface.gameObject.SetActive(true);
        cinematicSurface.transform.SetAsLastSibling();
    }
    void HideCinematicSurface() {
        if(!cinematicSurface)return;
        cinematicSurface.gameObject.SetActive(false);cinematicSurface.texture=null;
    }
    IEnumerator PlayStoryTimeline(string key) {
        StopEpisodeMovies();originalUi.HideAllDialogue();originalUi.SetMovieMode(true);
        string scenePath=bundles.SelectMany(b=>b.GetAllScenePaths()).Single(p=>System.IO.Path.GetFileNameWithoutExtension(p)==key);
        MSoundClip.Resolve=cue=>episode.soundCues.Where(c=>c.key==cue).SelectMany(c=>c.audio).Select(StoryContent.Load<AudioClip>).ToArray();
        yield return SceneManager.LoadSceneAsync(scenePath,LoadSceneMode.Additive);
        cinematicScene=SceneManager.GetSceneByPath(scenePath);var roots=cinematicScene.GetRootGameObjects();
        var definition=episode.timelines.First(t=>t.key==key);
        var directorName=string.IsNullOrEmpty(definition.director)?key:definition.director;
        var director=roots.SelectMany(r=>r.GetComponentsInChildren<PlayableDirector>(true)).Single(d=>d.name==directorName);
        cinematicTarget=new RenderTexture(1120,630,24);cinematicTarget.Create();
        foreach(var root in roots) {
            foreach(var listener in root.GetComponentsInChildren<AudioListener>(true))listener.enabled=false;
            foreach(var canvas in root.GetComponentsInChildren<Canvas>(true))canvas.enabled=false;
            foreach(var camera in root.GetComponentsInChildren<Camera>(true)) {
                camera.targetTexture=cinematicTarget;
                camera.gameObject.AddComponent<StoryCinematicCamera>();
                var data=camera.GetUniversalAdditionalCameraData();data.SetRenderer(0);
            }
            foreach(var renderer in root.GetComponentsInChildren<Renderer>(true))foreach(var material in renderer.materials)
                if(material.shader.name.Contains("Nilo")) {
                    SetMaterialDefaults(material,renderer.transform);
                    material.SetVector("_CharacterBoundCenterPosWS",renderer.bounds.center);
                    material.SetFloat("_CharacterBoundRadius",renderer.bounds.extents.magnitude);
                }
        }
        ShowCinematicSurface(cinematicTarget);SetFocusAmount(0);actorImage.gameObject.SetActive(false);
        cinematicPlaying=true;director.Play();
        Debug.Log("STORY_TIMELINE_BEGIN "+key+" duration="+director.duration);
        float deadline=Time.realtimeSinceStartup+(float)director.duration+30;
        bool inspected=false;
        while(director.state==PlayState.Playing && Time.realtimeSinceStartup<deadline) {
            if(!inspected && director.time>1) {
                inspected=true;
                Debug.Log("TIMELINE_GRAPH "+key+" valid="+director.playableGraph.IsValid()+" outputs="+director.playableGraph.GetOutputCount());
                foreach(var c in roots.SelectMany(r=>r.GetComponentsInChildren<Camera>(true)))
                    Debug.Log("TIMELINE_CAMERA "+c.name+" active="+c.isActiveAndEnabled+" pos="+c.transform.position+" rot="+c.transform.eulerAngles+" mask="+c.cullingMask+" target="+c.targetTexture);
                foreach(var d in roots.SelectMany(r=>r.GetComponentsInChildren<PlayableDirector>(true)))
                    Debug.Log("TIMELINE_DIRECTOR "+d.name+" active="+d.isActiveAndEnabled+" state="+d.state+" time="+d.time+" outputs="+(d.playableGraph.IsValid()?d.playableGraph.GetOutputCount():-1));
                if(Application.isBatchMode) {
                    foreach(var c in roots.SelectMany(r=>r.GetComponentsInChildren<Camera>()).Where(c=>c.isActiveAndEnabled).OrderBy(c=>c.depth))c.Render();
                    SaveStoryTarget(cinematicTarget,System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath,"../../RenderChecks/cinematic-"+key+".png")));
                }
            }
            yield return null;
        }
        if(director.state==PlayState.Playing)throw new InvalidOperationException("Timeline did not complete "+key);
        Debug.Log("STORY_TIMELINE_END "+key);
        cinematicPlaying=false;HideCinematicSurface();yield return SceneManager.UnloadSceneAsync(cinematicScene);
        cinematicScene=default;cinematicTarget.Release();Destroy(cinematicTarget);cinematicTarget=null;
        backgroundImage.texture=Texture2D.blackTexture;actorImage.gameObject.SetActive(true);originalUi.SetMovieMode(false);
    }
    void StopStoryTimeline() {
        cinematicPlaying=false;HideCinematicSurface();
        if(cinematicScene.IsValid() && cinematicScene.isLoaded)cinematicUnload=SceneManager.UnloadSceneAsync(cinematicScene);
        cinematicScene=default;
        if(cinematicTarget){cinematicTarget.Release();Destroy(cinematicTarget);cinematicTarget=null;}
        actorImage.gameObject.SetActive(true);
    }
}

#endif
