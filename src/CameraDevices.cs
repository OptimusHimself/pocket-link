using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using Microsoft.Win32;

static class CameraDevices {
    [ComImport,Guid("29840822-5B84-11D0-BD3B-00A0C911CE86"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]interface IDeviceEnum{[PreserveSig]int CreateClassEnumerator(ref Guid category,out IEnumMoniker enumerator,int flags);}
    [ComImport,Guid("55272A00-42CB-11CE-8135-00AA004BB851"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]interface IPropertyBag{[PreserveSig]int Read([MarshalAs(UnmanagedType.LPWStr)]string name,[MarshalAs(UnmanagedType.Struct)]out object value,IntPtr log);[PreserveSig]int Write([MarshalAs(UnmanagedType.LPWStr)]string name,[MarshalAs(UnmanagedType.Struct)]ref object value);}
    public sealed class Info {public string Name,Registration,Module;}
    public static List<Info> Inspect(){
        object device=Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("62BE5D10-60EB-11D0-BD3B-00A0C911CE86")));IEnumMoniker enumerator=null;
        try{Guid category=new Guid("860BB310-5D01-11D0-BD3B-00A0C911CE86");int result=((IDeviceEnum)device).CreateClassEnumerator(ref category,out enumerator,0);if(result<0)Marshal.ThrowExceptionForHR(result);var names=new List<Info>();if(result!=0||enumerator==null)return names;
            var items=new IMoniker[1];while(enumerator.Next(1,items,IntPtr.Zero)==0){object storage=null;try{Guid iid=typeof(IPropertyBag).GUID;items[0].BindToStorage(null,null,ref iid,out storage);object value;if(((IPropertyBag)storage).Read("FriendlyName",out value,IntPtr.Zero)==0&&value is string){string registration;items[0].GetDisplayName(null,null,out registration);string module=null;var ids=System.Text.RegularExpressions.Regex.Matches(registration,@"\{[0-9a-fA-F-]{36}\}");if(ids.Count>0){using(var key=Registry.ClassesRoot.OpenSubKey(@"CLSID\"+ids[ids.Count-1].Value+@"\InprocServer32")){if(key!=null)module=(key.GetValue(null) as string??"").Trim('"');}}names.Add(new Info{Name=(string)value,Registration=registration,Module=module});}}finally{if(storage!=null)Marshal.ReleaseComObject(storage);Marshal.ReleaseComObject(items[0]);}}return names;
        }finally{if(enumerator!=null)Marshal.ReleaseComObject(enumerator);Marshal.ReleaseComObject(device);}
    }
    public static List<string> Names(){return Inspect().ConvertAll(x=>x.Name);}
    public static bool HasObs(){return Inspect().Exists(x=>String.Equals(x.Name,"OBS Virtual Camera",StringComparison.OrdinalIgnoreCase)&&(x.Module==null||System.IO.File.Exists(x.Module)));}
}
