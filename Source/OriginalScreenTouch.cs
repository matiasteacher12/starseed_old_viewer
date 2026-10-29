using System.Collections.Generic;
using Coffee.UIExtensions;
using UnityEngine;

public sealed class OriginalScreenTouch : MonoBehaviour
{
    readonly List<UIParticle> pool = new List<UIParticle>();
    RectTransform stage;
    GameObject prefab;
    int next;

    public void Initialize(Transform parent, GameObject source)
    {
        stage = (RectTransform)parent; prefab = source;
        if (!prefab || prefab.GetComponentsInChildren<ParticleSystem>(true).Length == 0)
            throw new System.InvalidOperationException("Original screen touch prefab could not be loaded.");
        Debug.Log("STORY_SCREEN_TOUCH_READY particles=" + prefab.GetComponentsInChildren<ParticleSystem>(true).Length);
    }
    void LateUpdate()
    {
        if (!prefab || !Input.GetMouseButtonDown(0)) return;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(stage, Input.mousePosition, null, out var position)) return;
        if (!stage.rect.Contains(position)) return;
        ShowAt(position);
    }
    public UIParticle ShowAt(Vector2 position) {
        UIParticle effect;
        if (pool.Count < 8) {
            var go = Instantiate(prefab, stage, false);
            go.transform.localScale = Vector3.one;
            effect = go.GetComponent<UIParticle>();
            if (!effect) {
                // The scene's MonoScript reference resolves in the Editor but
                // not in the standalone player. Bind the same open-source UI
                // renderer explicitly; retain all six original particle systems.
                effect = go.AddComponent<UIParticle>();
                effect.scale3D = new Vector3(80, 80, 80);
                effect.autoScalingMode = UIParticle.AutoScalingMode.Transform;
                effect.RefreshParticles();
            }
            foreach (var g in go.GetComponentsInChildren<UnityEngine.UI.Graphic>(true)) g.raycastTarget = false;
            pool.Add(effect);
        } else { effect = pool[next]; next = (next + 1) % pool.Count; }
        var rt = (RectTransform)effect.transform;
        rt.localScale = Vector3.one; rt.anchoredPosition = position; rt.SetAsLastSibling();
        effect.gameObject.SetActive(true); effect.Clear(); effect.Play();
        Debug.Log("STORY_SCREEN_TOUCH " + position);
        return effect;
    }
}
