using System.ComponentModel;
using System.Runtime.InteropServices;

namespace CK2MAP;

/// <summary>
/// Thin wrapper over the Win32 calls needed to read, allocate and write
/// memory inside another process (CK2game.exe).
/// </summary>
internal static class NativeMethods
{
    [Flags]
    public enum ProcessAccess : uint
    {
        VmOperation = 0x0008,
        VmRead      = 0x0010,
        VmWrite     = 0x0020,
        QueryInfo   = 0x0400,
        // Everything we need to scan, allocate, protect and write.
        Required    = VmOperation | VmRead | VmWrite | QueryInfo,
    }

    [Flags]
    public enum AllocationType : uint
    {
        Commit  = 0x1000,
        Reserve = 0x2000,
    }

    [Flags]
    public enum MemoryProtection : uint
    {
        ReadWrite         = 0x04,
        ExecuteRead       = 0x20,
        ExecuteReadWrite  = 0x40,
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr OpenProcess(
        ProcessAccess dwDesiredAccess,
        [MarshalAs(UnmanagedType.Bool)] bool bInheritHandle,
        int dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool ReadProcessMemory(
        IntPtr hProcess,
        IntPtr lpBaseAddress,
        byte[] lpBuffer,
        int nSize,
        out IntPtr lpNumberOfBytesRead);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool WriteProcessMemory(
        IntPtr hProcess,
        IntPtr lpBaseAddress,
        byte[] lpBuffer,
        int nSize,
        out IntPtr lpNumberOfBytesWritten);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr VirtualAllocEx(
        IntPtr hProcess,
        IntPtr lpAddress,
        int dwSize,
        AllocationType flAllocationType,
        MemoryProtection flProtect);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool VirtualProtectEx(
        IntPtr hProcess,
        IntPtr lpAddress,
        int dwSize,
        MemoryProtection flNewProtect,
        out MemoryProtection lpflOldProtect);

    /// <summary>Read exactly <paramref name="size"/> bytes or throw.</summary>
    public static byte[] ReadBytes(IntPtr hProcess, IntPtr address, int size)
    {
        var buffer = new byte[size];
        if (!ReadProcessMemory(hProcess, address, buffer, size, out var read) || read.ToInt64() != size)
            throw new Win32Exception(Marshal.GetLastWin32Error(),
                $"ReadProcessMemory failed at 0x{address.ToInt64():X} ({size} bytes).");
        return buffer;
    }

    /// <summary>Write bytes, flipping the page to writable/executable around the write.</summary>
    public static void WriteBytes(IntPtr hProcess, IntPtr address, byte[] data)
    {
        if (!VirtualProtectEx(hProcess, address, data.Length,
                MemoryProtection.ExecuteReadWrite, out var old))
            throw new Win32Exception(Marshal.GetLastWin32Error(),
                $"VirtualProtectEx (unprotect) failed at 0x{address.ToInt64():X}.");

        bool ok = WriteProcessMemory(hProcess, address, data, data.Length, out var written)
                  && written.ToInt64() == data.Length;
        int err = Marshal.GetLastWin32Error();

        // Restore the original protection regardless of the write result.
        VirtualProtectEx(hProcess, address, data.Length, old, out _);

        if (!ok)
            throw new Win32Exception(err,
                $"WriteProcessMemory failed at 0x{address.ToInt64():X} ({data.Length} bytes).");
    }
}
