using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace GivreLauncher.Core.Transport
{
    public static class TransportLog
    {
        public static void Line(string text)
            => Console.WriteLine($"{DateTimeOffset.Now:O} {text}");
    }
}

namespace GivreCore.Injection
{
    public static class DllInjector
    {
        private const uint ProcessCreateThread = 0x0002;
        private const uint ProcessQueryInformation = 0x0400;
        private const uint ProcessVmOperation = 0x0008;
        private const uint ProcessVmWrite = 0x0020;
        private const uint ProcessVmRead = 0x0010;
        private const uint MemCommit = 0x1000;
        private const uint MemReserve = 0x2000;
        private const uint MemRelease = 0x8000;
        private const uint PageReadWrite = 0x04;
        private const uint PageExecuteReadWrite = 0x40;
        private const uint WaitObject0 = 0;

        public static void Inject(int pid, string path)
        {
            string fullPath = Path.GetFullPath(path);
            if (!File.Exists(fullPath))
                throw new FileNotFoundException("DLL d'injection introuvable", fullPath);

            byte[] bytes = Encoding.Unicode.GetBytes(fullPath + "\0");
            IntPtr process = OpenProcess(
                ProcessCreateThread | ProcessQueryInformation | ProcessVmOperation | ProcessVmWrite | ProcessVmRead,
                false,
                pid);
            if (process == IntPtr.Zero)
                throw new Win32Exception(Marshal.GetLastWin32Error(), $"OpenProcess({pid})");

            IntPtr remote = IntPtr.Zero;
            IntPtr thread = IntPtr.Zero;
            try
            {
                remote = VirtualAllocEx(process, IntPtr.Zero, (nuint)bytes.Length, MemCommit | MemReserve, PageReadWrite);
                if (remote == IntPtr.Zero)
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "VirtualAllocEx");

                if (!WriteProcessMemory(process, remote, bytes, (nuint)bytes.Length, out nuint written) || written != (nuint)bytes.Length)
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "WriteProcessMemory");

                IntPtr kernel32 = GetModuleHandleW("kernel32.dll");
                if (kernel32 == IntPtr.Zero)
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "GetModuleHandleW(kernel32)");
                IntPtr loadLibraryW = GetProcAddress(kernel32, "LoadLibraryW");
                if (loadLibraryW == IntPtr.Zero)
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "GetProcAddress(LoadLibraryW)");

                thread = CreateRemoteThread(process, IntPtr.Zero, 0, loadLibraryW, remote, 0, out _);
                if (thread == IntPtr.Zero)
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "CreateRemoteThread");

                uint wait = WaitForSingleObject(thread, 10_000);
                if (wait != WaitObject0)
                    throw new TimeoutException($"LoadLibraryW distant non terminé (wait=0x{wait:X8})");
            }
            finally
            {
                if (thread != IntPtr.Zero) CloseHandle(thread);
                if (remote != IntPtr.Zero) VirtualFreeEx(process, remote, 0, MemRelease);
                CloseHandle(process);
            }
        }

        public static void SetEnvironmentVariable(int pid, string name, string value)
        {
            if (!Environment.Is64BitProcess)
                throw new PlatformNotSupportedException("Le harness d'injection doit être x64.");
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Nom de variable vide.", nameof(name));

            byte[] nameBytes = Encoding.Unicode.GetBytes(name + "\0");
            byte[] valueBytes = Encoding.Unicode.GetBytes(value + "\0");
            IntPtr process = OpenProcess(
                ProcessCreateThread | ProcessQueryInformation | ProcessVmOperation | ProcessVmWrite | ProcessVmRead,
                false,
                pid);
            if (process == IntPtr.Zero)
                throw new Win32Exception(Marshal.GetLastWin32Error(), $"OpenProcess({pid})");

            IntPtr remoteName = IntPtr.Zero;
            IntPtr remoteValue = IntPtr.Zero;
            IntPtr remoteStub = IntPtr.Zero;
            IntPtr thread = IntPtr.Zero;
            try
            {
                remoteName = AllocAndWrite(process, nameBytes, PageReadWrite);
                remoteValue = AllocAndWrite(process, valueBytes, PageReadWrite);

                IntPtr kernel32 = GetModuleHandleW("kernel32.dll");
                if (kernel32 == IntPtr.Zero)
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "GetModuleHandleW(kernel32)");
                IntPtr setEnvironmentVariableW = GetProcAddress(kernel32, "SetEnvironmentVariableW");
                if (setEnvironmentVariableW == IntPtr.Zero)
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "GetProcAddress(SetEnvironmentVariableW)");

                // Windows x64 ABI:
                //   sub rsp, 28h
                //   mov rcx, <name>
                //   mov rdx, <value>
                //   mov rax, <SetEnvironmentVariableW>
                //   call rax
                //   add rsp, 28h
                //   ret
                // RAX/EAX conserve le BOOL de SetEnvironmentVariableW, donc GetExitCodeThread valide l'appel.
                var stub = new List<byte>(48)
                {
                    0x48, 0x83, 0xEC, 0x28,
                    0x48, 0xB9
                };
                stub.AddRange(BitConverter.GetBytes(remoteName.ToInt64()));
                stub.AddRange(new byte[] { 0x48, 0xBA });
                stub.AddRange(BitConverter.GetBytes(remoteValue.ToInt64()));
                stub.AddRange(new byte[] { 0x48, 0xB8 });
                stub.AddRange(BitConverter.GetBytes(setEnvironmentVariableW.ToInt64()));
                stub.AddRange(new byte[] { 0xFF, 0xD0, 0x48, 0x83, 0xC4, 0x28, 0xC3 });
                remoteStub = AllocAndWrite(process, stub.ToArray(), PageExecuteReadWrite);

                thread = CreateRemoteThread(process, IntPtr.Zero, 0, remoteStub, IntPtr.Zero, 0, out _);
                if (thread == IntPtr.Zero)
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "CreateRemoteThread(SetEnvironmentVariableW)");
                uint wait = WaitForSingleObject(thread, 10_000);
                if (wait != WaitObject0)
                    throw new TimeoutException($"SetEnvironmentVariableW distant non terminé (wait=0x{wait:X8})");
                if (!GetExitCodeThread(thread, out uint exitCode))
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "GetExitCodeThread(SetEnvironmentVariableW)");
                if (exitCode == 0)
                    throw new InvalidOperationException($"SetEnvironmentVariableW distant a échoué pour {name}.");
            }
            finally
            {
                if (thread != IntPtr.Zero) CloseHandle(thread);
                if (remoteStub != IntPtr.Zero) VirtualFreeEx(process, remoteStub, 0, MemRelease);
                if (remoteValue != IntPtr.Zero) VirtualFreeEx(process, remoteValue, 0, MemRelease);
                if (remoteName != IntPtr.Zero) VirtualFreeEx(process, remoteName, 0, MemRelease);
                CloseHandle(process);
            }
        }

        private static IntPtr AllocAndWrite(IntPtr process, byte[] bytes, uint protect)
        {
            IntPtr remote = VirtualAllocEx(process, IntPtr.Zero, (nuint)bytes.Length, MemCommit | MemReserve, protect);
            if (remote == IntPtr.Zero)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "VirtualAllocEx");
            if (!WriteProcessMemory(process, remote, bytes, (nuint)bytes.Length, out nuint written) ||
                written != (nuint)bytes.Length)
            {
                VirtualFreeEx(process, remote, 0, MemRelease);
                throw new Win32Exception(Marshal.GetLastWin32Error(), "WriteProcessMemory");
            }
            return remote;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint access, bool inheritHandle, int processId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr VirtualAllocEx(IntPtr process, IntPtr address, nuint size, uint allocationType, uint protect);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool VirtualFreeEx(IntPtr process, IntPtr address, nuint size, uint freeType);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool WriteProcessMemory(IntPtr process, IntPtr baseAddress, byte[] buffer, nuint size, out nuint bytesWritten);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr GetModuleHandleW(string moduleName);

        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true)]
        private static extern IntPtr GetProcAddress(IntPtr module, string procName);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr CreateRemoteThread(
            IntPtr process,
            IntPtr threadAttributes,
            nuint stackSize,
            IntPtr startAddress,
            IntPtr parameter,
            uint creationFlags,
            out uint threadId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetExitCodeThread(IntPtr thread, out uint exitCode);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr handle);
    }
}
