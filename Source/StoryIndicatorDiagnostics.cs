#if UNITY_EDITOR || VIEWER_BACKEND
using System;
using System.Collections;
using UnityEngine;

// Opt-in, read-only frame trace for verifying the original UI animation in a player.
public sealed class StoryIndicatorDiagnostics : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (Array.IndexOf(Environment.GetCommandLineArgs(), "-traceStoryIndicator") >= 0)
            new GameObject("Indicator diagnostics").AddComponent<StoryIndicatorDiagnostics>();
    }
    IEnumerator Start()
    {
        OriginalStoryUi ui;
        while (!(ui = FindObjectOfType<OriginalStoryUi>()) || !ui.Root) yield return null;
        var icon = ui.Root.transform.Find("Center/Dialogue/Pivot/Dialogue/Pivot/Name/Icon");
        var animation = icon.GetComponent<Animation>();
        var line = (RectTransform)icon.Find("Line_1");
        while (!ui.Dialogue.activeInHierarchy) yield return null;
        float end = Time.realtimeSinceStartup + 3;
        while (Time.realtimeSinceStartup < end) {
            yield return new WaitForEndOfFrame();
            Debug.Log("INDICATOR_FRAME time=" + Time.time.ToString("F3") +
                " visible=" + ui.Body.maxVisibleCharacters + " playing=" + animation.isPlaying +
                " height=" + line.sizeDelta.y.ToString("F3") + " clipTime=" + animation[animation.clip.name].time.ToString("F3"));
        }
        Destroy(gameObject);
    }
}

#endif
