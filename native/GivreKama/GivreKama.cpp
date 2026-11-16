// GivreKama.dll - hook Detours de ws2_32!connect, propre a GivreClone.
// Redirige la connexion Dofus (IPv4 :5555 ET ::ffff:x.x.x.x:5555) vers 127.0.0.1:PROXY_PORT (proxy GivreClone).
// Un reconnect Zaap en AF_INET ne passait pas par le relais : aucune ligne, aucun auto-pick.
// et pousse un handshake de 26 octets (vraie IP + port + PID) en tete de socket.
// Port 5658 = distinct de GivreProxy (5657) -> GivreClone est independant et peut coexister.
// Le layout du handshake est identique a GivreCore.Handshake (pack=1, 26 o) : NE PAS reordonner.
//
// EMPREINTE MACHINE PAR COMPTE (2026-09-26) : en mode MITM le VRAI Dofus.exe calcule lui-meme le
// device_identifier (SHA-256 de {nom machine, utilisateur, adaptateurs/MAC}) que les serveurs mono-compte
// utilisent pour reconnaitre la "machine". Pour le faire varier PAR COMPTE sans toucher les autres facteurs,
// on hooke GetAdaptersAddresses (iphlpapi) DANS le client et on remplace chaque MAC 6 o par une MAC factice
// LOCALEMENT ADMINISTREE derivee d'une graine passee au lancement (env GIVRE_HWSEED). Graine absente/vide ->
// AUCUNE falsification (le hook MAC n'est meme pas pose) : autres process et comptes non concernes intacts.
// (Le mode Socket, lui, forge deja le device_identifier cote GivreClone via Account.DeviceSeed.)

#include <winsock2.h>
#include <ws2tcpip.h>
#include <windows.h>
#include <iphlpapi.h>
#include <bcrypt.h>
#include <detours.h>

#include <string>
#include <cstdio>
#include <cstring>
#include <cwchar>

#pragma comment(lib, "ws2_32.lib")
#pragma comment(lib, "iphlpapi.lib")
#pragma comment(lib, "bcrypt.lib")

// Marqueurs VMProtect : actifs si VMProtectSDK.h est present, sinon no-op.
#if defined(__has_include)
#  if __has_include(<VMProtectSDK.h>)
#    include <VMProtectSDK.h>
#    define GIVRE_HAS_VMP 1
#  endif
#endif
#ifndef GIVRE_HAS_VMP
#  define VMProtectBeginVirtualization(n) ((void)0)
#  define VMProtectBeginMutation(n)       ((void)0)
#  define VMProtectEnd()                  ((void)0)
#endif

// Port du relais local : 5658 par défaut ; l'application peut en donner un autre par l'environnement du client (GIVRE_RELAY_PORT, 1024..65535),
// lu une fois dans Init AVANT la pose du hook. Hors de cette plage ou illisible : on garde 5658.
static unsigned short PROXY_PORT = 5658;

// connect() original, rempli par Detours
static int (WINAPI *RealConnect)(SOCKET, const struct sockaddr*, int) = connect;

// Meme layout que le proxy C# (GivreCore.Handshake, pack=1, 26 octets) - ne pas reordonner.
#pragma pack(push, 1)
struct Handshake {
    unsigned char  Address[16];
    int            Addrlen;
    unsigned short Port;
    unsigned int   ProcessId;
};
#pragma pack(pop)

static int SendHandshake(SOCKET s, const struct sockaddr* dest, int destLen, const Handshake& h)
{
    int r = RealConnect(s, dest, destLen);
    int err = (r == SOCKET_ERROR) ? WSAGetLastError() : 0;
    // Le client Dofus 3 (Unity) utilise des sockets NON bloquants : connect() rend SOCKET_ERROR/WSAEWOULDBLOCK et un send()
    // immediat echouerait (socket pas encore connectee) -> l'en-tete ne partirait jamais. On attend donc que la socket
    // (boucle locale, quasi instantane) soit inscriptible avant d'envoyer l'en-tete, qui reste le tout premier octet du flux.
    if (r == 0 || err == WSAEWOULDBLOCK || err == WSAEINPROGRESS)
    {
        const char* p = (const char*)&h;
        int left = (int)sizeof(h);
        DWORD deadline = GetTickCount() + 3000;
        while (left > 0 && (LONG)(deadline - GetTickCount()) > 0)
        {
            int n = send(s, p, left, 0);
            if (n > 0) { p += n; left -= n; continue; }
            int e2 = WSAGetLastError();
            if (e2 != WSAEWOULDBLOCK && e2 != WSAENOTCONN && e2 != WSAEINPROGRESS) break;
            WSAPOLLFD pfd{}; pfd.fd = s; pfd.events = POLLWRNORM;
            WSAPoll(&pfd, 1, 50);
        }
    }
    if (err) WSASetLastError(err);   // le jeu attend le code d'origine (WSAEWOULDBLOCK)
    return r;
}

static int WINAPI HookConnect(SOCKET s, const struct sockaddr* name, int namelen)
{
    if (name && name->sa_family == AF_INET6 && namelen >= (int)sizeof(sockaddr_in6))
    {
        sockaddr_in6* t = (sockaddr_in6*)name;
        unsigned char* a = t->sin6_addr.s6_addr;

        bool mapped = (a[10] == 0xFF && a[11] == 0xFF);
        for (int i = 0; i < 10 && mapped; ++i) if (a[i] != 0) mapped = false;

        if (mapped && ntohs(t->sin6_port) == 5555)
        {
            VMProtectBeginMutation("givre_redirect");   // mutation ok : bloc sans return
            Handshake h;
            memcpy(h.Address, a, 16);
            h.Addrlen   = namelen;
            h.Port      = ntohs(t->sin6_port);   // host order
            h.ProcessId = GetCurrentProcessId();

            // in-place -> ::ffff:127.0.0.1:PROXY_PORT
            memset(&t->sin6_addr, 0, sizeof(t->sin6_addr));
            a[10] = 0xFF; a[11] = 0xFF;
            a[12] = 127; a[13] = 0; a[14] = 0; a[15] = 1;
            t->sin6_port = htons(PROXY_PORT);
            VMProtectEnd();
            return SendHandshake(s, name, namelen, h);
        }
    }
    if (name && name->sa_family == AF_INET && namelen >= (int)sizeof(sockaddr_in))
    {
        sockaddr_in* v4 = (sockaddr_in*)name;
        if (ntohs(v4->sin_port) == 5555)
        {
            VMProtectBeginMutation("givre_redirect_v4");   // mutation ok : bloc sans return
            Handshake h;
            memset(h.Address, 0, 16);
            h.Address[10] = 0xFF;
            h.Address[11] = 0xFF;
            memcpy(h.Address + 12, &v4->sin_addr, 4);
            h.Addrlen   = 28;
            h.Port      = ntohs(v4->sin_port);   // host order
            h.ProcessId = GetCurrentProcessId();
            v4->sin_addr.s_addr = htonl(INADDR_LOOPBACK);
            v4->sin_port = htons(PROXY_PORT);
            VMProtectEnd();
            return SendHandshake(s, name, namelen, h);
        }
    }
    return RealConnect(s, name, namelen);
}

// ====================================================================================================
// EMPREINTE MACHINE PAR COMPTE — hook GetAdaptersAddresses (falsification des MAC)
// ====================================================================================================

// Queue de MAC factice = octets 0..4 de SHA-256(graine). L'octet 0 de chaque MAC est derive de la MAC reelle
// (bits localement-administre/unicast forces) -> voir HookGetAdaptersAddresses. g_spoofMac : la graine etait
// presente et le SHA a reussi -> falsification active. Sinon (defaut) tout passe en clair, hook non pose.
static unsigned char g_macTail[5] = { 0 };
static bool          g_spoofMac   = false;
static bool          g_log        = false;   // trace OFF par defaut ; activee seulement si l'env GIVRE_KAMA_LOG est pose (debug)
static LONG          g_macLogged  = 0;   // correspondances MAC journalisees uniquement au 1er appel abouti (log borne)

// %LOCALAPPDATA%\GivreLauncher (cree si besoin) — meme convention que GivreMods (trace par PID).
static std::wstring LocalAppDir()
{
    wchar_t buf[MAX_PATH]{};
    DWORD n = GetEnvironmentVariableW(L"LOCALAPPDATA", buf, MAX_PATH);
    std::wstring dir = (n > 0 && n < MAX_PATH) ? std::wstring(buf, n) : std::wstring(L"C:\\Temp");
    dir += L"\\GivreLauncher";
    CreateDirectoryW(dir.c_str(), nullptr);
    return dir;
}

// Trace : OutputDebugStringA (visible dans DebugView) + une ligne dans %LOCALAPPDATA%\GivreLauncher\givrekama-<pid>.log.
// Contenu = donnee de diagnostic pour l'utilisateur (nom d'adaptateur + MAC) ; aucune chaine revelant l'implementation.
static void LogLine(const char* msg)
{
    if (!g_log) return;   // trace desactivee par defaut (voir g_log / GIVRE_KAMA_LOG dans Init)
    OutputDebugStringA(msg);
    wchar_t name[64];
    swprintf(name, 64, L"\\givrekama-%lu.log", static_cast<unsigned long>(GetCurrentProcessId()));
    const std::wstring path = LocalAppDir() + name;
    HANDLE f = CreateFileW(path.c_str(), FILE_APPEND_DATA, FILE_SHARE_READ | FILE_SHARE_WRITE,
                           nullptr, OPEN_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr);
    if (f == INVALID_HANDLE_VALUE) return;
    SetFilePointer(f, 0, nullptr, FILE_END);
    SYSTEMTIME t; GetLocalTime(&t);
    char line[512];
    int len = _snprintf_s(line, sizeof(line), _TRUNCATE, "[%02d:%02d:%02d] %s\r\n", t.wHour, t.wMinute, t.wSecond, msg);
    if (len > 0) { DWORD w = 0; WriteFile(f, line, static_cast<DWORD>(len), &w, nullptr); }
    CloseHandle(f);
}

// SHA-256 one-shot via BCrypt (bcrypt.dll = systeme, toujours present -> compatible /MT autonome).
static bool Sha256(const void* data, unsigned long len, unsigned char out[32])
{
    BCRYPT_ALG_HANDLE alg = nullptr;
    if (!BCRYPT_SUCCESS(BCryptOpenAlgorithmProvider(&alg, BCRYPT_SHA256_ALGORITHM, nullptr, 0)))
        return false;
    bool ok = BCRYPT_SUCCESS(BCryptHash(alg, nullptr, 0,
                                        static_cast<PUCHAR>(const_cast<void*>(data)), len, out, 32));
    BCryptCloseAlgorithmProvider(alg, 0);
    return ok;
}

// 6 octets -> "aa:bb:cc:dd:ee:ff" (18 o avec le NUL).
static void MacToStr(const unsigned char m[6], char out[18])
{
    static const char* hex = "0123456789abcdef";
    int p = 0;
    for (int i = 0; i < 6; ++i)
    {
        out[p++] = hex[m[i] >> 4];
        out[p++] = hex[m[i] & 0x0F];
        if (i < 5) out[p++] = ':';
    }
    out[p] = '\0';
}

// GetAdaptersAddresses original, rempli par Detours.
using GetAdaptersAddressesFn = ULONG (WINAPI*)(ULONG, ULONG, PVOID, PIP_ADAPTER_ADDRESSES, PULONG);
static GetAdaptersAddressesFn RealGetAdaptersAddresses = GetAdaptersAddresses;

static ULONG WINAPI HookGetAdaptersAddresses(ULONG family, ULONG flags, PVOID reserved,
                                             PIP_ADAPTER_ADDRESSES addrs, PULONG size)
{
    ULONG r = RealGetAdaptersAddresses(family, flags, reserved, addrs, size);
    // On ne reecrit QUE sur un appel abouti (le client fait souvent un 1er appel de dimensionnement qui renvoie
    // ERROR_BUFFER_OVERFLOW sans remplir la liste). g_spoofMac est redondant (hook pose seulement si actif) mais sur.
    if (r == ERROR_SUCCESS && addrs && g_spoofMac)
    {
        // La reecriture se fait a CHAQUE appel (buffer neuf a chaque fois) ; on ne JOURNALISE qu'au 1er appel abouti
        // (valeurs constantes -> pas d'interet a re-logguer, et evite une croissance sans fin du log / du flux debug).
        const bool logPass = (InterlockedExchange(&g_macLogged, 1) == 0);
        for (PIP_ADAPTER_ADDRESSES a = addrs; a; a = a->Next)
        {
            if (a->PhysicalAddressLength != 6) continue;   // Ethernet/Wi-Fi = 6 o ; on laisse le reste (loopback, tunnels) intact
            unsigned char real[6], fake[6];
            memcpy(real, a->PhysicalAddress, 6);
            // Octet 0 : on part du reel et on force « unicast + localement administre » (bit 0 = 0, bit 1 = 1),
            // exactement ce que fait une MAC randomisee legitime ; octets 1..5 = SHA-256(graine)[0..4].
            fake[0] = static_cast<unsigned char>((real[0] & 0xFC) | 0x02);
            memcpy(fake + 1, g_macTail, 5);
            memcpy(a->PhysicalAddress, fake, 6);

            if (!logPass) continue;
            char rs[18], fs[18], fname[128];
            MacToStr(real, rs);
            MacToStr(fake, fs);
            // Nom d'adaptateur : verifier le retour (0 = trop long/echec) et GARANTIR le NUL final avant %s (sinon
            // sur-lecture de pile a l'interieur de Dofus.exe sur un nom long/non-ASCII).
            int fl = a->FriendlyName ? WideCharToMultiByte(CP_UTF8, 0, a->FriendlyName, -1, fname, sizeof(fname), nullptr, nullptr) : 0;
            if (fl <= 0) { fname[0] = '?'; fname[1] = '\0'; }
            else         { fname[sizeof(fname) - 1] = '\0'; }
            char msg[320];
            _snprintf_s(msg, sizeof(msg), _TRUNCATE,
                        "GetAdaptersAddresses: %s MAC reelle %s -> falsifiee %s", fname, rs, fs);
            LogLine(msg);
        }
    }
    return r;
}

static DWORD WINAPI Init(LPVOID)
{
    {
        wchar_t pw[8]{};
        DWORD pn = GetEnvironmentVariableW(L"GIVRE_RELAY_PORT", pw, 8);
        if (pn > 0 && pn < 8)
        {
            unsigned long v = wcstoul(pw, nullptr, 10);
            if (v >= 1024 && v <= 65535) PROXY_PORT = static_cast<unsigned short>(v);
        }
    }
    // Trace de diagnostic (givrekama-<pid>.log + OutputDebugString) : OFF par defaut, activee seulement pour debug
    // en posant l'env GIVRE_KAMA_LOG au lancement.
    { wchar_t b[8]{}; g_log = GetEnvironmentVariableW(L"GIVRE_KAMA_LOG", b, 8) > 0; }
    // Graine d'empreinte machine PAR COMPTE : posee par le launcher (env GIVRE_HWSEED) uniquement quand le compte
    // lance en MITM a « Empreinte machine par compte » = ON. Absente/vide -> on ne pose PAS le hook MAC (aucune
    // falsification) : les comptes non concernes et tout autre process gardent leurs vraies MAC.
    {
        wchar_t seedW[256]{};
        DWORD n = GetEnvironmentVariableW(L"GIVRE_HWSEED", seedW, 256);
        if (n > 0 && n < 256)
        {
            char seedA[512]{};
            int m = WideCharToMultiByte(CP_UTF8, 0, seedW, static_cast<int>(n), seedA, sizeof(seedA), nullptr, nullptr);
            unsigned char digest[32];
            if (m > 0 && Sha256(seedA, static_cast<unsigned long>(m), digest))
            {
                memcpy(g_macTail, digest, 5);
                g_spoofMac = true;
            }
        }
    }

    VMProtectBeginVirtualization("givre_init");   // virtualise l'installation du hook (bloc a sortie unique)
    // Hook CRITIQUE (redirection connect) dans SA PROPRE transaction : coeur de la DLL, son sort ne doit jamais
    // dependre du hook MAC optionnel (un echec d'attache du hook MAC ne doit pas annuler la redirection connect).
    DetourTransactionBegin();
    DetourUpdateThread(GetCurrentThread());
    DetourAttach(&(PVOID&)RealConnect, HookConnect);
    const LONG rcConnect = DetourTransactionCommit();
    // Hook MAC optionnel : transaction SEPAREE. Un echec ne perd que la falsification, jamais la redirection connect.
    if (g_spoofMac)
    {
        DetourTransactionBegin();
        DetourUpdateThread(GetCurrentThread());
        DetourAttach(&(PVOID&)RealGetAdaptersAddresses, HookGetAdaptersAddresses);
        if (DetourTransactionCommit() != NO_ERROR) g_spoofMac = false;   // pas pose -> ne pas le detacher au DETACH
    }
    VMProtectEnd();

    if (rcConnect != NO_ERROR)
        LogLine("GivreKama: ATTENTION redirection connect NON posee.");
    if (g_spoofMac)
        LogLine("GivreKama: empreinte machine ACTIVE (hook GetAdaptersAddresses pose ; MAC falsifiees par compte).");
    return 0;
}

BOOL WINAPI DllMain(HINSTANCE hinst, DWORD reason, LPVOID)
{
    if (reason == DLL_PROCESS_ATTACH)
    {
        DisableThreadLibraryCalls(hinst);
        CreateThread(nullptr, 0, Init, nullptr, 0, nullptr);
    }
    else if (reason == DLL_PROCESS_DETACH)
    {
        DetourTransactionBegin();
        DetourUpdateThread(GetCurrentThread());
        DetourDetach(&(PVOID&)RealConnect, HookConnect);
        if (g_spoofMac) DetourDetach(&(PVOID&)RealGetAdaptersAddresses, HookGetAdaptersAddresses);
        DetourTransactionCommit();
    }
    return TRUE;
}
