using System;
using System.Collections.Generic;
using System.Reflection;
using NW.FairyToon;
using UnityEngine;

// Runs the actual runtime sources without loading or launching Unity.
static class WetnessRuntimeTests
{
    static int checks;
    static void Near(float a, float b, string label, float tolerance = .0002f)
    { checks++; if (float.IsNaN(a) || Math.Abs(a-b)>tolerance) throw new Exception(label+": expected "+b+", got "+a); }
    static void Is(bool ok, string label) { checks++; if(!ok) throw new Exception(label); }
    static void Call(object o, string name) => o.GetType().GetMethod(name, BindingFlags.NonPublic|BindingFlags.Instance).Invoke(o,null);
    static void Step(FairyToonWetness w,float dt)
    { Time.deltaTime=dt; Time.realtimeSinceStartupAsDouble+=dt; Call(w,"Update"); Call(w,"LateUpdate"); }
    static FairyToonWaterVolume Volume(float y,float yaw=0)
    {
        var v=new FairyToonWaterVolume();
        v.transform.position=new Vector3(0,y-1,0); v.transform.eulerAngles=new Vector3(0,yaw,0);
        v.box=new BoxCollider {transform=v.transform,size=new Vector3(4,2,2)};
        Call(v,"OnEnable"); return v;
    }
    static void Main()
    {
        var low=Volume(.4f); var high=Volume(.8f);
        Is(FairyToonWaterVolume.TryGetSurface(Vector3.zero,out float y),"volume inside"); Near(y,.8f,"highest");
        Is(!FairyToonWaterVolume.TryGetSurface(new Vector3(0,1.31f,0),out y),"ceiling tolerance");
        Is(FairyToonWaterVolume.TryGetSurface(new Vector3(0,-10,0),out y),"no bottom gate");
        Call(high,"OnDisable"); FairyToonWaterVolume.TryGetSurface(Vector3.zero,out y); Near(y,.4f,"unregister");
        Call(low,"OnDisable"); var rotated=Volume(.4f,45);
        Is(FairyToonWaterVolume.TryGetSurface(new Vector3(1.3f,0,-1.3f),out y),"yaw long axis inside");
        Is(!FairyToonWaterVolume.TryGetSurface(new Vector3(1.3f,0,1.3f),out y),"yaw short axis outside AABB");
        rotated.box.enabled=false; Is(!FairyToonWaterVolume.TryGetSurface(Vector3.zero,out y),"disabled collider");
        rotated.box.enabled=true; rotated.transform.lossyScale=new Vector3(2,1,.5f); rotated.box.center=new Vector3(.2f,0,.1f);
        var inside=rotated.transform.TransformPoint(rotated.box.center+new Vector3(1.9f,0,.5f));
        var outside=rotated.transform.TransformPoint(rotated.box.center+new Vector3(0,0,1.1f));
        Is(FairyToonWaterVolume.TryGetSurface(inside,out y),"offset scaled yaw inside");
        Is(!FairyToonWaterVolume.TryGetSurface(outside,out y),"offset scaled yaw outside");
        Call(rotated,"OnDisable");
        var w=new FairyToonWetness(); Call(w,"OnEnable"); w.SetWaterSurface(.4f); Step(w,.1f);
        Near(w.Wetness,.4f,"soak speed"); Near(w.SoakHeight,.44f,"wicking"); Step(w,.15f); Near(w.Wetness,1,"full soak");
        w.SetWaterSurface(.2f); Step(w,1); Near(w.SoakHeight,.44f,"high water memory"); Near(w.TimeSinceLeft,0,"submerged timer");
        w.ClearWaterSurface(); Step(w,30); Near(w.Wetness,2f/3,"30s"); Near(w.SoakHeight,.396f,"30s sink"); Near(w.TimeSinceLeft,30,"timer");
        Step(w,45); Near(w.Wetness,1f/6,"75s"); Near(w.SoakHeight,.33f,"75s sink"); Step(w,20);
        Near(w.Wetness,0,"95s"); Near(w.SoakHeight,0,"dry height resets"); Near(w.WaterSurfaceY,-100000,"absent surface");
        w.SetWaterSurface(.91f); Step(w,1); w.ClearWaterSurface();
        for(int i=0;i<750;i++) Step(w,.1f);
        Near(w.Wetness,1f/6,"frame partition wet",.001f); Near(w.SoakHeight,.7125f,"frame partition sink",.001f);
        float departureHeight=w.SoakHeight;
        w.SetWaterSurface(.4f); Step(w,.25f);
        Near(w.Wetness,1,"partial dry then reentry resoaks"); Near(w.SoakHeight,departureHeight,"shallower reentry retains line");
        w.ClearWaterSurface(); Step(w,45); Near(w.SoakHeight,departureHeight*.85f,"reentry resets dry anchor");
        w.rain=.5f; Step(w,5); Near(w.RainWetness,.25f,"rain rises"); Step(w,20); Near(w.RainWetness,.5f,"rain target");
        w.rain=.2f; Step(w,9); Near(w.RainWetness,.4f,"lessening rain dries over dryTime");
        Step(w,18); Near(w.RainWetness,.2f,"lessening rain stops at positive target");
        w.rain=.5f; Step(w,6); Near(w.RainWetness,.5f,"rain increases over rainSoakTime");
        w.rain=0; Step(w,22.5f); Near(w.RainWetness,.25f,"rain dries");
        low=Volume(.4f); w.SetWaterSurface(-2); Step(w,1); Near(w.WaterSurfaceY,-2,"override priority");
        w.ClearWaterSurface(); w.Push(); Near(w.WaterSurfaceY,.4f,"override cleared"); Call(low,"OnDisable");
        w.simulate=false; w.SoakHeight=.95f; w.Wetness=.67f; w.RainWetness=.2f; w.TimeSinceLeft=30;
        w.transform.position=new Vector3(0,2,0); Step(w,1000); Near(w.Wetness,.67f,"capture frozen");
        var g=Shader.values[Shader.PropertyToID("_FT_Wet0")];
        Near(g.x,2.95f,"line world Y"); Near(g.y,.67f,"global wet"); Near(g.z,-100000,"global surface"); Near(g.w,30,"global timer");
        Near(Shader.values[Shader.PropertyToID("_FT_Wet1")].x,.2f,"global rain");
        Application.isPlaying=false; w.simulate=true;
        for(int i=0;i<200;i++) Step(w,1);
        Near(w.Wetness,.67f,"edit mode preserves serialized wetness by default");
        Near(w.SoakHeight,.95f,"edit mode preserves serialized soak height");
        Near(w.RainWetness,.2f,"edit mode preserves serialized rain wetness");
        Near(w.TimeSinceLeft,30,"edit mode preserves serialized departure timer");
        w.SoakHeight=.5f; w.Wetness=.8f; w.Push();
        Near(Shader.values[Shader.PropertyToID("_FT_Wet0")].x,2.5f,"explicit edit capture still publishes");
        Near(w.Wetness,.8f,"Push never simulates edit state");
        w.simulateInEditMode=true; Step(w,1000);
        Near(w.Wetness,.8f-.1f/90f,"opt-in edit preview clamps idle gap");
        Near(w.TimeSinceLeft,30.1f,"opt-in edit preview advances bounded time");
        w.simulate=false; Step(w,1);
        Near(w.Wetness,.8f-.1f/90f,"simulate false still freezes opt-in editor");
        w.simulateInEditMode=false; w.simulate=true; Application.isPlaying=true;
        Step(w,9); Near(w.Wetness,.7f-.1f/90f,"play simulation independent of editor toggle");
        Call(w,"OnDisable"); Near(Shader.values[Shader.PropertyToID("_FT_Wet0")].y,0,"clear wet");
        Near(Shader.values[Shader.PropertyToID("_FT_Wet1")].x,0,"clear rain");
        Console.WriteLine("Wetness runtime: "+checks+" assertions passed (production sources; Unity API test doubles).");
    }
}
namespace UnityEngine
{
    class ExecuteAlways:Attribute {} class DisallowMultipleComponent:Attribute {}
    class RequireComponent:Attribute {public RequireComponent(Type t){}}
    class Tooltip:Attribute {public Tooltip(string s){}}
    class Min:Attribute {public Min(float x){}}
    class Range:Attribute {public Range(float a,float b){}}
    class SerializeField:Attribute {}
    class RuntimeInitializeOnLoadMethodAttribute:Attribute {public RuntimeInitializeOnLoadMethodAttribute(RuntimeInitializeLoadType t){}}
    enum RuntimeInitializeLoadType {SubsystemRegistration}
    public class Object {public static implicit operator bool(Object o)=>o!=null;}
    public class MonoBehaviour:Object
    {
        public Transform transform=new Transform(); public BoxCollider box; public bool isActiveAndEnabled=true;
        public GameObject gameObject=new GameObject(); public T GetComponent<T>() where T:class=>box as T;
    }
    public class GameObject:Object {public bool activeInHierarchy=true;}
    public class Transform
    {
        public Vector3 position,eulerAngles,lossyScale=Vector3.one;
        public Vector3 TransformPoint(Vector3 p) {p=Vector3.Scale(p,lossyScale);float a=eulerAngles.y*Mathf.Deg2Rad;
            return position+new Vector3(p.x*Mathf.Cos(a)+p.z*Mathf.Sin(a),p.y,-p.x*Mathf.Sin(a)+p.z*Mathf.Cos(a));}
    }
    public class BoxCollider:Object
    {
        public bool enabled=true,isTrigger;public Transform transform; public Vector3 center,size;
        public Bounds bounds=>new Bounds(transform.TransformPoint(center),Vector3.Scale(size,transform.lossyScale));
    }
    public struct Bounds
    {public Vector3 center,size; public Bounds(Vector3 c,Vector3 s){center=c;size=s;} public Vector3 max=>center+size*.5f;}
    public struct Vector3
    {
        public float x,y,z; public Vector3(float a,float b,float c){x=a;y=b;z=c;}
        public static Vector3 zero=>new Vector3();public static Vector3 one=>new Vector3(1,1,1);
        public static Vector3 Scale(Vector3 a,Vector3 b)=>new Vector3(a.x*b.x,a.y*b.y,a.z*b.z);
        public static Vector3 operator +(Vector3 a,Vector3 b)=>new Vector3(a.x+b.x,a.y+b.y,a.z+b.z);
        public static Vector3 operator -(Vector3 a,Vector3 b)=>new Vector3(a.x-b.x,a.y-b.y,a.z-b.z);
        public static Vector3 operator *(Vector3 a,float b)=>new Vector3(a.x*b,a.y*b,a.z*b);
    }
    public struct Vector4
    {public float x,y,z,w;public Vector4(float a,float b,float c,float d){x=a;y=b;z=c;w=d;}public static Vector4 zero=>new Vector4();}
    public struct Color {public Color(float a,float b,float c,float d){}}
    public static class Gizmos {public static Color color;public static void DrawLine(Vector3 a,Vector3 b){}}
    public static class Shader
    {public static Dictionary<int,Vector4> values=new Dictionary<int,Vector4>();public static int PropertyToID(string s)=>s.GetHashCode();
        public static void SetGlobalVector(int i,Vector4 v)=>values[i]=v;}
    public static class Time {public static float deltaTime;public static double realtimeSinceStartupAsDouble;}
    public static class Application {public static bool isPlaying=true;public static bool IsPlaying(GameObject o)=>isPlaying;}
    public static class Mathf
    {
        public const float Deg2Rad=(float)Math.PI/180;public static float Sin(float a)=>(float)Math.Sin(a);
        public static float Cos(float a)=>(float)Math.Cos(a);public static float Abs(float a)=>Math.Abs(a);
        public static float Max(float a,float b)=>Math.Max(a,b);public static float Min(float a,float b)=>Math.Min(a,b);
        public static float Clamp(float a,float b,float c)=>Max(b,Min(c,a));public static float Clamp01(float a)=>Clamp(a,0,1);
        public static float Lerp(float a,float b,float t)=>a+(b-a)*Clamp01(t);
        public static float MoveTowards(float a,float b,float d)=>Abs(b-a)<=d?b:a+Math.Sign(b-a)*d;
    }
}
