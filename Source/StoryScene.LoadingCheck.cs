#if UNITY_EDITOR || VIEWER_BACKEND
using System;
using System.Collections;
using System.Linq;
using UnityEngine;

public sealed partial class StoryScene {
    IEnumerator CheckDemandLoading() {
        if(!Application.isBatchMode)yield break;
        if(episode!=null||episodeActors.Count!=0||!storyMenu.Visible||loadedBundles.Count!=loadingCatalogue.menuBundles.Length)
            throw new Exception("Startup loaded episode content before selection");
        ready=false;int maximum=0;
        // Null mesh pointers verified in the preserved Android prefab graph.
        var authoredEmpty=new[]{"Character/Prefabs/c_ranger_mana_1|addition1","Character/Prefabs/npc_pollux_1|vr","Character/Prefabs/npc_pollux_1|body_shadow","Character/Prefabs/npc_pollux_1|remote"};
        foreach(var plan in loadingCatalogue.episodes) {
            yield return PrepareSelectedEpisode(plan);
            maximum=Math.Max(maximum,episodeActors.Count);
            if(episodeActors.Count!=episode.actors.Length||episodeActors.Values.Any(a=>a.gameObject.activeSelf))throw new Exception("Unexpected actor residency");
            var scenes=bundles.SelectMany(b=>b.GetAllScenePaths()).ToArray();
            foreach(var timeline in episode.timelines)if(!scenes.Any(s=>s.EndsWith("/"+timeline.key+".unity",StringComparison.OrdinalIgnoreCase)))throw new Exception("Missing selected timeline "+timeline.key);
            foreach(var actor in episodeActors.Values) {
                if(!actor.Animator.runtimeAnimatorController||actor.Animator.runtimeAnimatorController.animationClips.Length==0)throw new Exception("Missing actor controller "+actor.Definition.key);
                foreach(var renderer in actor.GetComponentsInChildren<SkinnedMeshRenderer>(true))if(renderer.enabled&&!renderer.sharedMesh&&!authoredEmpty.Contains(actor.Definition.key+"|"+renderer.name))throw new Exception("Missing selected actor mesh "+actor.Definition.key+"/"+renderer.name);
            }
            // These are the media keys the renderer can request for this episode.
            foreach(var key in plan.resources)if(!StoryContent.Load<UnityEngine.Object>(key))throw new Exception("Missing selected media "+key);
            Debug.Log("DEMAND_EPISODE_OK "+plan.id);
        }
        Debug.Log("DEMAND_LOADING_OK episodes="+loadingCatalogue.episodes.Length+" maxActors="+maximum+" hashes="+StoryContent.Hashes+" hashedBytes="+StoryContent.HashedBytes);
        Application.Quit(0);
    }
}

#endif
