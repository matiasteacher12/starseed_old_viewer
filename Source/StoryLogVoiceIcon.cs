using UnityEngine;
using UnityEngine.UI;

// Vector icon keeps the play/stop control crisp at any window scale.
public sealed class StoryLogVoiceIcon : MaskableGraphic {
    bool playing;
    public bool Playing {get=>playing;set{playing=value;SetVerticesDirty();}}
    protected override void OnPopulateMesh(VertexHelper mesh) {
        mesh.Clear();var r=rectTransform.rect;var c=(Color32)color;
        if(playing){
            mesh.AddVert(new Vector3(r.xMin,r.yMin),c,Vector2.zero);mesh.AddVert(new Vector3(r.xMin,r.yMax),c,Vector2.zero);
            mesh.AddVert(new Vector3(r.xMax,r.yMax),c,Vector2.zero);mesh.AddVert(new Vector3(r.xMax,r.yMin),c,Vector2.zero);
            mesh.AddTriangle(0,1,2);mesh.AddTriangle(2,3,0);
        }else{
            mesh.AddVert(new Vector3(r.xMin,r.yMin),c,Vector2.zero);mesh.AddVert(new Vector3(r.xMin,r.yMax),c,Vector2.zero);
            mesh.AddVert(new Vector3(r.xMax,0),c,Vector2.zero);mesh.AddTriangle(0,1,2);
        }
    }
}
