using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.TextCore;
using UnityEngine.TextCore.LowLevel;

// A static, single-glyph atlas from an installed Windows face. TMP 3.0.6 cannot
// create a dynamic atlas from CreateDynamicFontFromOSFont (it lacks font bytes).
// Use Unity's same glyph rasterizer/packer as TMP without copying system fonts.
public static class StorySymbolFont {
    public static TMP_FontAsset Create(string file,uint unicode) {
        string path=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts),file);
        if(!File.Exists(path))return null;
        var font=TMP_FontAsset.CreateFontAsset(StoryContent.Load<Font>("Original/MFont_ko"));
        font.name="Installed symbol U+"+unicode.ToString("X4");font.atlasPopulationMode=AtlasPopulationMode.Static;
        if(FontEngine.LoadFontFace(path,90)!=FontEngineError.Success||!FontEngine.TryGetGlyphWithUnicodeValue(unicode,GlyphLoadFlags.LOAD_DEFAULT,out var source))
            throw new InvalidOperationException("Missing installed symbol "+font.name+" in "+file);
        font.faceInfo=FontEngine.GetFaceInfo();
        var atlas=font.atlasTextures[0];atlas.Resize(1024,1024);atlas.SetPixels32(new Color32[1024*1024]);atlas.Apply();
        var pack=typeof(FontEngine).GetMethods(BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic)
            .Single(m=>m.Name=="TryAddGlyphToTexture"&&m.GetParameters().Length==8&&m.GetParameters()[0].ParameterType==typeof(uint));
        object[] args={source.index,9,GlyphPackingMode.BestShortSideFit,new List<GlyphRect>{new GlyphRect(0,0,1023,1023)},new List<GlyphRect>(),GlyphRenderMode.SDFAA,atlas,null};
        if(!(bool)pack.Invoke(null,args))throw new InvalidOperationException("Cannot rasterize "+font.name);
        var glyph=(Glyph)args[7];font.glyphTable.Clear();font.characterTable.Clear();
        font.glyphTable.Add(glyph);font.characterTable.Add(new TMP_Character(unicode,font,glyph));
        font.ReadFontAssetDefinition();
        if(!font.HasCharacter((char)unicode))throw new InvalidOperationException("Missing generated symbol "+font.name);
        Debug.Log("STORY_WINDOWS_SYMBOL_READY U+"+unicode.ToString("X4")+" face="+font.faceInfo.familyName);
        return font;
    }
}
