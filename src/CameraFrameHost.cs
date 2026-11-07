using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

// Read-only loopback preview. Closing a browser client never stops the producer.
sealed class CameraFrameHost:IDisposable {
    readonly TcpListener listener;byte[] frame;volatile bool stopped;int rotation=270,mirror;
    public string Url{get;private set;}
    public bool HasFrame{get{return Interlocked.CompareExchange(ref frame,null,null)!=null;}}
    public CameraFrameHost(){listener=new TcpListener(IPAddress.Loopback,0);listener.Start();Url="http://127.0.0.1:"+((IPEndPoint)listener.LocalEndpoint).Port+"/";new Thread(Accept){IsBackground=true}.Start();}
    public void SetFrame(byte[] jpeg){Interlocked.Exchange(ref frame,jpeg);}
    public void Transform(int degrees,bool flipped){ValidateRotation(degrees);Interlocked.Exchange(ref rotation,degrees);Interlocked.Exchange(ref mirror,flipped?1:0);}
    internal static void ValidateRotation(int degrees){if(degrees!=0&&degrees!=90&&degrees!=180&&degrees!=270)throw new ArgumentOutOfRangeException("degrees");}
    void Accept(){while(!stopped){try{TcpClient client=listener.AcceptTcpClient();ThreadPool.QueueUserWorkItem(_=>Serve(client));}catch(SocketException){if(stopped)return;}catch(ObjectDisposedException){return;}}}
    void Serve(TcpClient client){using(client){try{
        client.ReceiveTimeout=2000;client.SendTimeout=2000;NetworkStream stream=client.GetStream();string first;
        using(var reader=new StreamReader(stream,Encoding.ASCII,false,1024,true)){first=reader.ReadLine();for(int i=0;i<40;i++){string line=reader.ReadLine();if(String.IsNullOrEmpty(line))break;}}
        if(first==null)return;string[] parts=first.Split(' ');string path=parts.Length>1?parts[1]:"/";string type;byte[] body;
        if(path.StartsWith("/frame")){type="image/jpeg";body=Interlocked.CompareExchange(ref frame,null,null);if(body==null){Respond(stream,"503 Service Unavailable","text/plain",Encoding.UTF8.GetBytes("Waiting for camera"));return;}}
        else if(path.StartsWith("/state")){type="application/json";body=Encoding.UTF8.GetBytes("{\"rotation\":"+Volatile.Read(ref rotation)+",\"mirror\":"+(Volatile.Read(ref mirror)!=0?"true":"false")+"}");}
        else{type="text/html; charset=utf-8";body=Encoding.UTF8.GetBytes(Page);}
        Respond(stream,"200 OK",type,body);
    }catch(IOException){}catch(ObjectDisposedException){}}}
    static void Respond(Stream stream,string status,string type,byte[] body){byte[] headers=Encoding.ASCII.GetBytes("HTTP/1.1 "+status+"\r\nContent-Type: "+type+"\r\nContent-Length: "+body.Length+"\r\nCache-Control: no-store\r\nConnection: close\r\n\r\n");stream.Write(headers,0,headers.Length);stream.Write(body,0,body.Length);}
    internal const string Page=@"<!doctype html><meta charset='utf-8'><title>PocketLink 摄像头预览</title><style>html,body{margin:0;background:#000;overflow:hidden}canvas{width:100vw;height:100vh;object-fit:contain}</style><canvas id='view' width='1280' height='720'></canvas><script>
const c=document.getElementById('view'),ctx=c.getContext('2d');
async function tick(){try{const responses=await Promise.all([fetch('/state'),fetch('/frame?t='+Date.now())]);if(!responses[1].ok)throw Error();const s=await responses[0].json(),b=await createImageBitmap(await responses[1].blob());ctx.save();ctx.fillStyle='#000';ctx.fillRect(0,0,1280,720);ctx.translate(640,360);if(s.mirror)ctx.scale(-1,1);ctx.rotate(s.rotation*Math.PI/180);const portrait=s.rotation%180!==0;const k=Math.min(1280/(portrait?b.height:b.width),720/(portrait?b.width:b.height));ctx.drawImage(b,-b.width*k/2,-b.height*k/2,b.width*k,b.height*k);ctx.restore();b.close();}catch(e){}setTimeout(tick,50);}tick();</script>";
    public void Dispose(){stopped=true;listener.Stop();Interlocked.Exchange(ref frame,null);}
}
