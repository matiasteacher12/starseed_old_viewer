using System;
using System.Collections;
using System.Runtime.InteropServices;
using UnityEngine;

// Restrict the client area, excluding the DPI-dependent caption and borders.
// https://learn.microsoft.com/windows/win32/winmsg/wm-sizing
// https://learn.microsoft.com/windows/win32/winmsg/wm-windowposchanging
public sealed class StoryWindowAspect : MonoBehaviour {
    [StructLayout(LayoutKind.Sequential)] public struct Bounds {
        public int left,top,right,bottom;
        public int Width=>right-left; public int Height=>bottom-top;
    }
    public static Bounds Constrain(Bounds rect,int borderWidth,int borderHeight,int edge) {
        int width=Math.Max(1,rect.Width-borderWidth),height=Math.Max(1,rect.Height-borderHeight);
        // Round the dependent axis only. Quantizing both axes into 16x9 blocks
        // made slow mouse drags jump by 16 pixels.
        if(edge==3 || edge==6) {height=Math.Max(360,height);width=(int)Math.Round(height*16.0/9);}
        else if(edge==1 || edge==2) {width=Math.Max(640,width);height=(int)Math.Round(width*9.0/16);}
        else if(Math.Abs(width*9-height*16)<=8) { /* already pixel-rounded */ }
        else if(width*9>height*16) {height=Math.Max(360,height);width=(int)Math.Round(height*16.0/9);}
        else {width=Math.Max(640,width);height=(int)Math.Round(width*9.0/16);}
        width=Math.Max(640,width)+borderWidth;height=Math.Max(360,height)+borderHeight;
        if(edge==1 || edge==4 || edge==7)rect.left=rect.right-width;else rect.right=rect.left+width;
        if(edge==3 || edge==4 || edge==5)rect.top=rect.bottom-height;else rect.bottom=rect.top+height;
        return rect;
    }
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
    const int WndProcIndex=-4,StyleIndex=-16,ExStyleIndex=-20;
    const uint Sizing=0x214,WindowPosChanging=0x46,GetMinMaxInfo=0x24,SysCommand=0x112,Size=5;
    IntPtr window,previous,callbackPointer;
    WindowProcedure callback;
    bool minimizing;
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate IntPtr WindowProcedure(IntPtr hwnd,uint message,IntPtr wParam,IntPtr lParam);
    delegate bool EnumProcedure(IntPtr hwnd,IntPtr data);
    [StructLayout(LayoutKind.Sequential)] struct Position {public IntPtr hwnd,after;public int x,y,cx,cy;public uint flags;}
    [StructLayout(LayoutKind.Sequential)] struct Point {public int x,y;}
    [StructLayout(LayoutKind.Sequential)] struct MinMax {public Point reserved,maxSize,maxPosition,minTrack,maxTrack;}
    [StructLayout(LayoutKind.Sequential)] struct MonitorInfo {public uint size;public Bounds monitor,work;public uint flags;}
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProcedure callback,IntPtr data);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd,out uint process);
    [DllImport("kernel32.dll")] static extern uint GetCurrentProcessId();
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int GetClassName(IntPtr hwnd,System.Text.StringBuilder text,int max);
    [DllImport("user32.dll",EntryPoint="SetWindowLongPtrW",SetLastError=true)] static extern IntPtr SetWindowLongPtr(IntPtr hwnd,int index,IntPtr value);
    [DllImport("user32.dll",EntryPoint="GetWindowLongPtrW")] static extern IntPtr GetWindowLongPtr(IntPtr hwnd,int index);
    [DllImport("user32.dll",EntryPoint="CallWindowProcW")] static extern IntPtr CallWindowProc(IntPtr proc,IntPtr hwnd,uint msg,IntPtr wParam,IntPtr lParam);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hwnd,out Bounds bounds);
    [DllImport("user32.dll")] static extern bool GetClientRect(IntPtr hwnd,out Bounds bounds);
    [DllImport("user32.dll")] static extern uint GetDpiForWindow(IntPtr hwnd);
    [DllImport("user32.dll")] static extern bool AdjustWindowRectExForDpi(ref Bounds rect,uint style,bool menu,uint exStyle,uint dpi);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hwnd,IntPtr after,int x,int y,int cx,int cy,uint flags);
    [DllImport("user32.dll")] static extern IntPtr MonitorFromWindow(IntPtr hwnd,uint flags);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern bool GetMonitorInfo(IntPtr monitor,ref MonitorInfo info);
    [DllImport("user32.dll")] static extern bool IsIconic(IntPtr hwnd);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern IntPtr CreateWindowEx(uint exStyle,string className,string name,uint style,int x,int y,int width,int height,IntPtr parent,IntPtr menu,IntPtr instance,IntPtr param);
    [DllImport("user32.dll")] static extern bool DestroyWindow(IntPtr hwnd);
    [DllImport("user32.dll",EntryPoint="SendMessageW")] static extern IntPtr SendMessage(IntPtr hwnd,uint msg,IntPtr wParam,IntPtr lParam);

    IEnumerator Start() {
        if(Application.isBatchMode)yield break;
        for(int retry=0;retry<120;retry++) {
            IntPtr found=IntPtr.Zero;uint process=GetCurrentProcessId();
            EnumWindows((h,p)=>{GetWindowThreadProcessId(h,out var owner);var name=new System.Text.StringBuilder(64);GetClassName(h,name,64);if(owner==process && name.ToString()=="UnityWndClass"){found=h;return false;}return true;},IntPtr.Zero);
            if(found!=IntPtr.Zero){Attach(found);yield break;}
            yield return null;
        }
        Debug.LogError("STORY_WINDOW_ASPECT_FAILED Unity window not found");
    }
    void Frame(out int width,out int height) {
        var rect=new Bounds();
        uint dpi=GetDpiForWindow(window);
        if(!AdjustWindowRectExForDpi(ref rect,(uint)GetWindowLongPtr(window,StyleIndex).ToInt64(),false,(uint)GetWindowLongPtr(window,ExStyleIndex).ToInt64(),dpi==0?96:dpi))throw new InvalidOperationException("Window frame measurement failed");
        width=rect.Width;height=rect.Height;
    }
    void Attach(IntPtr handle) {
        window=handle;callback=Handle;callbackPointer=Marshal.GetFunctionPointerForDelegate(callback);
        previous=SetWindowLongPtr(window,WndProcIndex,callbackPointer);
        if(previous==IntPtr.Zero)throw new InvalidOperationException("Window aspect hook failed: "+Marshal.GetLastWin32Error());
        Frame(out int bw,out int bh);GetWindowRect(window,out var bounds);bounds=Constrain(bounds,bw,bh,0);
        SetWindowPos(window,IntPtr.Zero,0,0,bounds.Width,bounds.Height,0x16); // no move / z-order / activation
        Debug.Log("STORY_WINDOW_ASPECT_READY client=16:9");
    }
    IntPtr Handle(IntPtr hwnd,uint message,IntPtr wParam,IntPtr lParam) {
        try {
            if(message==SysCommand){long command=wParam.ToInt64()&0xfff0;if(command==0xf020)minimizing=true;else if(command==0xf120)minimizing=false;}
            if(message==Size)minimizing=wParam.ToInt64()==1;
            if(message==Sizing) {
                Frame(out int bw,out int bh);
                var rect=Constrain(Marshal.PtrToStructure<Bounds>(lParam),bw,bh,wParam.ToInt32());
                Marshal.StructureToPtr(rect,lParam,false);return new IntPtr(1);
            }
            if(message==WindowPosChanging && !minimizing && !IsIconic(hwnd)) {
                var pos=Marshal.PtrToStructure<Position>(lParam);
                if((pos.flags&1)==0 && pos.cx>0 && pos.cy>0) {
                    Frame(out int bw,out int bh);
                    var rect=Constrain(new Bounds{right=pos.cx,bottom=pos.cy},bw,bh,0);
                    pos.cx=rect.Width;pos.cy=rect.Height;Marshal.StructureToPtr(pos,lParam,false);
                }
            }
            if(message==GetMinMaxInfo) {
                var result=CallWindowProc(previous,hwnd,message,wParam,lParam);
                var info=Marshal.PtrToStructure<MinMax>(lParam);Frame(out int bw,out int bh);
                info.minTrack=new Point{x=640+bw,y=360+bh};
                var monitor=new MonitorInfo{size=(uint)Marshal.SizeOf<MonitorInfo>()};
                if(GetMonitorInfo(MonitorFromWindow(hwnd,2),ref monitor)) {
                    var fit=Constrain(new Bounds{right=monitor.work.Width,bottom=monitor.work.Height},bw,bh,0);
                    info.maxSize=new Point{x=fit.Width,y=fit.Height};info.maxTrack=info.maxSize;
                    info.maxPosition=new Point{x=monitor.work.left-monitor.monitor.left+(monitor.work.Width-fit.Width)/2,y=monitor.work.top-monitor.monitor.top+(monitor.work.Height-fit.Height)/2};
                }
                Marshal.StructureToPtr(info,lParam,false);return result;
            }
        } catch(Exception e){Debug.LogError("STORY_WINDOW_ASPECT_ERROR "+e.Message);}
        return CallWindowProc(previous,hwnd,message,wParam,lParam);
    }
    void Detach() {
        if(window!=IntPtr.Zero && previous!=IntPtr.Zero && GetWindowLongPtr(window,WndProcIndex)==callbackPointer)SetWindowLongPtr(window,WndProcIndex,previous);
        window=previous=IntPtr.Zero;callback=null;
    }
    void OnDestroy(){Detach();}
    public void CheckHiddenWindow() {
        // A private, never-shown test window. No desktop input or other app access.
        var test=CreateWindowEx(0,"STATIC","Story aspect test",0x00cf0000,0,0,900,600,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero);
        if(test==IntPtr.Zero)throw new Exception("Could not create hidden aspect test window");
        try {
            Attach(test);Frame(out int bw,out int bh);
            foreach(var size in new[]{new Vector2Int(1000,900),new Vector2Int(1700,500),new Vector2Int(400,300)}) {
                SetWindowPos(test,IntPtr.Zero,0,0,size.x,size.y,0x16);GetClientRect(test,out var client);
                if(Math.Abs(client.Width*9-client.Height*16)>8)throw new Exception("Native resize aspect incorrect: "+client.Width+"x"+client.Height);
            }
            for(int edge=1;edge<=8;edge++) {
                var pointer=Marshal.AllocHGlobal(Marshal.SizeOf<Bounds>());
                try {Marshal.StructureToPtr(new Bounds{left=100,top=100,right=1371+100,bottom=829+100},pointer,false);SendMessage(test,Sizing,new IntPtr(edge),pointer);var rect=Marshal.PtrToStructure<Bounds>(pointer);
                    if(Math.Abs((rect.Width-bw)*9-(rect.Height-bh)*16)>8)throw new Exception("Sizing edge failed: "+edge);
                } finally {Marshal.FreeHGlobal(pointer);}
            }
            for(int width=800;width<1000;width++) {
                var sized=Constrain(new Bounds{right=width+bw,bottom=700+bh},bw,bh,2);
                var repeated=Constrain(sized,bw,bh,0);
                if(sized.Width-bw!=width || repeated.Width!=sized.Width || repeated.Height!=sized.Height)throw new Exception("Resize quantized or drifted");
            }
            Debug.Log("STORY_WINDOW_ASPECT_CHECK_OK nativeResize=true edges=8 clientRatio=16:9 hiddenWindow=true");
        } finally {Detach();DestroyWindow(test);}
    }
#else
    public void CheckHiddenWindow() {}
#endif
}
