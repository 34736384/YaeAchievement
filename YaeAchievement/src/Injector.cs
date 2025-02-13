﻿using System.ComponentModel;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Memory;
using Windows.Win32.System.Threading;

namespace YaeAchievement;

public static class Injector {

    public static unsafe bool CreateProcess(string path, out HANDLE hProc, out HANDLE hThread, out uint pid) {
        Span<char> cmdLines = stackalloc char[1]; // "\0"
        var si = new STARTUPINFOW {
            cb = unchecked((uint)sizeof(STARTUPINFOW))
        };
        var dir = Path.GetDirectoryName(path)!;
        var result = Native.CreateProcess(
            path, ref cmdLines, default, default, false,
            PROCESS_CREATION_FLAGS.CREATE_SUSPENDED, default, dir, in si, out var pi
        );
        pid = pi.dwProcessId;
        hProc = pi.hProcess;
        hThread = pi.hThread;
        return result;
    }

    // todo: refactor
    public static unsafe int LoadLibraryAndInject(HANDLE hProc, ReadOnlySpan<char> libPath) {
        fixed (char* lpModelName = "kernel32.dll") {
            var hKernel = Native.GetModuleHandle(lpModelName);
            if (hKernel.IsNull) {
                return new Win32Exception().PrintMsgAndReturnErrCode("GetModuleHandle fail");
            }
            fixed(byte* lpProcName = "LoadLibraryW"u8) {
                var pLoadLibrary = Native.GetProcAddress(hKernel, (PCSTR)lpProcName);
                if (pLoadLibrary.IsNull) {
                    return new Win32Exception().PrintMsgAndReturnErrCode("GetProcAddress fail");
                }
                var libPathByteLen = (uint) libPath.Length * 2;
                var pBase = Native.VirtualAllocEx(hProc, default, libPathByteLen + 2, VIRTUAL_ALLOCATION_TYPE.MEM_RESERVE | VIRTUAL_ALLOCATION_TYPE.MEM_COMMIT, PAGE_PROTECTION_FLAGS.PAGE_READWRITE);
                if ((nint)pBase == 0) {
                    return new Win32Exception().PrintMsgAndReturnErrCode("VirtualAllocEx fail");
                }
                fixed (void* lpBuffer = libPath) {
                    if (!Native.WriteProcessMemory(hProc, pBase, lpBuffer, libPathByteLen)) {
                        return new Win32Exception().PrintMsgAndReturnErrCode("WriteProcessMemory fail");
                    }
                }
                var lpStartAddress = (delegate* unmanaged[Stdcall]<void*, uint>)pLoadLibrary.Value; //THREAD_START_ROUTINE
                var hThread = Native.CreateRemoteThread(hProc, default, 0, lpStartAddress, pBase, 0);
                if (hThread.IsNull) {
                    var e = new Win32Exception();
                    Native.VirtualFreeEx(hProc, pBase, 0, VIRTUAL_FREE_TYPE.MEM_RELEASE);
                    return e.PrintMsgAndReturnErrCode("CreateRemoteThread fail");
                }
                if (Native.WaitForSingleObject(hThread, 2000) == 0) {
                    Native.VirtualFreeEx(hProc, pBase, 0, VIRTUAL_FREE_TYPE.MEM_RELEASE);
                }
                return !Native.CloseHandle(hThread) ? new Win32Exception().PrintMsgAndReturnErrCode("CloseHandle fail") : 0;
            }
        }
    }
}
