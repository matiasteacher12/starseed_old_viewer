#if UNITY_EDITOR || VIEWER_BACKEND
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public sealed partial class StoryScene {
    [Serializable] public class LoadingCatalogue {public string[] menuBundles;public LoadingBundle[] bundles;public LoadingEpisode[] episodes;}
    [Serializable] public class LoadingBundle {public string name;public string[] dependencies,assets;}
    [Serializable] public class LoadingEpisode {public string id,scene,resource;public string[] bundles,resources,files;public int actors;}
    LoadingCatalogue loadingCatalogue;
    bool episodeLoading;
    readonly Dictionary<string,AssetBundle> loadedBundles=new Dictionary<string,AssetBundle>(StringComparer.OrdinalIgnoreCase);
    readonly System.Diagnostics.Stopwatch startupWatch=System.Diagnostics.Stopwatch.StartNew();
    IEnumerator CatchLoadingFailure(IEnumerator operation,Action<Exception> failed) {
        var stack=new Stack<IEnumerator>();stack.Push(operation);
        while(stack.Count>0) {
            object current=null;bool moved=false;Exception failure=null;
            try {moved=stack.Peek().MoveNext();if(moved)current=stack.Peek().Current;}catch(Exception e){failure=e;}
            if(failure!=null){failed(failure);yield break;}
            if(!moved){stack.Pop();continue;}
            if(current is IEnumerator nested)stack.Push(nested);else yield return current;
        }
    }
    IEnumerator LoadStoryBundles(IEnumerable<string> names,Action<float> progress=null) {
        var unique=names.Distinct().ToArray();int completed=0;
        foreach(var name in unique) {
            if(loadedBundles.ContainsKey(name))continue;
            yield return StoryContent.VerifyAsync("Bundles/"+name);
            var request=AssetBundle.LoadFromFileAsync(StoryContent.FilePath("Bundles/"+name));yield return request;
            if(!request.assetBundle)throw new InvalidOperationException("Cannot load story bundle "+name);
            loadedBundles.Add(name,request.assetBundle);bundles.Add(request.assetBundle);
            progress?.Invoke(++completed/(float)unique.Length);
        }
        StoryContent.SaveVerification();
    }
    void BeginSelectedEpisode(OriginalStoryMenu.Episode selected) {
        if(episodeLoading)return;
        StartCoroutine(GuardEpisodeLoading(loadingCatalogue.episodes.Single(p=>p.id==selected.id)));
    }
    IEnumerator GuardEpisodeLoading(LoadingEpisode plan) {
        episodeLoading=true;ready=false;loading.text="이야기를 불러오는 중…";loading.transform.SetAsLastSibling();loading.gameObject.SetActive(true);
        // Flatten nested coroutines so verification/load failures are recoverable
        // in the normal menu, and fail automated checks explicitly.
        Exception failure=null;
        yield return CatchLoadingFailure(PrepareSelectedEpisode(plan),e=>failure=e);
        episodeLoading=false;
        if(failure!=null) {
            Debug.LogError("STORY_EPISODE_LOAD_FAILED "+plan.id+" "+failure.Message);
            ClearEpisodeActors();
            if(Application.isBatchMode){Application.Quit(1);yield break;}
            ready=true;storyMenu.ReturnFromEpisode(false);
            loading.text="자료를 불러오지 못했습니다. 애셋팩을 확인해 주세요.";loading.transform.SetAsLastSibling();
            yield break;
        }
        loading.gameObject.SetActive(false);ready=true;
        backgroundImage.gameObject.SetActive(true);actorImage.gameObject.SetActive(true);originalUi.Root.SetActive(true);ReplayEpisode();
    }
    IEnumerator PrepareSelectedEpisode(LoadingEpisode plan) {
        var watch=System.Diagnostics.Stopwatch.StartNew();long before=StoryContent.HashedBytes;
        if(episodeRoutine!=null){StopCoroutine(episodeRoutine);episodeRoutine=null;}
        if(endRoutine!=null){StopCoroutine(endRoutine);endRoutine=null;}
        if(fadeRoutine!=null){StopCoroutine(fadeRoutine);fadeRoutine=null;}
        if(focusRoutine!=null){StopCoroutine(focusRoutine);focusRoutine=null;}
        ClearStoryUiAnimation();StopStoryTimeline();StopEpisodeMovies();ClearStoryBackground();ClearGlobalStoryEffects();ClearMoods();
        originalUi.ResetView();originalUi.Root.SetActive(false);
        backgroundImage.texture=Texture2D.blackTexture;backgroundImage.gameObject.SetActive(false);actorImage.gameObject.SetActive(false);
        voice.Stop();music.Stop();ambienceAudio.Stop();
        originalUi.BeginEpisodeLog();voice.clip=null;effectsAudio.Stop();ambienceAudio.clip=null;music.clip=null;
        ClearEpisodeActors();effectCameraShaders=null;episode=null;
        yield return null; // Destroy old instances/log rows before unloading their assets.
        if(cinematicUnload!=null)yield return cinematicUnload;
        var keep=new HashSet<string>(loadingCatalogue.menuBundles);
        foreach(var name in loadedBundles.Keys.Where(n=>!keep.Contains(n)).ToArray()) {
            var b=loadedBundles[name];bundles.Remove(b);b.Unload(true);loadedBundles.Remove(name);
        }
        StoryContent.ReleaseTransientResources();
        // Also reclaim runtime-created clips/materials once their old instances
        // have gone, so repeatedly selecting episodes does not accumulate them.
        yield return Resources.UnloadUnusedAssets();
        yield return StoryContent.PrepareResources(new[]{plan.resource});
        episode=BackendJson.FromJson<StoryEpisode>(StoryContent.Load<TextAsset>(plan.resource).text);
        if(episode.scene!=plan.scene)throw new InvalidOperationException("Episode loading plan mismatch");
        var repairedAudio=StoryAudioRepairs.Apply(episode);
        yield return LoadStoryBundles(plan.bundles);
        yield return StoryContent.PrepareResources(plan.resources.Concat(repairedAudio));
        foreach(var key in repairedAudio)if(!StoryContent.Load<AudioClip>(key))throw new InvalidOperationException("Missing verified repair audio "+key);
        for(int i=0;i<plan.files.Length;i++) {
            loading.text="이야기를 불러오는 중… "+(i+1)+" / "+plan.files.Length;
            yield return StoryContent.VerifyAsync(plan.files[i]);
        }
        foreach(var definition in episode.actors) {
            var slot=new GameObject("Story slot "+definition.key);var pivot=new GameObject("Pivot");pivot.transform.SetParent(slot.transform,false);
            var component=pivot.AddComponent<StoryEpisodeActor>();
            episodeActors.Add(definition.key,component);
            component.Initialize(definition,Instantiate(OriginalAsset<GameObject>(definition.asset)),SetMaterialDefaults);
            BindActorAnimationEffects(component);component.Voice=voice;component.gameObject.SetActive(false);
            yield return null;
        }
        if(episodeActors.Count!=plan.actors)throw new InvalidOperationException("Episode actor set mismatch");
        StoryContent.SaveVerification();
        Debug.Log("STORY_EPISODE_LOADED id="+plan.id+" actors="+episodeActors.Count+" bundles="+loadedBundles.Count+" seconds="+watch.Elapsed.TotalSeconds.ToString("F3",System.Globalization.CultureInfo.InvariantCulture)+" hashedBytes="+(StoryContent.HashedBytes-before));
    }
    void ClearEpisodeActors() {
        occupied.Clear();
        foreach(var a in episodeActors.Values)if(a){a.gameObject.SetActive(false);Destroy(a.transform.parent.gameObject);}
        episodeActors.Clear();
    }
    IEnumerator WaitForEpisodeLoad() {while(episodeLoading)yield return null;}
}

#endif
