using System;
using System.Runtime.InteropServices;

namespace FH6Mod.Cheats.RuntimeHook;

/// <summary>
/// Win32 P/Invoke wrappers used by the runtime hook engine.
/// Ported 1:1 from Autoshow Unlocker v1.3.0 — same constants, same call patterns.
/// </summary>
internal static class Native
{
    public const uint PROCESS_ALL_ACCESS         = 0x001F0FFF;
    public const uint MEM_COMMIT                 = 0x00001000;
    public const uint MEM_RESERVE                = 0x00002000;
    public const uint MEM_RELEASE                = 0x00008000;
    // Region types returned by VirtualQueryEx MemoryBasicInformation64.Type
    public const uint MEM_PRIVATE                = 0x00020000;
    public const uint MEM_MAPPED                 = 0x00040000;
    public const uint MEM_IMAGE                  = 0x01000000;
    public const uint PAGE_EXECUTE_READWRITE     = 0x40;
    public const uint PAGE_EXECUTE_READ          = 0x20;
    public const uint PAGE_EXECUTE_WRITECOPY     = 0x80;
    public const uint PAGE_READWRITE             = 0x04;
    public const uint PAGE_WRITECOPY             = 0x08;
    public const uint PAGE_READONLY              = 0x02;
    public const uint PAGE_NOACCESS              = 0x01;
    public const uint PAGE_GUARD                 = 0x100;

    [StructLayout(LayoutKind.Sequential)]
    public struct MemoryBasicInformation64
    {
        public ulong  BaseAddress;
        public ulong  AllocationBase;
        public uint   AllocationProtect;
        public uint   __alignment1;
        public ulong  RegionSize;
        public uint   State;
        public uint   Protect;
        public uint   Type;
        public uint   __alignment2;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, uint dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool ReadProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress,
        [Out] byte[] lpBuffer, UIntPtr dwSize, out UIntPtr lpNumberOfBytesRead);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool WriteProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress,
        byte[] lpBuffer, UIntPtr nSize, out UIntPtr lpNumberOfBytesWritten);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr VirtualAllocEx(IntPtr hProcess, IntPtr lpAddress,
        UIntPtr dwSize, uint flAllocationType, uint flProtect);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool VirtualFreeEx(IntPtr hProcess, IntPtr lpAddress,
        UIntPtr dwSize, uint dwFreeType);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool VirtualProtectEx(IntPtr hProcess, IntPtr lpAddress,
        UIntPtr dwSize, uint flNewProtect, out uint lpflOldProtect);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern UIntPtr VirtualQueryEx(IntPtr hProcess, UIntPtr lpAddress,
        out MemoryBasicInformation64 lpBuffer, UIntPtr dwLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr CreateRemoteThread(IntPtr hProcess, IntPtr lpThreadAttributes,
        uint dwStackSize, IntPtr lpStartAddress, IntPtr lpParameter,
        uint dwCreationFlags, out uint lpThreadId);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);

    // ===== EnumProcessModulesEx — works on UWP processes where .NET MainModule fails =====

    public const uint LIST_MODULES_ALL = 0x03;

    [DllImport("psapi.dll", SetLastError = true)]
    public static extern bool EnumProcessModulesEx(IntPtr hProcess, [Out] IntPtr[] lphModule,
        uint cb, out uint lpcbNeeded, uint dwFilterFlag);

    [StructLayout(LayoutKind.Sequential)]
    public struct MODULEINFO
    {
        public IntPtr lpBaseOfDll;
        public uint SizeOfImage;
        public IntPtr EntryPoint;
    }

    [DllImport("psapi.dll", SetLastError = true)]
    public static extern bool GetModuleInformation(IntPtr hProcess, IntPtr hModule,
        out MODULEINFO lpmodinfo, uint cb);

    [DllImport("psapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern uint GetModuleBaseName(IntPtr hProcess, IntPtr hModule,
        [Out] System.Text.StringBuilder lpBaseName, uint nSize);

    [DllImport("psapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern uint GetModuleFileNameEx(IntPtr hProcess, IntPtr hModule,
        [Out] System.Text.StringBuilder lpFilename, uint nSize);

    /// <summary>
    /// Find the main module of a process via Win32 (works on UWP where Process.MainModule
    /// throws AccessDenied). Returns (baseAddr, sizeOfImage, filePath).
    /// </summary>
    public static (IntPtr Base, uint Size, string Path)? FindMainModule(IntPtr hProcess, string expectedNameWithoutExt)
    {
        var modules = new IntPtr[1024];
        var cb = (uint)(modules.Length * IntPtr.Size);
        if (!EnumProcessModulesEx(hProcess, modules, cb, out var needed, LIST_MODULES_ALL))
            return null;

        var count = (int)(needed / IntPtr.Size);
        var sb = new System.Text.StringBuilder(1024);
        for (var i = 0; i < count && i < modules.Length; i++)
        {
            sb.Clear();
            if (GetModuleBaseName(hProcess, modules[i], sb, (uint)sb.Capacity) == 0) continue;
            var name = sb.ToString();
            // Match "ForzaHorizon6" or "ForzaHorizon6.exe" — case-insensitive
            if (!name.StartsWith(expectedNameWithoutExt, System.StringComparison.OrdinalIgnoreCase)) continue;
            if (!GetModuleInformation(hProcess, modules[i], out var info, (uint)System.Runtime.InteropServices.Marshal.SizeOf<MODULEINFO>()))
                continue;
            sb.Clear();
            GetModuleFileNameEx(hProcess, modules[i], sb, (uint)sb.Capacity);
            return (info.lpBaseOfDll, info.SizeOfImage, sb.ToString());
        }
        return null;
    }

    [DllImport("advapi32.dll", SetLastError = true)]
    public static extern bool OpenProcessToken(IntPtr ProcessHandle, uint DesiredAccess, out IntPtr TokenHandle);

    [DllImport("advapi32.dll", SetLastError = true)]
    public static extern bool LookupPrivilegeValue(string? lpSystemName, string lpName, out LUID lpLuid);

    [DllImport("advapi32.dll", SetLastError = true)]
    public static extern bool AdjustTokenPrivileges(IntPtr TokenHandle, bool DisableAllPrivileges,
        ref TOKEN_PRIVILEGES NewState, uint BufferLength, IntPtr PreviousState, IntPtr ReturnLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr GetCurrentProcess();

    [StructLayout(LayoutKind.Sequential)]
    public struct LUID { public uint LowPart; public int HighPart; }

    [StructLayout(LayoutKind.Sequential)]
    public struct TOKEN_PRIVILEGES { public uint PrivilegeCount; public LUID Luid; public uint Attributes; }

    public const uint SE_PRIVILEGE_ENABLED = 0x00000002;
    public const uint TOKEN_ADJUST_PRIVILEGES = 0x0020;
    public const uint TOKEN_QUERY = 0x0008;
    public const string SE_DEBUG_NAME = "SeDebugPrivilege";

    public static bool IsReadable(uint protect)
    {
        if ((protect & PAGE_NOACCESS) != 0 || (protect & PAGE_GUARD) != 0) return false;
        return (protect & (PAGE_EXECUTE_READ | PAGE_EXECUTE_READWRITE | PAGE_READWRITE)) != 0;
    }

    public static bool IsExecutable(uint protect)
    {
        if ((protect & PAGE_NOACCESS) != 0 || (protect & PAGE_GUARD) != 0) return false;
        return (protect & (PAGE_EXECUTE_READ | PAGE_EXECUTE_READWRITE)) != 0;
    }

    /// <summary>
    /// True for pages we can write to (heap, private committed data). Used by the
    /// memory scanner to restrict the value search to the profile object's home.
    /// </summary>
    public static bool IsWritable(uint protect)
    {
        if ((protect & PAGE_NOACCESS) != 0 || (protect & PAGE_GUARD) != 0) return false;
        return (protect & (PAGE_READWRITE | PAGE_WRITECOPY | PAGE_EXECUTE_READWRITE | PAGE_EXECUTE_WRITECOPY)) != 0;
    }

    // ===== Thread context (for hijacking game threads to call TLS-dependent functions) =====

    public const uint CONTEXT_ALL = 0x10001B;
    public const int CONTEXT_X64_SIZE = 1232;
    public const int OFF_CTX_FLAGS = 0x30;
    public const int OFF_CTX_RSP = 0x98;
    public const int OFF_CTX_RIP = 0xF8;

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool GetThreadContext(IntPtr hThread, IntPtr lpContext);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool SetThreadContext(IntPtr hThread, IntPtr lpContext);

    public const uint THREAD_GET_CONTEXT = 0x0008;
    public const uint THREAD_SET_CONTEXT = 0x0010;
    public const uint THREAD_QUERY_INFORMATION = 0x0040;

    public static void EnableDebugPrivilege()
    {
        if (!OpenProcessToken(GetCurrentProcess(), TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY, out var token))
            return;
        try
        {
            if (!LookupPrivilegeValue(null, SE_DEBUG_NAME, out var luid)) return;
            var tp = new TOKEN_PRIVILEGES
            {
                PrivilegeCount = 1,
                Luid = luid,
                Attributes = SE_PRIVILEGE_ENABLED,
            };
            AdjustTokenPrivileges(token, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero);
        }
        finally { CloseHandle(token); }
    }
}
