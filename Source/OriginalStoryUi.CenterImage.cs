using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

public sealed partial class OriginalStoryUi {
    Coroutine centerImageRoutine;
    Sprite centerImageSprite;
    public bool CenterImageVisible => Find("Center/CenterImage").gameObject.activeSelf;
    public void ShowCenterImage(string resource) {
        var node=Find("Center/CenterImage").gameObject;
        if(centerImageRoutine!=null){StopCoroutine(centerImageRoutine);centerImageRoutine=null;}
        if(!string.IsNullOrEmpty(resource)) {
            var texture=StoryContent.Load<Texture2D>(resource);
            if(!texture)throw new InvalidOperationException("Missing center image "+resource);
            if(centerImageSprite)Destroy(centerImageSprite);
            centerImageSprite=Sprite.Create(texture,new Rect(0,0,texture.width,texture.height),new Vector2(.5f,.5f),100);
            Find("Center/CenterImage/Pivot/Img").GetComponent<Image>().sprite=centerImageSprite;
            if(!node.activeSelf){node.SetActive(true);node.GetComponent<Animation>().Play("TalkScene_Center_Image_In");}
        } else if(node.activeSelf)centerImageRoutine=StartCoroutine(HideCenterImage());
        Debug.Log("STORY_CENTER_IMAGE "+resource);
    }
    IEnumerator HideCenterImage() {
        var node=Find("Center/CenterImage").gameObject;var animation=node.GetComponent<Animation>();
        animation.Play("TalkScene_Center_Image_Out");
        while(animation.isPlaying)yield return null;
        node.SetActive(false);centerImageRoutine=null;
    }
    void ResetCenterImage() {
        if(centerImageRoutine!=null){StopCoroutine(centerImageRoutine);centerImageRoutine=null;}
        var node=Find("Center/CenterImage");node.GetComponent<Animation>().Stop();node.gameObject.SetActive(false);
    }
}
