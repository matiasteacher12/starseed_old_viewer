#if UNITY_EDITOR || VIEWER_BACKEND
using System;
using System.Collections;
using System.IO;
using System.Linq;
using UnityEngine;

public sealed partial class StoryScene {
    [Serializable] class MainSuite {public MainSuiteEntry[] entries;}
    [Serializable] class MainSuiteEntry {public string id;public OldCheckPlan plan;}
    IEnumerator CheckAllMain() {
        if(!Application.isBatchMode)yield break;
        var args=Environment.GetCommandLineArgs();int i=Array.IndexOf(args,"-all-main-check");
        var suite=BackendJson.FromJson<MainSuite>(File.ReadAllText(args[i+1]));
        if(loadingCatalogue.episodes.Length!=94||storyMenu.Catalogue.chapters.SelectMany(c=>c.episodes).Count(e=>e.playable)!=94)throw new Exception("Main catalogue is incomplete");
        foreach(var entry in suite.entries) {
            var selected=storyMenu.Catalogue.chapters.SelectMany(c=>c.episodes).Single(e=>e.scene==entry.plan.scene);
            storyMenu.ShowChapter(storyMenu.Catalogue.chapters.Single(c=>c.episodes.Contains(selected)));
            Debug.Log("ALL_MAIN_ROUTE_BEGIN "+entry.id);
            storyMenu.OpenEpisode(selected);
            yield return WaitForEpisodeLoad();
            yield return CheckOldEpisode(true,entry.plan,false);
            Debug.Log("ALL_MAIN_ROUTE_OK "+entry.id);
            yield return Resources.UnloadUnusedAssets();
        }
        Debug.Log("ALL_MAIN_SUITE_OK routes="+suite.entries.Length);
        Application.Quit(0);
    }
}

#endif
