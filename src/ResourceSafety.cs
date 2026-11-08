using System;
using System.Collections.Generic;

// A timed-out native call cannot be safely aborted or reused in this process.
static class ResourceSafety {
    static readonly object gate=new object();static readonly Dictionary<PhoneResource,string> blocked=new Dictionary<PhoneResource,string>();
    public static void Block(PhoneResource resource,string reason){lock(gate)blocked[resource]=reason;}
    public static Dictionary<string,string> Snapshot(){lock(gate){var copy=new Dictionary<string,string>();foreach(var item in blocked)copy[item.Key.ToString()]=item.Value;return copy;}}
    public static void Check(ResourceOptions options){lock(gate){foreach(PhoneResource key in Enum.GetValues(typeof(PhoneResource))){bool used=key==PhoneResource.Speaker?options.Speaker:key==PhoneResource.Camera?options.Camera:options.Microphone;string error;if(used&&blocked.TryGetValue(key,out error))throw new InvalidOperationException(error+"。请从托盘退出并重新启动 PocketLink，再开启此资源。");}}}
}
