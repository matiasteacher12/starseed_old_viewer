#if UNITY_EDITOR || VIEWER_BACKEND
using System;
using System.IO;
using System.Text;
using System.Runtime.Serialization.Json;
using System.Xml;
using System.Xml.Linq;
using System.Reflection;
using System.Globalization;
using System.Linq;

// Unity's native JsonUtility type metadata is baked into the player. Nested
// records from a newly loaded assembly need a managed serializer instead.
// Unity objects (e.g. TMP settings) still use JsonUtility.FromJsonOverwrite.
public static class BackendJson {
    public static T FromJson<T>(string json){using(var reader=JsonReaderWriterFactory.CreateJsonReader(Encoding.UTF8.GetBytes(json),new XmlDictionaryReaderQuotas{MaxDepth=64,MaxArrayLength=1000000,MaxStringContentLength=16777216,MaxBytesPerRead=65536,MaxNameTableCharCount=65536}))return (T)Read(XElement.Load(reader),typeof(T));}
    static object Read(XElement node,Type type){
        if((string)node.Attribute("type")=="null")return null;
        if(type==typeof(string))return node.Value;
        if(type.IsEnum)return Enum.ToObject(type,Convert.ToInt32(node.Value,CultureInfo.InvariantCulture));
        if(type.IsPrimitive||type==typeof(decimal))return Convert.ChangeType(node.Value,type,CultureInfo.InvariantCulture);
        if(type.IsArray){var children=node.Elements().ToArray();var element=type.GetElementType();var values=Array.CreateInstance(element,children.Length);for(int i=0;i<children.Length;i++)values.SetValue(Read(children[i],element),i);return values;}
        var result=Activator.CreateInstance(type,true);
        foreach(var child in node.Elements()){var field=type.GetField(child.Name.LocalName,BindingFlags.Public|BindingFlags.Instance);if(field!=null&&!field.IsInitOnly)field.SetValue(result,Read(child,field.FieldType));}
        return result;
    }
    public static string ToJson(object value){using(var stream=new MemoryStream()){using(var writer=JsonReaderWriterFactory.CreateJsonWriter(stream,Encoding.UTF8,false)){Write("root",value).WriteTo(writer);writer.Flush();}return Encoding.UTF8.GetString(stream.ToArray());}}
    static XElement Write(string name,object value){var node=new XElement(name);if(value==null){node.SetAttributeValue("type","null");return node;}var type=value.GetType();
        if(type==typeof(string)){node.SetAttributeValue("type","string");node.Value=(string)value;}
        else if(type.IsPrimitive||type.IsEnum||type==typeof(decimal)){node.SetAttributeValue("type",type==typeof(bool)?"boolean":"number");node.Value=type==typeof(bool)?((bool)value?"true":"false"):Convert.ToString(value,CultureInfo.InvariantCulture);}
        else if(type.IsArray){node.SetAttributeValue("type","array");foreach(var item in (Array)value)node.Add(Write("item",item));}
        else{node.SetAttributeValue("type","object");foreach(var field in type.GetFields(BindingFlags.Public|BindingFlags.Instance))if(!field.IsNotSerialized)node.Add(Write(field.Name,field.GetValue(value)));}return node;
    }
}
#endif
