using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;

sealed class ProcessOutput {public string Output,Error;public int ExitCode;}
static class ProcessRunner {
    static string Drain(TextReader reader){var text=new StringBuilder();char[] buffer=new char[1024];try{int count;while((count=reader.Read(buffer,0,buffer.Length))>0){int keep=Math.Min(count,16384-text.Length);if(keep>0)text.Append(buffer,0,keep);}}catch(IOException){}catch(ObjectDisposedException){}return text.ToString();}
    public static ProcessOutput Run(Process process,int timeout,Func<bool> canceled=null){
        var output=Task.Factory.StartNew(()=>Drain(process.StandardOutput));var error=Task.Factory.StartNew(()=>Drain(process.StandardError));var watch=Stopwatch.StartNew();bool completed=false;
        try{
            while(!process.WaitForExit(100)){
                if(canceled!=null&&canceled())throw new OperationCanceledException();
                if(watch.ElapsedMilliseconds>=timeout)throw new TimeoutException("组件响应超时（"+timeout+" ms）");
            }
            if(canceled!=null&&canceled())throw new OperationCanceledException();
            if(!Task.WaitAll(new Task[]{output,error},1500))throw new IOException("组件已退出，但输出管道未关闭。");
            if(canceled!=null&&canceled())throw new OperationCanceledException();
            completed=true;return new ProcessOutput{Output=output.Result,Error=error.Result,ExitCode=process.ExitCode};
        }finally{
            if(!completed)OwnedProcessTree.Kill(process);
            process.StandardOutput.Close();process.StandardError.Close();
        }
    }
    public static ProcessOutput Capture(string exe,string args,int timeout,Func<bool> canceled=null){if(canceled!=null&&canceled())throw new OperationCanceledException();using(var process=Process.Start(new ProcessStartInfo(exe,args){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true})){return Run(process,timeout,canceled);}}
}
