// Éprouve GivreKama.dll SANS Dofus ni compte : un processus jetable (ce même exe, mode --child) reçoit la DLL par injection, appelle
// ws2_32!connect vers ::ffff:1.2.3.4:5555 ; le test vérifie que la connexion arrive sur 127.0.0.1:5658 précédée du handshake de 26 octets.
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;

if (args.Length >= 1 && args[0] == "--child") return Child();
if (args.Length < 1) { Console.WriteLine("usage: MZP23HookHarness <GivreKama.dll>"); return 2; }
return Parent(Path.GetFullPath(args[0]), args.Length > 1 ? int.Parse(args[1]) : 5658);

static int Parent(string dll, int relayPort)
{
    if (!File.Exists(dll)) { Console.WriteLine("FAIL : DLL introuvable"); return 2; }
    TcpListener listener;
    try { listener = new TcpListener(IPAddress.Loopback, relayPort); listener.Start(); }
    catch (Exception e) { Console.WriteLine("FAIL : port 5658 occupé (un GivreClone tourne ?) : " + e.Message); return 2; }

    var psi = new ProcessStartInfo(Environment.ProcessPath!, "--child") { RedirectStandardInput = true, RedirectStandardOutput = true, UseShellExecute = false };
    if (relayPort != 5658) psi.Environment["GIVRE_RELAY_PORT"] = relayPort.ToString();
    using var child = Process.Start(psi)!;
    try
    {
        if (child.StandardOutput.ReadLine() != "READY") { Console.WriteLine("FAIL : enfant non prêt"); return 2; }
        Inject(child, dll);
        for (int i = 0; i < 50 && !child.Modules.Cast<ProcessModule>().Any(m => m.ModuleName.Equals("GivreKama.dll", StringComparison.OrdinalIgnoreCase)); i++) Thread.Sleep(100);
        bool loaded = child.Modules.Cast<ProcessModule>().Any(m => m.ModuleName.Equals("GivreKama.dll", StringComparison.OrdinalIgnoreCase));
        Console.WriteLine("DLL chargée dans l'enfant : " + loaded);
        if (!loaded) return 1;
        Thread.Sleep(800);   // la DLL pose son hook dans un thread d'initialisation
        child.StandardInput.WriteLine("GO");
        var accept = listener.AcceptTcpClientAsync();
        if (!accept.Wait(8000)) { Console.WriteLine("FAIL : aucune connexion redirigée reçue sur 127.0.0.1:" + relayPort + ""); return 1; }
        using var c = accept.Result; var s = c.GetStream(); s.ReadTimeout = 5000;
        var buf = new byte[26]; int got = 0;
        while (got < 26) { int n = s.Read(buf, got, 26 - got); if (n <= 0) break; got += n; }
        Console.WriteLine($"handshake reçu : {got} octets");
        if (got != 26) return 1;
        bool mapped = buf[10] == 0xFF && buf[11] == 0xFF && buf[0..10].All(b => b == 0);
        var ip = new IPAddress(buf[12..16]);
        int addrlen = BitConverter.ToInt32(buf, 16); ushort port = BitConverter.ToUInt16(buf, 20); uint pid = BitConverter.ToUInt32(buf, 22);
        Console.WriteLine($"adresse d'origine : ::ffff:{ip} (mappée IPv4 = {mapped}), longueur sockaddr {addrlen}, port {port}, pid {pid} (enfant {child.Id})");
        bool ok = mapped && ip.Equals(IPAddress.Parse("1.2.3.4")) && port == 5555 && pid == (uint)child.Id && addrlen == 28;
        Console.WriteLine(ok ? "PASS : redirection + handshake corrects" : "FAIL : contenu du handshake inattendu");
        return ok ? 0 : 1;
    }
    finally { try { if (!child.HasExited) child.Kill(); } catch { } listener.Stop(); }
}

static int Child()
{
    WSAStartup(0x0202, new byte[512]);
    Console.WriteLine("READY"); Console.Out.Flush();
    Console.In.ReadLine();   // attend GO
    IntPtr sock = socket(23 /*AF_INET6*/, 1 /*SOCK_STREAM*/, 6);
    int off = 0; setsockopt(sock, 41, 27 /*IPV6_V6ONLY*/, ref off, 4);
    var sa = new byte[28];
    sa[0] = 23; sa[1] = 0;                       // sin6_family
    sa[2] = (byte)(5555 >> 8); sa[3] = (byte)(5555 & 255);   // sin6_port (réseau)
    sa[8 + 10] = 0xFF; sa[8 + 11] = 0xFF; sa[8 + 12] = 1; sa[8 + 13] = 2; sa[8 + 14] = 3; sa[8 + 15] = 4;   // ::ffff:1.2.3.4
    int r = connect(sock, sa, 28);
    Thread.Sleep(1500);
    return r == 0 ? 0 : 3;
}

static void Inject(Process p, string dll)
{
    IntPtr h = OpenProcess(0x1F0FFF, false, p.Id);
    byte[] path = Encoding.Unicode.GetBytes(dll + "\0");
    IntPtr mem = VirtualAllocEx(h, IntPtr.Zero, (uint)path.Length, 0x3000, 0x04);
    WriteProcessMemory(h, mem, path, path.Length, out _);
    IntPtr load = GetProcAddress(GetModuleHandle("kernel32.dll"), "LoadLibraryW");
    IntPtr t = CreateRemoteThread(h, IntPtr.Zero, 0, load, mem, 0, out _);
    WaitForSingleObject(t, 10000);
    CloseHandle(t); CloseHandle(h);
}

[DllImport("ws2_32.dll")] static extern int WSAStartup(ushort v, byte[] data);
[DllImport("ws2_32.dll")] static extern IntPtr socket(int af, int type, int proto);
[DllImport("ws2_32.dll")] static extern int setsockopt(IntPtr s, int level, int opt, ref int val, int len);
[DllImport("ws2_32.dll")] static extern int connect(IntPtr s, byte[] name, int len);
[DllImport("kernel32.dll")] static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
[DllImport("kernel32.dll")] static extern IntPtr VirtualAllocEx(IntPtr h, IntPtr addr, uint size, uint type, uint prot);
[DllImport("kernel32.dll")] static extern bool WriteProcessMemory(IntPtr h, IntPtr addr, byte[] buf, int size, out IntPtr written);
[DllImport("kernel32.dll")] static extern IntPtr GetModuleHandle(string n);
[DllImport("kernel32.dll")] static extern IntPtr GetProcAddress(IntPtr m, string n);
[DllImport("kernel32.dll")] static extern IntPtr CreateRemoteThread(IntPtr h, IntPtr attr, uint stack, IntPtr start, IntPtr param, uint flags, out uint id);
[DllImport("kernel32.dll")] static extern uint WaitForSingleObject(IntPtr h, uint ms);
[DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
