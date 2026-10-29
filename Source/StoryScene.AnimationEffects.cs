using UnityEngine;
using System.Linq;

public sealed partial class StoryScene {
    void BindActorAnimationEffects(StoryEpisodeActor actor) {
        var manifest=StoryContent.Load<TextAsset>("story-animation-effects");
        if(!manifest)return;
        var data=JsonUtility.FromJson<StoryAnimationEffects.Manifest>(manifest.text);
        var row=data.actors.FirstOrDefault(a=>a.actor==actor.Definition.key);
        if(row==null)return;
        var receiver=actor.Animator.gameObject.AddComponent<StoryAnimationEffects>();
        receiver.Owner=actor;receiver.Data=row;
        receiver.Load=asset=>OriginalAsset<GameObject>(asset);receiver.Configure=ConfigureEffectComposite;
    }
}
