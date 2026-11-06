using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
namespace PixelVault.Companion;

// Job membership includes nested save-decoder children. SCM/host death closes the last job handle.
internal sealed class LocalWorkerLifetime:IDisposable
{
    readonly SafeFileHandle? job;
    internal LocalWorkerLifetime(Process process)
    {
        if(!OperatingSystem.IsWindows())return;
        job=CreateJobObjectW(IntPtr.Zero,null);
        try
        {
            if(job.IsInvalid)throw new IOException("A reader lifetime job could not be created.");
            var limits=new ExtendedLimits{Basic=new(){Flags=0x2000}};
            if(!SetInformationJobObject(job,9,ref limits,(uint)Marshal.SizeOf<ExtendedLimits>())||!AssignProcessToJobObject(job,process.SafeHandle))throw new IOException("The reader lifetime could not be constrained.");
        }
        catch{job.Dispose();if(!process.HasExited)process.Kill(entireProcessTree:true);throw;}
    }
    public void Dispose()=>job?.Dispose();
    [StructLayout(LayoutKind.Sequential)] struct BasicLimits{internal long ProcessTime,JobTime;internal uint Flags;internal UIntPtr MinimumWorkingSet,MaximumWorkingSet;internal uint ActiveProcesses;internal UIntPtr Affinity;internal uint Priority,Scheduling;}
    [StructLayout(LayoutKind.Sequential)] struct IoCounters{internal ulong ReadOperations,WriteOperations,OtherOperations,ReadBytes,WriteBytes,OtherBytes;}
    [StructLayout(LayoutKind.Sequential)] struct ExtendedLimits{internal BasicLimits Basic;internal IoCounters Io;internal UIntPtr ProcessMemory,JobMemory,PeakProcessMemory,PeakJobMemory;}
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern SafeFileHandle CreateJobObjectW(IntPtr security,string? name);
    [DllImport("kernel32.dll",SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)] static extern bool SetInformationJobObject(SafeFileHandle job,int type,ref ExtendedLimits information,uint size);
    [DllImport("kernel32.dll",SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)] static extern bool AssignProcessToJobObject(SafeFileHandle job,SafeProcessHandle process);
}
