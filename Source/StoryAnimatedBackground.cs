#if UNITY_EDITOR || VIEWER_BACKEND
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Rendering.Universal;

// Preserve the authored Canvas/Animation/UIParticle draw, but feed its HDR colour
// into the talk camera BEFORE geometry/bloom instead of the final overlay.
public sealed class StoryAnimatedBackground : MonoBehaviour {
    public const int Layer=30;
    public Camera Camera {get;private set;}
    public RenderTexture Target {get;private set;}
    public RectTransform Stage {get;private set;}
    RawImage fallback;
    public void Initialize(RectTransform overlay,RawImage background,Camera talkCamera) {
        Camera=new GameObject("Animated background HDR camera").AddComponent<Camera>();
        Camera.transform.SetParent(transform,false);Camera.transform.position=new Vector3(10000,10000,-10);
        Camera.cullingMask=1<<Layer;Camera.orthographic=true;Camera.orthographicSize=360;
        Camera.clearFlags=CameraClearFlags.SolidColor;Camera.backgroundColor=Color.black;
        Camera.depth=talkCamera.depth-1;Camera.allowHDR=true;Camera.allowMSAA=false;
        Camera.GetUniversalAdditionalCameraData().renderPostProcessing=false;
        var canvas=new GameObject("Animated background source canvas",typeof(Canvas),typeof(CanvasScaler));
        canvas.transform.SetParent(transform,false);canvas.layer=Layer;
        var cv=canvas.GetComponent<Canvas>();cv.renderMode=RenderMode.ScreenSpaceCamera;cv.worldCamera=Camera;cv.planeDistance=1;
        var scaler=canvas.GetComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution=new Vector2(1280,720);scaler.screenMatchMode=CanvasScaler.ScreenMatchMode.Expand;
        Stage=new GameObject("Stage",typeof(RectTransform)).GetComponent<RectTransform>();Stage.SetParent(canvas.transform,false);
        Stage.anchorMin=Stage.anchorMax=Stage.pivot=new Vector2(.5f,.5f);Stage.sizeDelta=new Vector2(1280,720);Stage.gameObject.layer=Layer;
        fallback=new GameObject("Animated background colour",typeof(RectTransform),typeof(RawImage)).GetComponent<RawImage>();
        fallback.transform.SetParent(overlay,false);fallback.transform.SetSiblingIndex(background.transform.GetSiblingIndex()+1);
        fallback.rectTransform.anchorMin=fallback.rectTransform.anchorMax=fallback.rectTransform.pivot=new Vector2(.5f,.5f);
        fallback.rectTransform.sizeDelta=new Vector2(1280,720);fallback.raycastTarget=false;
        Resize();
    }
    void LateUpdate(){Resize();}
    void Resize() {
        int width=Mathf.Max(1280,Screen.width),height=Mathf.RoundToInt(width*9f/16);
        if(Target&&Target.width==width)return;
        ReleaseTarget();
        Target=new RenderTexture(width,height,24,RenderTextureFormat.ARGBHalf);Target.Create();
        Camera.targetTexture=Target;fallback.texture=Target;StoryBloomFeature.AnimatedBackground=Target;
    }
    void ReleaseTarget(){if(!Target)return;Camera.targetTexture=null;if(StoryBloomFeature.AnimatedBackground==Target)StoryBloomFeature.AnimatedBackground=null;Target.Release();Destroy(Target);Target=null;}
    void OnDisable(){if(Camera)Camera.enabled=false;if(fallback)fallback.gameObject.SetActive(false);if(StoryBloomFeature.AnimatedBackground==Target)StoryBloomFeature.AnimatedBackground=null;}
    void OnDestroy(){ReleaseTarget();if(fallback)Destroy(fallback.gameObject);}
}

#endif
