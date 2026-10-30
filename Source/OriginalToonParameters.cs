#if UNITY_EDITOR || VIEWER_BACKEND
using UnityEngine;

// Shader globals from the original PP_nilo_talk profile and renderer settings.
// The original shader remains intact; this supplies its missing game-side driver.
public static class OriginalToonParameters
{
    public static void Apply(Camera camera, Light mainLight)
    {
        foreach(var key in new[]{"_GlobalMainDirectionalLightMultiplier","_GlobalMainDirectionalLightMaxContribution",
            "_GlobalVolumeMulColor","_GlobalOutlineTintColor"})
            Shader.SetGlobalVector(key,Vector4.one);
        Shader.SetGlobalVector("_GlobalVolumeBaseColorTintColor",new Vector4(.9607843f,.9607843f,.9607843f,1));
        Shader.SetGlobalVector("_GlobalVolumeLerpColor",new Vector4(1,1,1,.1f));
        Shader.SetGlobalVector("_GlobalIndirectLightMultiplier",Vector4.zero);
        Shader.SetGlobalVector("_GlobalIndirectLightMinColor",new Vector4(.01f,.01f,.01f,0));
        Shader.SetGlobalVector("_GlobalSpecularTintColor",new Vector4(3,3,3,0));
        Shader.SetGlobalFloat("_GlobalSpecularInShadowMinIntensity",.25f);
        Shader.SetGlobalFloat("_GlobalOcclusionStrength",1);
        Shader.SetGlobalFloat("_GlobalShouldRenderOutline",1);
        Shader.SetGlobalFloat("_GlobalOutlineWidthMultiplier",1);
        Shader.SetGlobalFloat("_GlobalOutlineWidthAutoAdjustToCameraDistanceAndFOV",1);
        Shader.SetGlobalFloat("_GlobalAspectFix",1);
        Shader.SetGlobalFloat("_GlobalFOVorOrthoSizeFix",1);
        Shader.SetGlobalFloat("_CurrentCameraFOV",30);
        // SetToonParamPass multiplies both CharRenderingControl and ShadowControl
        // profiles. PP_nilo_talk uses (2/3, .4) in the former and (1, 1) in the latter.
        Shader.SetGlobalVector("_GlobalCharacterOverallShadowTintColor",new Vector4(2f/3,2f/3,2f/3,0));
        Shader.SetGlobalFloat("_GlobalCharacterOverallShadowStrength",.4f);
        Shader.SetGlobalFloat("_GlobalDepthTexRimLightAndShadowWidthMultiplier",2);
        Shader.SetGlobalFloat("_GlobalDepthTexRimLightCameraDistanceFadeoutStartDistance",1000);
        Shader.SetGlobalFloat("_GlobalDepthTexRimLightCameraDistanceFadeoutEndDistance",2000);
        Shader.SetGlobalVector("_GlobalRimLightMultiplier",new Vector4(3,3,3,0));
        // Original SetToonParamPass (0x32AD470): rotate Vector3.forward in VIEW
        // space, then blend with the existing light in each space and normalize.
        // Camera matrices flip Z; rotating Vector3.back in world space reverses Y.
        var originalWS=-mainLight.transform.forward;
        var originalVS=camera.worldToCameraMatrix.MultiplyVector(originalWS);
        var overrideVS=Quaternion.Euler(-50,0,0)*Vector3.forward;
        var overrideWS=camera.cameraToWorldMatrix.MultiplyVector(overrideVS);
        var directionWS=Vector3.Lerp(originalWS,overrideWS,.569f).normalized;
        var directionVS=Vector3.Lerp(originalVS,overrideVS,.569f).normalized;
        Shader.SetGlobalVector("_GlobalUserOverridedMainLightDirWS",directionWS);
        Shader.SetGlobalVector("_GlobalUserOverridedMainLightDirVS",directionVS);
    }
}

#endif
