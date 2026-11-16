"""Map a renamed copy suspended, dump its image, and terminate without executing it."""
import ctypes as C
from ctypes import wintypes as W
import json, os, pathlib, re, shutil, struct

ROOT = pathlib.Path(__file__).parent
K = C.WinDLL("kernel32", use_last_error=True)
N = C.WinDLL("ntdll")

class SI(C.Structure):
    _fields_ = [("cb", W.DWORD), ("reserved", W.LPWSTR), ("desktop", W.LPWSTR),
                ("title", W.LPWSTR), ("x", W.DWORD), ("y", W.DWORD),
                ("xs", W.DWORD), ("ys", W.DWORD), ("xc", W.DWORD),
                ("yc", W.DWORD), ("fill", W.DWORD), ("flags", W.DWORD),
                ("show", W.WORD), ("reserved2", W.WORD), ("reserved3", C.c_void_p),
                ("stdin", W.HANDLE), ("stdout", W.HANDLE), ("stderr", W.HANDLE)]

class PI(C.Structure):
    _fields_ = [("process", W.HANDLE), ("thread", W.HANDLE),
                ("pid", W.DWORD), ("tid", W.DWORD)]

class PBI(C.Structure):
    _fields_ = [("reserved", C.c_void_p), ("peb", C.c_void_p),
                ("reserved2", C.c_void_p * 2), ("pid", C.c_size_t),
                ("reserved3", C.c_void_p)]

K.CreateProcessW.argtypes = [W.LPCWSTR, W.LPWSTR, C.c_void_p, C.c_void_p,
    W.BOOL, W.DWORD, C.c_void_p, W.LPCWSTR, C.POINTER(SI), C.POINTER(PI)]
K.ReadProcessMemory.argtypes = [W.HANDLE, C.c_void_p, C.c_void_p, C.c_size_t, C.POINTER(C.c_size_t)]
K.TerminateProcess.argtypes = [W.HANDLE, W.UINT]
K.WaitForSingleObject.argtypes = [W.HANDLE, W.DWORD]
K.CloseHandle.argtypes = [W.HANDLE]
N.NtQueryInformationProcess.argtypes = [W.HANDLE, W.ULONG, C.c_void_p, W.ULONG, C.c_void_p]

def read(process, addr, size):
    buf = C.create_string_buffer(size)
    got = C.c_size_t()
    ok = K.ReadProcessMemory(process, C.c_void_p(addr), buf, size, C.byref(got))
    return buf.raw[:got.value], bool(ok)

def main():
    folder = ROOT / "dumps"
    folder.mkdir(exist_ok=True)
    target = folder / "MZBBot.exe"
    shutil.copyfile(r"%USERPROFILE%\AppData\Local\Mizan\app\MizanBot.exe", target)
    si, pi = SI(), PI()
    si.cb = C.sizeof(si)
    si.flags, si.show = 1, 0
    report = {"mode": "CREATE_SUSPENDED; primary thread never resumed; no login or product code executed"}
    os.environ["__COMPAT_LAYER"] = "RunAsInvoker"
    report["compatibility_layer"] = "RunAsInvoker (first attempt without this returned Win32 error 740)"
    try:
        if not K.CreateProcessW(str(target), C.create_unicode_buffer('"' + str(target) + '"'),
                None, None, False, 4 | 0x08000000, None, str(folder), C.byref(si), C.byref(pi)):
            raise C.WinError(C.get_last_error())
        report["created_pid"] = pi.pid
        pbi = PBI()
        status = N.NtQueryInformationProcess(pi.process, 0, C.byref(pbi), C.sizeof(pbi), None)
        if status != 0:
            raise RuntimeError("NtQueryInformationProcess status " + hex(status & 0xffffffff))
        rawbase, _ = read(pi.process, pbi.peb + 0x10, 8)
        base = struct.unpack("<Q", rawbase)[0]
        header, _ = read(pi.process, base, 4096)
        pe = struct.unpack_from("<I", header, 0x3c)[0]
        size = struct.unpack_from("<I", header, pe + 24 + 56)[0]
        report.update(base=hex(base), image_size=size)
        image = bytearray(size)
        readable = 0
        for rva in range(0, size, 0x10000):
            data, ok = read(pi.process, base + rva, min(0x10000, size-rva))
            image[rva:rva+len(data)] = data
            readable += len(data)
        (folder / "MZBBot.suspended.mapped.bin").write_bytes(image)
        report["bytes_read"] = readable
        report["text_first_64_nonzero"] = sum(v != 0 for v in image[4096:4160])
        report["clr_metadata_BSJB_offsets"] = [m.start() for m in re.finditer(b"BSJB", image)][:40]
        report["conclusion"] = "Pre-entrypoint mapped image only; packer has not run. Not an unpacked executable."
    except Exception as e:
        report["error"] = repr(e)
    finally:
        if pi.process:
            report["terminated"] = bool(K.TerminateProcess(pi.process, 0))
            report["termination_wait"] = K.WaitForSingleObject(pi.process, 10000)
            K.CloseHandle(pi.process)
        if pi.thread:
            K.CloseHandle(pi.thread)
        (folder / "memory-probe.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
        print(json.dumps(report, indent=2))

if __name__ == "__main__":
    main()
