using System;
using System.Runtime.InteropServices;
using System.Text;

namespace LagWatch
{
    /// <summary>
    /// 관리자 권한·드라이버 없이 쓰는 윈도우 호출만 모음.
    /// 프로세스 목록과 스레드 상태는 작업 관리자와 같은 NtQuerySystemInformation 으로 읽는다(게임 프로세스 핸들을 열지 않음).
    /// </summary>
    internal static unsafe class Native
    {
        public const int SystemProcessInformation = 5;
        public const int SystemProcessorPerformanceInformation = 8;
        public const int SystemPagefileInformation = 18;
        public const int SystemProcessIdInformation = 88;
        public const uint STATUS_INFO_LENGTH_MISMATCH = 0xC0000004;

        [DllImport("ntdll.dll")]
        public static extern uint NtQuerySystemInformation(int cls, void* buf, int len, out int ret);

        [DllImport("ntdll.dll")]
        public static extern uint NtQueryInformationProcess(IntPtr h, int cls, void* buf, int len, out int ret);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool CloseHandle(IntPtr h);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern int QueryDosDeviceW(string dev, StringBuilder sb, int max);
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool TerminateProcess(IntPtr h, uint code);
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool GetProcessTimes(IntPtr h, out long create, out long exit, out long kernel, out long user);

        public const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
        public const uint PROCESS_TERMINATE = 0x0001;
        public const uint SYNCHRONIZE = 0x00100000;

        [DllImport("kernel32.dll")]
        public static extern uint WaitForSingleObject(IntPtr h, uint ms);

        [StructLayout(LayoutKind.Sequential)]
        public struct PERFORMANCE_INFORMATION
        {
            public uint cb;
            public UIntPtr CommitTotal, CommitLimit, CommitPeak, PhysicalTotal, PhysicalAvailable, SystemCache,
                KernelTotal, KernelPaged, KernelNonpaged, PageSize;
            public uint HandleCount, ProcessCount, ThreadCount;
        }
        [DllImport("psapi.dll", SetLastError = true)]
        public static extern bool GetPerformanceInfo(out PERFORMANCE_INFORMATION pi, uint cb);

        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
        [DllImport("user32.dll")]
        public static extern bool DestroyIcon(IntPtr h);
        [DllImport("user32.dll")]
        public static extern bool SetProcessDPIAware();

        [DllImport("shell32.dll")]
        public static extern int SHQueryUserNotificationState(out int state);

        [StructLayout(LayoutKind.Sequential)] struct LASTIN { public uint Cb, At; }
        [DllImport("user32.dll")] static extern bool GetLastInputInfo(ref LASTIN l);
        /// <summary>마우스·키보드를 안 만진 시간(ms)</summary>
        public static long IdleMs() { var l = new LASTIN { Cb = 8 }; return GetLastInputInfo(ref l) ? (uint)Environment.TickCount - l.At : 0; }
        [DllImport("kernel32.dll")] static extern bool HeapSetInformation(IntPtr heap, int cls, ref long info, IntPtr len);
        /// <summary>힙이 쥐고 있는 빈 조각을 윈도우에 돌려준다(8.1부터).</summary>
        public static void HeapOptimize() { long v = 1; try { HeapSetInformation(IntPtr.Zero, 3, ref v, (IntPtr)8); } catch (Exception) { } }
        [DllImport("kernel32.dll")]
        public static extern bool SetProcessWorkingSetSize(IntPtr h, IntPtr min, IntPtr max);
        [DllImport("kernel32.dll")]
        public static extern IntPtr GetCurrentProcess();

        [StructLayout(LayoutKind.Sequential)]
        public struct PROCESSOR_POWER_INFORMATION
        {
            public uint Number, MaxMhz, CurrentMhz, MhzLimit, MaxIdleState, CurrentIdleState;
        }
        [DllImport("powrprof.dll")]
        public static extern uint CallNtPowerInformation(int level, IntPtr inBuf, uint inLen, void* outBuf, uint outLen);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool GetLogicalProcessorInformation(void* buf, ref int len);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern IntPtr OpenSCManagerW(string machine, string db, uint access);
        [DllImport("advapi32.dll", SetLastError = true)]
        public static extern bool CloseServiceHandle(IntPtr h);
        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool EnumServicesStatusExW(IntPtr h, int level, uint type, uint state, byte* buf, int size,
            out int needed, out int count, ref int resume, string group);
    }
}
