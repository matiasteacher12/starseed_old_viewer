using UnityEngine;

public sealed partial class StoryScene {
    GameObject scanlinePlane;
    void SetStoryScanline(string slot,bool enabled) {
        if(string.IsNullOrEmpty(slot))slot="Left"; // ScriptSlotType default value 0.
        if(!occupied.TryGetValue(slot,out var character))return;
        if(!scanlinePlane) {
            scanlinePlane=GameObject.CreatePrimitive(PrimitiveType.Plane);
            scanlinePlane.name="Original ScanlinePlane";scanlinePlane.layer=17;
            Destroy(scanlinePlane.GetComponent<Collider>());
            scanlinePlane.transform.SetPositionAndRotation(new Vector3(0,15.5f,-39),Quaternion.Euler(-90,0,0));
            scanlinePlane.transform.localScale=Vector3.one*.15f;
            scanlinePlane.GetComponent<Renderer>().sharedMaterial=OriginalAsset<Material>("story-scanline.mat");
        }
        character.SetScanline(enabled);
        Debug.Log("STORY_SCANLINE "+slot+" "+enabled);
    }
}
