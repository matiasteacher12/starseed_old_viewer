#if UNITY_EDITOR || VIEWER_BACKEND
using System;
using System.Collections;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

public sealed partial class StoryScene {
    IEnumerator ConfirmMeetingSkip() {
        originalUi.ToggleSummary();yield return new WaitForSecondsRealtime(.3f);
        var popup=stage.Cast<Transform>().Single(t=>t.gameObject.activeInHierarchy&&t.Find("Window/Contents/Skip")!=null);
        popup.Find("Window/Button/Btn_Ok").GetComponent<Button>().onClick.Invoke();
        yield return new WaitForSecondsRealtime(.8f);
    }
    IEnumerator CheckMeetingFiveSkips(Camera camera,RenderTexture target,string folder) {
        foreach(var scene in new[]{"Chapter1_7","Chapter1_8","Chapter1_9","Chapter1_10","Chapter1_11"}) {
            storyMenu.OpenEpisode(storyMenu.Catalogue.chapters.SelectMany(c=>c.episodes).First(e=>e.scene==scene));
            yield return WaitForEpisodeLoad();
            string anchors=string.Join(",",StorySkipPlan.Build(episode).Select(a=>a.group+":"+a.step));
            if(anchors!=(scene=="Chapter1_7"?"Chapter1_7_2_02:7":""))throw new Exception("Unexpected authored skip anchors "+scene+" "+anchors);
            float deadline=Time.realtimeSinceStartup+40;
            while((!episodeWaiting||processingCommands||originalUi.Busy)&&Time.realtimeSinceStartup<deadline)yield return null;
            if(!episodeWaiting)throw new Exception("Skip entry stalled "+scene);
            yield return ConfirmMeetingSkip();
            if(scene=="Chapter1_7") {
                deadline=Time.realtimeSinceStartup+40;
                while((!movieBlocking||!storyVideo||storyVideo.time<1)&&Time.realtimeSinceStartup<deadline)yield return null;
                if(!movieBlocking||!storyVideo.isPlaying||currentGroup!="Chapter1_7_2_02")throw new Exception("Skip omitted original middle movie");
                Canvas.ForceUpdateCanvases();camera.Render();SaveStoryTarget(target,Path.Combine(folder,"skip-07-movie.png"));
                yield return ConfirmMeetingSkip(); // Movie confirmation resumes script, not menu.
                deadline=Time.realtimeSinceStartup+40;
                while((!episodeWaiting||processingCommands||originalUi.Busy)&&Time.realtimeSinceStartup<deadline)yield return null;
                if(storyMenu.Visible||!episodeWaiting||movieBlocking)throw new Exception("Movie skip did not resume dialogue");
                yield return ConfirmMeetingSkip();
            }
            deadline=Time.realtimeSinceStartup+8;while(!storyMenu.Visible&&Time.realtimeSinceStartup<deadline)yield return null;
            if(!storyMenu.Visible||storyUiAnimation||movieBlocking)throw new Exception("Final skip did not return to menu "+scene);
            Debug.Log("STORY_MEETING_FIVE_SKIP_OK "+scene+" anchors="+anchors);
        }
    }
}

#endif
