using System;
using System.Linq;
using UnityEngine;

public sealed partial class StoryScene {
    [Serializable] sealed class CameraBlendTable {
        public int defaultStyle; public float defaultDuration; public CameraBlendRule[] blends;
    }
    [Serializable] sealed class CameraBlendRule {
        public string fromCamera,toCamera; public int style; public float duration;
    }
    CameraBlendTable cameraBlendTable;
    StoryEpisode.Shot cameraFrom,cameraTo,cameraCurrent;
    string requestedCamera="Talk_Center",activeCamera;
    string activeZoom;
    string closeUpSlot;
    float cameraBlendElapsed,cameraBlendDuration;
    int cameraBlendStyle;
    Vector3 highlightOffset,highlightFrom,highlightTo;
    float highlightElapsed,highlightDuration;
    void MoveHighlight(bool use,Vector3 delta,float duration) {
        highlightFrom=highlightOffset;highlightTo=use?highlightOffset+delta:Vector3.zero;
        highlightElapsed=0;highlightDuration=duration;
        if(duration<=0)highlightOffset=highlightTo;
        Debug.Log("STORY_HIGHLIGHT_MOVE delta="+delta+" duration="+duration);
    }
    // Cinemachine 2.9 EaseIn: linear keys with outgoing tangent 1.4 and incoming 0.
    // https://github.com/Unity-Technologies/com.unity.cinemachine/blob/release/2.9/com.unity.cinemachine/Runtime/Core/CinemachineBlend.cs
    static readonly AnimationCurve CameraEaseIn=new AnimationCurve(new Keyframe(0,0,1,1.4f),new Keyframe(1,1,0,1));

    void ResetEpisodeCamera() {
        if(cameraBlendTable==null)cameraBlendTable=JsonUtility.FromJson<CameraBlendTable>(StoryContent.Load<TextAsset>("story-camera-blends").text);
        activeZoom=null;closeUpSlot=null;shotSlot="Center";requestedCamera="Talk_Center";activeCamera=requestedCamera;
        cameraCurrent=cameraTo=cameraFrom=episode.cameras.First(s=>s.key==requestedCamera);
        cameraBlendElapsed=cameraBlendDuration=0;ApplyEpisodeShot();
        highlightOffset=highlightFrom=highlightTo=Vector3.zero;highlightElapsed=highlightDuration=0;
    }
    void MoveEpisodeCamera(string slot) {
        bool wasCloseUp=closeUpSlot!=null;closeUpSlot=null;
        if(shotSlot==slot&&!wasCloseUp)return; // An active close-up must still be released.
        shotSlot=slot;
        requestedCamera=slot=="Center"?(activeZoom??"Talk_Center"):"Talk_"+slot;
    }
    void ZoomEpisodeCamera(string mode) {
        closeUpSlot=null;
        string target=string.IsNullOrEmpty(mode)?null:mode=="ZoomIn1_Large"?"Talk_Center_L_Zoom_01":mode=="ZoomIn2_Large"?"Talk_Center_L_Zoom_02":mode=="ZoomIn1_Small"?"Talk_Center_S_Zoom_01":mode=="ZoomIn1_Mid"?"Talk_Center_M_Zoom_01":mode=="ZoomIn2_Mid"?"Talk_Center_M_Zoom_02":mode=="ZoomIn2_Small"?"Talk_Center_S_Zoom_02":throw new InvalidOperationException("Unexported camera zoom "+mode);
        activeZoom=target;
        requestedCamera=target??"Talk_"+shotSlot;
        ApplyStoryZoom(mode); // Original background animation has its own duration.
    }
    void CloseUpEpisodeCamera(string mode) {
        // Original Windows CameraCloseUpType + level0 component 1085 array at
        // byte 540, independently verified in chapter6_camera_audit.json.
        string target;
        switch(mode) {
            case null:case "":case "None":target=null;break;
            case "Left":target="Talk_Left__Closeup";break;
            case "Center":target="Talk_Center_Closeup";break;
            case "Right":target="Talk_Right_Closeup";break;
            case "Left_Small":target="Talk_Left__Closeup_Small";break;
            case "Center_Small":target="Talk_Center_Closeup_Small";break;
            case "Right_Small":target="Talk_Right_Closeup_Small";break;
            case "Left_Ex":case "Center_Ex":case "Right_Ex":
            case "Left_Small_Ex":case "Center_Small_Ex":case "Right_Small_Ex":
            case "Left_Large_Ex":case "Center_Large_Ex":case "Right_Large_Ex":target="Talk_"+mode;break;
            default:throw new InvalidOperationException("Unexported close-up "+mode);
        }
        closeUpSlot=target==null?null:mode.Split('_')[0];
        requestedCamera=target??activeZoom??"Talk_"+shotSlot;
        ApplyStoryZoom(closeUpSlot==null?null:"explicit blur");
        Debug.Log("STORY_CLOSEUP "+mode+" camera="+requestedCamera);
    }
    CameraBlendRule ResolveCameraBlend(string from,string to) {
        const string any="**ANY CAMERA**";
        // Cinemachine prefers exact pairs, then a destination wildcard, then source.
        return cameraBlendTable.blends.LastOrDefault(r=>r.fromCamera==from&&r.toCamera==to)
            ??cameraBlendTable.blends.LastOrDefault(r=>r.fromCamera==any&&r.toCamera==to)
            ??cameraBlendTable.blends.LastOrDefault(r=>r.fromCamera==from&&r.toCamera==any)
            ??cameraBlendTable.blends.LastOrDefault(r=>r.fromCamera==any&&r.toCamera==any)
            ??new CameraBlendRule{style=cameraBlendTable.defaultStyle,duration=cameraBlendTable.defaultDuration};
    }
    void ApplyEpisodeShot() {
        if(cameraCurrent==null)return;
        foreach(var pair in occupied) {
            var slot=episode.slots.First(s=>s.key==pair.Key);
            // level0 SetRotationY serialized Origin/Change: Left 150/165,
            // Center 180/180, Right -150/-165. Preserve source x/z rotation.
            var angles=slot.rotation.eulerAngles;
            if(pair.Key==closeUpSlot)angles.y=pair.Key=="Left"?165:pair.Key=="Center"?180:-165;
            pair.Value.transform.parent.rotation=Quaternion.Euler(angles);
        }
        if(activeCamera!=requestedCamera) {
            var rule=ResolveCameraBlend(activeCamera=="Talk_Center"?"Talk_Highlight":activeCamera,requestedCamera=="Talk_Center"?"Talk_Highlight":requestedCamera);
            cameraFrom=cameraCurrent;cameraTo=episode.cameras.First(s=>s.key==requestedCamera);
            cameraBlendElapsed=0;cameraBlendDuration=rule.style==0?0:rule.duration;cameraBlendStyle=rule.style;
            Debug.Log("STORY_CAMERA_BLEND "+activeCamera+" -> "+requestedCamera+" duration="+cameraBlendDuration+" style="+cameraBlendStyle);
            activeCamera=requestedCamera;
        }
        cameraBlendElapsed+=Time.deltaTime;
        float t=cameraBlendDuration<=0?1:Mathf.Clamp01(cameraBlendElapsed/cameraBlendDuration);
        float weight=cameraBlendStyle==2?CameraEaseIn.Evaluate(t):cameraBlendStyle==0?1:throw new InvalidOperationException("Unimplemented original camera blend style "+cameraBlendStyle);
        cameraCurrent=new StoryEpisode.Shot{position=Vector3.Lerp(cameraFrom.position,cameraTo.position,weight),rotation=Quaternion.Slerp(cameraFrom.rotation,cameraTo.rotation,weight),fov=Mathf.Lerp(cameraFrom.fov,cameraTo.fov,weight),lensShift=Vector2.Lerp(cameraFrom.lensShift,cameraTo.lensShift,weight)};
        highlightElapsed+=Time.deltaTime;
        highlightOffset=Vector3.Lerp(highlightFrom,highlightTo,highlightDuration<=0?1:highlightElapsed/highlightDuration);
        actorCamera.transform.SetPositionAndRotation(cameraCurrent.position+(activeCamera=="Talk_Center"?highlightOffset:Vector3.zero),cameraCurrent.rotation);actorCamera.fieldOfView=cameraCurrent.fov;
        actorCamera.ResetProjectionMatrix();var projection=actorCamera.projectionMatrix;
        projection.m02=2*cameraCurrent.lensShift.x;projection.m12=2*cameraCurrent.lensShift.y;actorCamera.projectionMatrix=projection;
    }
}
