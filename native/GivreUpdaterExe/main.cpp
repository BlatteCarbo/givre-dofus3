// GivreUpdater (natif Win32) — amorceur (install) + swappeur (update), un seul exe, VMProtect-pack-able.
// Split de securite (valide) : ce setup natif n'embarque AUCUNE crypto ecrite a la main.
//   - INSTALL : GET /version (HTTPS) -> download -> verif SHA-256 (built-in BCrypt) -> extract (tar.exe) -> install.
//   - UPDATE  : GivreClone.App (C#, Ed25519 eprouve) verifie + telecharge + met en staging, puis lance ce swappeur
//               avec --zip <deja verifie> --sha256 <hex> ; on re-verifie le SHA-256 et on swap a froid.
// Built-ins Windows only : WinHTTP, Crypt32 (base64), BCrypt (SHA-256), tar.exe (unzip), IShellLink (raccourcis).
// WIN32_LEAN_AND_MEAN / NOMINMAX / UNICODE sont definis par le vcxproj (-D) pour tous les .cpp.
#include <windows.h>
#include <windowsx.h>
#include <winhttp.h>
#include <bcrypt.h>
#include <wincrypt.h>
#include <shlobj.h>
#include <shlwapi.h>
#include <shellapi.h>
#include <shobjidl.h>
#include <string>
#include <vector>
#include <functional>
#include <cstdint>
#include <cstdlib>
#include <cctype>
#include "resource.h"

#pragma comment(lib, "winhttp.lib")
#pragma comment(lib, "bcrypt.lib")
#pragma comment(lib, "crypt32.lib")
#pragma comment(lib, "shell32.lib")
#pragma comment(lib, "ole32.lib")
#pragma comment(lib, "shlwapi.lib")
#pragma comment(lib, "user32.lib")
#pragma comment(lib, "gdi32.lib")

#ifndef STATUS_SUCCESS
#define STATUS_SUCCESS ((NTSTATUS)0x00000000L)
#endif

// ============================ helpers chaines / logs ============================
static std::wstring Utf8ToW(const std::string& s)
{
    if (s.empty()) return L"";
    int n = MultiByteToWideChar(CP_UTF8, 0, s.data(), (int)s.size(), nullptr, 0);
    std::wstring w(n, L'\0');
    MultiByteToWideChar(CP_UTF8, 0, s.data(), (int)s.size(), w.data(), n);
    return w;
}
static std::string WToUtf8(const std::wstring& w)
{
    if (w.empty()) return "";
    int n = WideCharToMultiByte(CP_UTF8, 0, w.data(), (int)w.size(), nullptr, 0, nullptr, nullptr);
    std::string s(n, '\0');
    WideCharToMultiByte(CP_UTF8, 0, w.data(), (int)w.size(), s.data(), n, nullptr, nullptr);
    return s;
}

static std::wstring g_installRoot;   // %LOCALAPPDATA%\Givre

static std::wstring LocalAppData()
{
    PWSTR p = nullptr;
    std::wstring out;
    if (SUCCEEDED(SHGetKnownFolderPath(FOLDERID_LocalAppData, 0, nullptr, &p))) { out = p; CoTaskMemFree(p); }
    return out;
}
static void Log(const std::wstring& msg)
{
    if (g_installRoot.empty()) return;
    SHCreateDirectoryExW(nullptr, g_installRoot.c_str(), nullptr);
    std::wstring path = g_installRoot + L"\\updater.log";
    HANDLE h = CreateFileW(path.c_str(), FILE_APPEND_DATA, FILE_SHARE_READ | FILE_SHARE_WRITE, nullptr,
                           OPEN_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr);
    if (h == INVALID_HANDLE_VALUE) return;
    std::string line = WToUtf8(msg) + "\r\n";
    DWORD w = 0; WriteFile(h, line.data(), (DWORD)line.size(), &w, nullptr);
    CloseHandle(h);
}

// ============================ secrets (BaseUrl, XOR — MEME schema que C#) ============================
static const unsigned char K16[16] =
    { 0x3A,0xC8,0x91,0x4F,0xD2,0x0B,0x77,0xA6,0x1E,0x5C,0xE9,0x84,0x30,0xBD,0x62,0xF1 };

static std::string XorDecode(const unsigned char* e, size_t n)
{
    std::string p(n, '\0');
    for (size_t i = 0; i < n; i++)
        p[i] = (char)(e[i] ^ K16[i % 16] ^ ((i * 27 + 165) & 0xFF));
    return p;
}
// BaseUrl : env GIVRE_BASE_URL (dev/test) > baké PROD. Baké = (retiré de cette copie).
static std::string BaseUrl()
{
    wchar_t buf[1024];
    DWORD n = GetEnvironmentVariableW(L"GIVRE_BASE_URL", buf, 1024);
    if (n > 0 && n < 1024) return WToUtf8(std::wstring(buf, n));
    static const unsigned char BaseUrlEnc[] = { 0 };
    return XorDecode(BaseUrlEnc, sizeof(BaseUrlEnc));
}

// ============================ base64 (Crypt32) ============================
static std::vector<uint8_t> B64Decode(const std::string& s)
{
    DWORD len = 0;
    if (!CryptStringToBinaryA(s.c_str(), (DWORD)s.size(), CRYPT_STRING_BASE64, nullptr, &len, nullptr, nullptr))
        return {};
    std::vector<uint8_t> out(len);
    if (!CryptStringToBinaryA(s.c_str(), (DWORD)s.size(), CRYPT_STRING_BASE64, out.data(), &len, nullptr, nullptr))
        return {};
    out.resize(len);
    return out;
}

// ============================ SHA-256 fichier -> hex (BCrypt) ============================
static bool Sha256File(const std::wstring& path, std::string& hexOut)
{
    bool ok = false;
    BCRYPT_ALG_HANDLE alg = nullptr; BCRYPT_HASH_HANDLE hash = nullptr;
    DWORD objLen = 0, cb = 0, hashLen = 0;
    std::vector<uint8_t> obj, digest;
    HANDLE f = INVALID_HANDLE_VALUE;
    std::vector<uint8_t> buf(1 << 16);

    if (BCryptOpenAlgorithmProvider(&alg, BCRYPT_SHA256_ALGORITHM, nullptr, 0) != STATUS_SUCCESS) goto done;
    if (BCryptGetProperty(alg, BCRYPT_OBJECT_LENGTH, (PUCHAR)&objLen, sizeof(objLen), &cb, 0) != STATUS_SUCCESS) goto done;
    if (BCryptGetProperty(alg, BCRYPT_HASH_LENGTH, (PUCHAR)&hashLen, sizeof(hashLen), &cb, 0) != STATUS_SUCCESS) goto done;
    obj.resize(objLen); digest.resize(hashLen);
    if (BCryptCreateHash(alg, &hash, obj.data(), objLen, nullptr, 0, 0) != STATUS_SUCCESS) goto done;

    f = CreateFileW(path.c_str(), GENERIC_READ, FILE_SHARE_READ, nullptr, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, nullptr);
    if (f == INVALID_HANDLE_VALUE) goto done;
    for (;;)
    {
        DWORD r = 0;
        if (!ReadFile(f, buf.data(), (DWORD)buf.size(), &r, nullptr)) goto done;
        if (r == 0) break;
        if (BCryptHashData(hash, buf.data(), r, 0) != STATUS_SUCCESS) goto done;
    }
    if (BCryptFinishHash(hash, digest.data(), hashLen, 0) != STATUS_SUCCESS) goto done;
    {
        static const char* hx = "0123456789abcdef";
        hexOut.clear(); hexOut.reserve(hashLen * 2);
        for (DWORD i = 0; i < hashLen; i++) { hexOut.push_back(hx[digest[i] >> 4]); hexOut.push_back(hx[digest[i] & 0xF]); }
        ok = true;
    }
done:
    if (f != INVALID_HANDLE_VALUE) CloseHandle(f);
    if (hash) BCryptDestroyHash(hash);
    if (alg) BCryptCloseAlgorithmProvider(alg, 0);
    return ok;
}
static bool EqualsCI(const std::string& a, const std::string& b)
{
    if (a.size() != b.size()) return false;
    for (size_t i = 0; i < a.size(); i++)
        if (tolower((unsigned char)a[i]) != tolower((unsigned char)b[i])) return false;
    return true;
}

// ============================ JSON minimal (objet plat, cles uniques) ============================
static size_t JsonValueStart(const std::string& j, const std::string& key)
{
    std::string pat = "\"" + key + "\"";
    size_t p = j.find(pat);
    if (p == std::string::npos) return std::string::npos;
    p += pat.size();
    while (p < j.size() && (j[p] == ' ' || j[p] == '\t' || j[p] == '\r' || j[p] == '\n')) p++;
    if (p >= j.size() || j[p] != ':') return std::string::npos;
    p++;
    while (p < j.size() && (j[p] == ' ' || j[p] == '\t' || j[p] == '\r' || j[p] == '\n')) p++;
    return p;
}
static bool JsonString(const std::string& j, const std::string& key, std::string& out)
{
    size_t p = JsonValueStart(j, key);
    if (p == std::string::npos || j[p] != '"') return false;
    p++;
    std::string s;
    while (p < j.size())
    {
        char c = j[p++];
        if (c == '"') { out = s; return true; }
        if (c == '\\' && p < j.size())
        {
            char e = j[p++];
            switch (e)
            {
                case '"': s.push_back('"'); break;
                case '\\': s.push_back('\\'); break;
                case '/': s.push_back('/'); break;
                case 'n': s.push_back('\n'); break;
                case 't': s.push_back('\t'); break;
                case 'r': s.push_back('\r'); break;
                case 'b': s.push_back('\b'); break;
                case 'f': s.push_back('\f'); break;
                case 'u':
                {
                    if (p + 4 <= j.size())
                    {
                        int cp = (int)strtol(j.substr(p, 4).c_str(), nullptr, 16); p += 4;
                        wchar_t wc = (wchar_t)cp; s += WToUtf8(std::wstring(1, wc));
                    }
                    break;
                }
                default: s.push_back(e); break;
            }
        }
        else s.push_back(c);
    }
    return false;
}
static bool JsonBool(const std::string& j, const std::string& key, bool& out)
{
    size_t p = JsonValueStart(j, key);
    if (p == std::string::npos) return false;
    if (j.compare(p, 4, "true") == 0) { out = true; return true; }
    if (j.compare(p, 5, "false") == 0) { out = false; return true; }
    return false;
}

// ============================ WinHTTP (GET memoire / fichier + progression) ============================
static bool HttpFetch(const std::string& url, std::string* toMem, HANDLE toFile,
                      const std::function<void(double)>& progress, std::wstring& err)
{
    std::wstring wurl = Utf8ToW(url);
    URL_COMPONENTS uc; ZeroMemory(&uc, sizeof(uc)); uc.dwStructSize = sizeof(uc);
    wchar_t host[256] = {0}, path[2048] = {0};
    uc.lpszHostName = host; uc.dwHostNameLength = 255;
    uc.lpszUrlPath = path; uc.dwUrlPathLength = 2047;
    if (!WinHttpCrackUrl(wurl.c_str(), 0, 0, &uc)) { err = L"URL invalide"; return false; }
    bool https = (uc.nScheme == INTERNET_SCHEME_HTTPS);

    // User-Agent "GivreClient/*" (même préfixe que l'app managée) -> une seule règle d'allowlist WAF Cloudflare.
    HINTERNET hs = WinHttpOpen(L"GivreClient/1.0", WINHTTP_ACCESS_TYPE_AUTOMATIC_PROXY,
                               WINHTTP_NO_PROXY_NAME, WINHTTP_NO_PROXY_BYPASS, 0);
    if (!hs) { err = L"WinHttpOpen"; return false; }
    // Timeouts (ms) resolve/connect/send/receive : receive large pour un gros ZIP derrière un edge Cloudflare lent.
    WinHttpSetTimeouts(hs, 60000, 60000, 60000, 120000);
    HINTERNET hc = WinHttpConnect(hs, host, uc.nPort, 0);
    HINTERNET hr = nullptr;
    bool ok = false; DWORD statusCode = 0;
    if (!hc) { err = L"WinHttpConnect"; goto cleanup; }
    hr = WinHttpOpenRequest(hc, L"GET", path, nullptr, WINHTTP_NO_REFERER,
                            WINHTTP_DEFAULT_ACCEPT_TYPES, https ? WINHTTP_FLAG_SECURE : 0);
    if (!hr) { err = L"WinHttpOpenRequest"; goto cleanup; }
    if (!WinHttpSendRequest(hr, WINHTTP_NO_ADDITIONAL_HEADERS, 0, WINHTTP_NO_REQUEST_DATA, 0, 0, 0)) { err = L"envoi requete"; goto cleanup; }
    if (!WinHttpReceiveResponse(hr, nullptr)) { err = L"pas de reponse"; goto cleanup; }
    {
        DWORD sz = sizeof(statusCode);
        WinHttpQueryHeaders(hr, WINHTTP_QUERY_STATUS_CODE | WINHTTP_QUERY_FLAG_NUMBER,
                            WINHTTP_HEADER_NAME_BY_INDEX, &statusCode, &sz, WINHTTP_NO_HEADER_INDEX);
        if (statusCode < 200 || statusCode >= 300) { err = L"HTTP " + std::to_wstring(statusCode); goto cleanup; }
    }
    {
        long long total = -1;
        DWORD clen = 0, sz2 = sizeof(clen);
        if (WinHttpQueryHeaders(hr, WINHTTP_QUERY_CONTENT_LENGTH | WINHTTP_QUERY_FLAG_NUMBER,
                                WINHTTP_HEADER_NAME_BY_INDEX, &clen, &sz2, WINHTTP_NO_HEADER_INDEX))
            total = clen;
        long long read = 0;
        std::vector<char> buf(1 << 16);
        for (;;)
        {
            DWORD avail = 0;
            if (!WinHttpQueryDataAvailable(hr, &avail)) { err = L"lecture"; goto cleanup; }
            if (avail == 0) break;
            DWORD toRead = avail < buf.size() ? avail : (DWORD)buf.size();
            DWORD got = 0;
            if (!WinHttpReadData(hr, buf.data(), toRead, &got)) { err = L"lecture"; goto cleanup; }
            if (got == 0) break;
            if (toMem) toMem->append(buf.data(), got);
            if (toFile != INVALID_HANDLE_VALUE && toFile != nullptr)
            {
                DWORD w = 0;
                if (!WriteFile(toFile, buf.data(), got, &w, nullptr)) { err = L"ecriture disque"; goto cleanup; }
            }
            read += got;
            if (progress && total > 0) progress((double)read / (double)total);
        }
        if (progress && total <= 0) progress(1.0);
        ok = true;
    }
cleanup:
    if (hr) WinHttpCloseHandle(hr);
    if (hc) WinHttpCloseHandle(hc);
    if (hs) WinHttpCloseHandle(hs);
    return ok;
}

// ============================ fichiers / install ============================
static void EnsureDir(const std::wstring& p) { SHCreateDirectoryExW(nullptr, p.c_str(), nullptr); }

static void RemovePath(const std::wstring& path)
{
    // SHFileOperation FO_DELETE : chemin double-null-termine.
    std::wstring from = path; from.push_back(L'\0'); from.push_back(L'\0');
    SHFILEOPSTRUCTW op; ZeroMemory(&op, sizeof(op));
    op.wFunc = FO_DELETE; op.pFrom = from.c_str();
    op.fFlags = FOF_NO_UI | FOF_NOCONFIRMATION | FOF_NOERRORUI | FOF_SILENT;
    SHFileOperationW(&op);
}
static bool MoveDirRetry(const std::wstring& src, const std::wstring& dst, int tries = 8)
{
    for (int i = 0; i < tries; i++)
    {
        if (MoveFileExW(src.c_str(), dst.c_str(), MOVEFILE_COPY_ALLOWED)) return true;
        Sleep(250);
    }
    return false;
}
static bool WaitForPid(DWORD pid, DWORD timeoutMs)
{
    if (pid == 0) return true;
    HANDLE h = OpenProcess(SYNCHRONIZE, FALSE, pid);
    if (!h) return true;   // deja termine
    DWORD r = WaitForSingleObject(h, timeoutMs);
    if (r == WAIT_TIMEOUT) { HANDLE k = OpenProcess(PROCESS_TERMINATE, FALSE, pid); if (k) { TerminateProcess(k, 0); CloseHandle(k); } }
    CloseHandle(h);
    return true;
}
// Extraction ZIP via tar.exe (bsdtar, present Win10 1803+ ; extraction securisee vs ../).
static bool TarExtract(const std::wstring& zip, const std::wstring& target, std::wstring& err)
{
    EnsureDir(target);
    wchar_t sysdir[MAX_PATH]; GetSystemDirectoryW(sysdir, MAX_PATH);
    std::wstring tar = std::wstring(sysdir) + L"\\tar.exe";
    std::wstring cmd = L"\"" + tar + L"\" -xf \"" + zip + L"\" -C \"" + target + L"\"";
    std::vector<wchar_t> cmdbuf(cmd.begin(), cmd.end()); cmdbuf.push_back(L'\0');
    STARTUPINFOW si; ZeroMemory(&si, sizeof(si)); si.cb = sizeof(si);
    PROCESS_INFORMATION pi; ZeroMemory(&pi, sizeof(pi));
    if (!CreateProcessW(tar.c_str(), cmdbuf.data(), nullptr, nullptr, FALSE,
                        CREATE_NO_WINDOW, nullptr, nullptr, &si, &pi)) { err = L"tar.exe introuvable"; return false; }
    WaitForSingleObject(pi.hProcess, INFINITE);
    DWORD code = 1; GetExitCodeProcess(pi.hProcess, &code);
    CloseHandle(pi.hThread); CloseHandle(pi.hProcess);
    if (code != 0) { err = L"extraction (tar code " + std::to_wstring(code) + L")"; return false; }
    return true;
}
// Extrait le ZIP puis remplace appDir ; backup -> rollback si echec.
static bool ApplyZip(const std::wstring& zip, const std::wstring& appDir, bool backup,
                     const std::wstring& stagingDir, std::wstring& err)
{
    std::wstring newDir = stagingDir + L"\\new";
    std::wstring oldDir = stagingDir + L"\\old";
    RemovePath(newDir); RemovePath(oldDir);
    if (!TarExtract(zip, newDir, err)) return false;

    bool hasBackup = false;
    DWORD attr = GetFileAttributesW(appDir.c_str());
    if (backup && attr != INVALID_FILE_ATTRIBUTES)
    {
        if (!MoveDirRetry(appDir, oldDir)) { err = L"backup impossible (app verrouillee ?)"; return false; }
        hasBackup = true;
    }
    else if (attr != INVALID_FILE_ATTRIBUTES) RemovePath(appDir);

    if (!MoveDirRetry(newDir, appDir))
    {
        if (hasBackup) { RemovePath(appDir); MoveDirRetry(oldDir, appDir); }   // rollback
        err = L"bascule impossible";
        return false;
    }
    return true;
}
static bool CreateShortcut(const std::wstring& lnk, const std::wstring& target, const std::wstring& desc)
{
    IShellLinkW* sl = nullptr;
    bool ok = false;
    if (SUCCEEDED(CoCreateInstance(CLSID_ShellLink, nullptr, CLSCTX_INPROC_SERVER, IID_IShellLinkW, (void**)&sl)))
    {
        sl->SetPath(target.c_str());
        std::wstring wd = target; size_t sp = wd.find_last_of(L"\\/"); if (sp != std::wstring::npos) wd.resize(sp);
        sl->SetWorkingDirectory(wd.c_str());
        sl->SetIconLocation(target.c_str(), 0);
        if (!desc.empty()) sl->SetDescription(desc.c_str());
        IPersistFile* pf = nullptr;
        if (SUCCEEDED(sl->QueryInterface(IID_IPersistFile, (void**)&pf)))
        {
            std::wstring dir = lnk; size_t sp2 = dir.find_last_of(L"\\/"); if (sp2 != std::wstring::npos) { dir.resize(sp2); EnsureDir(dir); }
            ok = SUCCEEDED(pf->Save(lnk.c_str(), TRUE));
            pf->Release();
        }
        sl->Release();
    }
    return ok;
}
static std::wstring SelfPath()
{
    wchar_t buf[MAX_PATH]; GetModuleFileNameW(nullptr, buf, MAX_PATH); return buf;
}
static std::wstring KnownFolderLnk(REFKNOWNFOLDERID id, const std::wstring& sub)
{
    PWSTR p = nullptr; std::wstring out;
    if (SUCCEEDED(SHGetKnownFolderPath(id, 0, nullptr, &p))) { out = std::wstring(p) + sub; CoTaskMemFree(p); }
    return out;
}

// ============================ etat UI partage ============================
struct UiState
{
    std::wstring title = L"GivreClone";
    std::wstring status = L"Initialisation…";
    double progress = 0.0;      // 0..1
    bool indeterminate = true;
    bool hasError = false;
    std::wstring errorText;
    bool done = false;
    int marquee = 0;            // animation indeterminee
};
static UiState g_ui;
static CRITICAL_SECTION g_cs;
static HWND g_hwnd = nullptr;
static const UINT WM_APP_REPAINT = WM_APP + 1;
static const UINT WM_APP_QUIT   = WM_APP + 2;

static void UiSet(const std::function<void(UiState&)>& fn)
{
    EnterCriticalSection(&g_cs); fn(g_ui); LeaveCriticalSection(&g_cs);
    if (g_hwnd) PostMessageW(g_hwnd, WM_APP_REPAINT, 0, 0);
}

// ============================ arguments ============================
struct Args
{
    int mode = 0;            // 0=install, 1=update, 2=selftest, 3=defender-exclude (enfant eleve)
    DWORD pid = 0;
    std::wstring target;     // dossier app
    std::wstring relaunch = L"GivreClone.exe";
    std::wstring zip;        // pre-staged (update) OU zip a tester (selftest)
    std::string sha;         // hex attendu (optionnel)
    std::wstring selftestTarget;
    std::wstring excludePath; // mode 3 : chemin a exclure de Windows Defender (process DEJA eleve via runas)
    std::wstring RelaunchPath() const { return target + L"\\" + relaunch; }
};
static Args g_args;

static Args ParseArgs()
{
    Args a;
    int argc = 0; LPWSTR* argv = CommandLineToArgvW(GetCommandLineW(), &argc);
    for (int i = 1; i < argc; i++)
    {
        std::wstring t = argv[i];
        if (t == L"install") a.mode = 0;
        else if (t == L"update") a.mode = 1;
        else if (t == L"--selftest" && i + 2 < argc) { a.mode = 2; a.zip = argv[++i]; a.selftestTarget = argv[++i]; }
        else if (t == L"--pid" && i + 1 < argc) { a.pid = (DWORD)_wtoi(argv[++i]); a.mode = 1; }
        else if (t == L"--target" && i + 1 < argc) a.target = argv[++i];
        else if (t == L"--relaunch" && i + 1 < argc) a.relaunch = argv[++i];
        else if (t == L"--zip" && i + 1 < argc) a.zip = argv[++i];
        else if (t == L"--sha256" && i + 1 < argc) a.sha = WToUtf8(argv[++i]);
        else if (t == L"--defender-exclude" && i + 1 < argc) { a.mode = 3; a.excludePath = argv[++i]; }
    }
    if (argv) LocalFree(argv);
    if (a.target.empty()) a.target = g_installRoot + L"\\app";
    return a;
}

// ============================ orchestration ============================
static const char* SETUP_VERSION = "0.0.0";

// Renvoie true si succes. Pilote l'UI via UiSet.
static bool RunFlow(const Args& a, std::wstring& err)
{
    std::wstring staging = g_installRoot + L"\\update";
    EnsureDir(staging);
    std::wstring zipPath = a.zip.empty() ? (staging + L"\\pkg.zip") : a.zip;
    std::string expectedSha = a.sha;

    if (a.zip.empty())
    {
        // 1) /version (HTTPS) — pas de verif signature ici (split : l'app fait l'Ed25519 pour les MAJ)
        UiSet([&](UiState& u){ u.indeterminate = true; u.status = (a.mode == 1) ? L"Recherche de mise à jour…" : L"Vérification de la version…"; });
        std::string raw;
        std::string url = BaseUrl();
        if (!url.empty() && url.back() == '/') url.pop_back();
        url += "/version?v=" + std::string(SETUP_VERSION);
        std::wstring e;
        // Cloudflare : un flap edge (5xx/HTML) est transitoire -> on ré-essaie (borné) avant d'abandonner.
        bool vok = false;
        for (int attempt = 1; attempt <= 3 && !vok; attempt++)
        {
            raw.clear();
            vok = HttpFetch(url, &raw, nullptr, nullptr, e);
            if (!vok && attempt < 3) Sleep(1000 * attempt);
        }
        if (!vok) { err = L"Serveur injoignable (" + e + L")"; return false; }
        // enveloppe {payload,signature} : on decode le payload (base64) et on lit le JSON interne
        std::string payloadB64;
        if (!JsonString(raw, "payload", payloadB64)) { err = L"Réponse serveur invalide"; return false; }
        std::vector<uint8_t> pj = B64Decode(payloadB64);
        std::string payload((const char*)pj.data(), pj.size());
        bool kill = false; JsonBool(payload, "kill", kill);
        if (kill) { std::string msg; JsonString(payload, "message", msg); err = msg.empty() ? L"Accès bloqué par le serveur." : Utf8ToW(msg); return false; }
        std::string dl, sha, cur;
        if (!JsonString(payload, "download_url", dl) || !JsonString(payload, "sha256", sha) || dl.empty() || sha.empty())
        { err = L"Le serveur n'expose pas de paquet (download_url / sha256)."; return false; }
        expectedSha = sha;
        if (JsonString(payload, "current_version", cur) && !cur.empty())
            UiSet([&](UiState& u){ u.title = L"GivreClone " + Utf8ToW(cur); });

        // 2) download
        UiSet([&](UiState& u){ u.indeterminate = false; u.progress = 0; u.status = L"Téléchargement…"; });
        // Retry+backoff : coupure edge / 524 (~100 s) = transitoire. Re-télécharge from-scratch (SHA-256 revérifié après).
        bool dok = false;
        for (int attempt = 1; attempt <= 4 && !dok; attempt++)
        {
            HANDLE f = CreateFileW(zipPath.c_str(), GENERIC_WRITE, 0, nullptr, CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr);
            if (f == INVALID_HANDLE_VALUE) { err = L"Écriture staging impossible"; return false; }
            dok = HttpFetch(dl, nullptr, f, [](double p){ UiSet([&](UiState& u){ u.progress = p; }); }, e);
            CloseHandle(f);
            if (!dok && attempt < 4) { UiSet([&](UiState& u){ u.indeterminate = true; u.status = L"Nouvelle tentative…"; }); Sleep(2000 * attempt); }
        }
        if (!dok) { err = L"Téléchargement échoué (" + e + L")"; return false; }
    }

    // 3) verif SHA-256
    UiSet([&](UiState& u){ u.indeterminate = true; u.status = L"Vérification de l'intégrité…"; });
    std::string actual;
    if (!Sha256File(zipPath, actual) || expectedSha.empty() || !EqualsCI(actual, expectedSha))
    { err = L"Intégrité invalide (SHA-256) — installation annulée."; return false; }

    // 4) attente app (update)
    if (a.mode == 1 && a.pid != 0)
    {
        UiSet([&](UiState& u){ u.status = L"Fermeture de GivreClone…"; });
        WaitForPid(a.pid, 30000);
    }

    // 5) swap
    UiSet([&](UiState& u){ u.status = L"Installation…"; });
    if (!ApplyZip(zipPath, a.target, a.mode == 1, staging, err)) return false;

    // 6) extras install
    if (a.mode == 0)
    {
        std::wstring self = SelfPath();
        std::wstring dst = g_installRoot + L"\\GivreUpdater.exe";
        if (!EqualsCI(WToUtf8(self), WToUtf8(dst))) { EnsureDir(g_installRoot); CopyFileW(self.c_str(), dst.c_str(), FALSE); }
        std::wstring exe = a.RelaunchPath();
        CreateShortcut(KnownFolderLnk(FOLDERID_Programs, L"\\Givre\\GivreClone.lnk"), exe, L"GivreClone");
        CreateShortcut(KnownFolderLnk(FOLDERID_Desktop, L"\\GivreClone.lnk"), exe, L"GivreClone");

        // 6b) OPT-IN : exclusion Windows Defender (faux positifs AV). L'ajout exige l'admin -> on relance CE meme exe en
        // ELEVE (runas, --defender-exclude <racine>) : UNE seule UAC, uniquement a l'install ET si l'utilisateur accepte.
        // L'updater reste asInvoker -> les MAJ (mode 1) ne repassent JAMAIS ici et restent silencieuses. Best-effort :
        // un refus/echec n'annule pas l'install (l'app fonctionne, l'user pourra ajouter l'exclusion a la main).
        int consent = MessageBoxW(g_hwnd,
            L"Certains antivirus signalent GivreClone par erreur (faux positif).\r\n\r\n"
            L"Ajouter GivreClone aux exclusions de Windows Defender pour éviter cela ?\r\n"
            L"(demande une autorisation administrateur, une seule fois)",
            L"GivreClone — Antivirus", MB_YESNO | MB_ICONQUESTION | MB_SETFOREGROUND);
        if (consent == IDYES)
        {
            UiSet([&](UiState& u){ u.status = L"Ajout de l'exclusion antivirus…"; });
            std::wstring selfp = SelfPath();
            std::wstring params = L"--defender-exclude \"" + g_installRoot + L"\"";
            SHELLEXECUTEINFOW si; ZeroMemory(&si, sizeof(si)); si.cbSize = sizeof(si);
            si.fMask = SEE_MASK_NOCLOSEPROCESS; si.lpVerb = L"runas"; si.lpFile = selfp.c_str();
            si.lpParameters = params.c_str(); si.nShow = SW_HIDE;
            if (ShellExecuteExW(&si) && si.hProcess)
            {
                WaitForSingleObject(si.hProcess, 60000);
                DWORD code = 1; GetExitCodeProcess(si.hProcess, &code); CloseHandle(si.hProcess);
                Log(code == 0 ? L"Defender: exclusion OK" : (L"Defender: exclusion KO (code " + std::to_wstring(code) + L")"));
            }
            else Log(L"Defender: UAC refusée/annulée");
        }
    }

    // 7) fini
    RemovePath(staging + L"\\new"); RemovePath(staging + L"\\old");
    std::wstring marker = g_installRoot + L"\\update-failed.txt"; DeleteFileW(marker.c_str());
    UiSet([&](UiState& u){ u.done = true; u.indeterminate = false; u.progress = 1.0; u.status = L"Lancement de GivreClone…"; });
    Sleep(700);
    std::wstring exe = a.RelaunchPath();
    ShellExecuteW(nullptr, L"open", exe.c_str(), nullptr, a.target.c_str(), SW_SHOWNORMAL);
    Sleep(400);
    return true;
}

static DWORD WINAPI WorkerThread(LPVOID)
{
    std::wstring err;
    bool ok = RunFlow(g_args, err);
    if (!ok)
    {
        std::wstring m = g_installRoot + L"\\update-failed.txt";
        HANDLE h = CreateFileW(m.c_str(), GENERIC_WRITE, 0, nullptr, CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr);
        if (h != INVALID_HANDLE_VALUE) { std::string s = WToUtf8(err); DWORD w = 0; WriteFile(h, s.data(), (DWORD)s.size(), &w, nullptr); CloseHandle(h); }
        Log(L"ECHEC: " + err);
        UiSet([&](UiState& u){ u.hasError = true; u.indeterminate = false; u.errorText = err; u.status = L"Échec"; });
        return 1;
    }
    if (g_hwnd) PostMessageW(g_hwnd, WM_APP_QUIT, 0, 0);
    return 0;
}

// ============================ selftest headless ============================
static int RunSelfTest()
{
    std::wstring resultFile = g_installRoot + L"\\updater-selftest.txt";
    EnsureDir(g_installRoot);
    std::wstring err;
    std::string sha;
    bool shaOk = Sha256File(g_args.zip, sha);
    std::wstring tgt = g_args.selftestTarget;
    RemovePath(tgt);
    bool exOk = TarExtract(g_args.zip, tgt, err);
    // fichiers extraits ?
    int files = 0;
    WIN32_FIND_DATAW fd; std::wstring pat = tgt + L"\\*";
    HANDLE h = FindFirstFileW(pat.c_str(), &fd);
    if (h != INVALID_HANDLE_VALUE) { do { if (!(fd.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY)) files++; } while (FindNextFileW(h, &fd)); FindClose(h); }
    bool pass = shaOk && exOk && files >= 0;
    std::string rep = "sha256=" + sha + "\r\nsha_ok=" + (shaOk?"1":"0") +
                      "\r\nextract_ok=" + (exOk?"1":"0") + "\r\nfiles_top=" + std::to_string(files) +
                      "\r\nRESULT=" + (pass?"OK":"FAIL") + "\r\n";
    HANDLE rf = CreateFileW(resultFile.c_str(), GENERIC_WRITE, 0, nullptr, CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr);
    if (rf != INVALID_HANDLE_VALUE) { DWORD w=0; WriteFile(rf, rep.data(), (DWORD)rep.size(), &w, nullptr); CloseHandle(rf); }
    return pass ? 0 : 1;
}

// ============================ exclusion Windows Defender (enfant ELEVE) ============================
// Lance par le flux d'install via ShellExecuteEx(runas) : ce process est DEJA eleve (une UAC unique, cote install,
// UNIQUEMENT si l'utilisateur a accepte). On ajoute le chemin (passe en argument par le parent, qui a le bon
// %LOCALAPPDATA% de l'UTILISATEUR — pas celui de l'admin) aux exclusions Defender. Idempotent. Best-effort : un echec
// ne bloque jamais l'install. Les MAJ (mode 1) NE passent JAMAIS ici -> l'updater reste asInvoker, MAJ silencieuses.
static int RunDefenderExclude()
{
    if (g_args.excludePath.empty()) return 2;
    std::wstring esc; for (wchar_t c : g_args.excludePath) { if (c == L'\'') esc += L"''"; else esc += c; }   // echappe ' pour PowerShell
    std::wstring params = L"-NoProfile -NonInteractive -WindowStyle Hidden -Command \"Add-MpPreference -ExclusionPath '"
                        + esc + L"' -ErrorAction SilentlyContinue\"";
    SHELLEXECUTEINFOW si; ZeroMemory(&si, sizeof(si)); si.cbSize = sizeof(si);
    si.fMask = SEE_MASK_NOCLOSEPROCESS; si.lpVerb = L"open"; si.lpFile = L"powershell.exe";
    si.lpParameters = params.c_str(); si.nShow = SW_HIDE;
    if (!ShellExecuteExW(&si) || !si.hProcess) return 3;
    WaitForSingleObject(si.hProcess, 30000);
    DWORD code = 1; GetExitCodeProcess(si.hProcess, &code); CloseHandle(si.hProcess);
    return (int)code;
}

// ============================ UI Win32 (GDI custom) ============================
static HFONT g_fTitle = nullptr, g_fBody = nullptr, g_fSmall = nullptr;
static RECT g_closeRect = {0,0,0,0};

static void Paint(HWND hwnd, HDC hdcWin)
{
    RECT rc; GetClientRect(hwnd, &rc);
    int W = rc.right, H = rc.bottom;
    HDC hdc = CreateCompatibleDC(hdcWin);
    HBITMAP bmp = CreateCompatibleBitmap(hdcWin, W, H);
    HBITMAP old = (HBITMAP)SelectObject(hdc, bmp);

    // fond
    HBRUSH bg = CreateSolidBrush(RGB(0x0E, 0x11, 0x17));
    FillRect(hdc, &rc, bg); DeleteObject(bg);
    // liseré ambre en haut
    RECT top = { 0, 0, W, 3 }; HBRUSH amb = CreateSolidBrush(RGB(0xF0, 0xA9, 0x3C));
    FillRect(hdc, &top, amb); DeleteObject(amb);

    UiState s;
    EnterCriticalSection(&g_cs); s = g_ui; LeaveCriticalSection(&g_cs);

    SetBkMode(hdc, TRANSPARENT);

    // icone
    HICON ic = (HICON)LoadImageW(GetModuleHandleW(nullptr), MAKEINTRESOURCEW(IDI_APPICON), IMAGE_ICON, 48, 48, 0);
    if (ic) { DrawIconEx(hdc, W/2 - 24, 40, ic, 48, 48, 0, nullptr, DI_NORMAL); DestroyIcon(ic); }

    // titre
    SelectObject(hdc, g_fTitle); SetTextColor(hdc, RGB(0xF2, 0xF5, 0xFB));
    RECT rt = { 0, 100, W, 130 }; DrawTextW(hdc, s.title.c_str(), -1, &rt, DT_CENTER | DT_SINGLELINE);

    // statut / erreur
    SelectObject(hdc, g_fBody);
    if (s.hasError)
    {
        SetTextColor(hdc, RGB(0xF0, 0x53, 0x3B));
        RECT re = { 30, 150, W - 30, H - 40 };
        DrawTextW(hdc, s.errorText.c_str(), -1, &re, DT_CENTER | DT_WORDBREAK);
    }
    else
    {
        SetTextColor(hdc, RGB(0xA7, 0xB0, 0xC0));
        RECT rs = { 30, 150, W - 30, 176 }; DrawTextW(hdc, s.status.c_str(), -1, &rs, DT_CENTER | DT_SINGLELINE);

        // barre de progression
        int barX = 40, barW = W - 80, barY = 196, barH = 6;
        HBRUSH track = CreateSolidBrush(RGB(0x1A, 0x1E, 0x27));
        RECT rtk = { barX, barY, barX + barW, barY + barH }; FillRect(hdc, &rtk, track); DeleteObject(track);
        HBRUSH fill = CreateSolidBrush(RGB(0xF0, 0xA9, 0x3C));
        if (s.indeterminate)
        {
            int segW = barW / 3;
            int x = barX + ((s.marquee) % (barW + segW)) - segW;
            int a = x < barX ? barX : x;
            int b = (x + segW) > (barX + barW) ? (barX + barW) : (x + segW);
            if (b > a) { RECT rf2 = { a, barY, b, barY + barH }; FillRect(hdc, &rf2, fill); }
        }
        else
        {
            int fw = (int)(barW * (s.progress < 0 ? 0 : s.progress > 1 ? 1 : s.progress));
            RECT rf2 = { barX, barY, barX + fw, barY + barH }; FillRect(hdc, &rf2, fill);
        }
        DeleteObject(fill);
    }

    // bouton fermer (X)
    g_closeRect = { W - 34, 8, W - 10, 32 };
    SelectObject(hdc, g_fBody); SetTextColor(hdc, RGB(0xA7, 0xB0, 0xC0));
    DrawTextW(hdc, L"✕", -1, &g_closeRect, DT_CENTER | DT_VCENTER | DT_SINGLELINE);

    BitBlt(hdcWin, 0, 0, W, H, hdc, 0, 0, SRCCOPY);
    SelectObject(hdc, old); DeleteObject(bmp); DeleteDC(hdc);
}

static LRESULT CALLBACK WndProc(HWND hwnd, UINT msg, WPARAM wp, LPARAM lp)
{
    switch (msg)
    {
        case WM_CREATE:
        {
            g_fTitle = CreateFontW(-22, 0,0,0, FW_SEMIBOLD, 0,0,0, DEFAULT_CHARSET, 0,0, CLEARTYPE_QUALITY, 0, L"Segoe UI");
            g_fBody  = CreateFontW(-15, 0,0,0, FW_NORMAL,   0,0,0, DEFAULT_CHARSET, 0,0, CLEARTYPE_QUALITY, 0, L"Segoe UI");
            g_fSmall = CreateFontW(-12, 0,0,0, FW_NORMAL,   0,0,0, DEFAULT_CHARSET, 0,0, CLEARTYPE_QUALITY, 0, L"Segoe UI");
            SetTimer(hwnd, 1, 33, nullptr);
            CreateThread(nullptr, 0, WorkerThread, nullptr, 0, nullptr);
            return 0;
        }
        case WM_TIMER:
        {
            bool ind; EnterCriticalSection(&g_cs); ind = g_ui.indeterminate && !g_ui.hasError; g_ui.marquee += 6; LeaveCriticalSection(&g_cs);
            if (ind) InvalidateRect(hwnd, nullptr, FALSE);
            return 0;
        }
        case WM_APP_REPAINT: InvalidateRect(hwnd, nullptr, FALSE); return 0;
        case WM_APP_QUIT: DestroyWindow(hwnd); return 0;
        case WM_PAINT:
        {
            PAINTSTRUCT ps; HDC hdc = BeginPaint(hwnd, &ps); Paint(hwnd, hdc); EndPaint(hwnd, &ps); return 0;
        }
        case WM_LBUTTONDOWN:
        {
            POINT pt = { GET_X_LPARAM(lp), GET_Y_LPARAM(lp) };
            if (PtInRect(&g_closeRect, pt)) { DestroyWindow(hwnd); return 0; }
            // drag depuis la zone haute
            if (pt.y < 44) { ReleaseCapture(); SendMessageW(hwnd, WM_NCLBUTTONDOWN, HTCAPTION, 0); return 0; }
            return 0;
        }
        case WM_DESTROY:
            if (g_fTitle) DeleteObject(g_fTitle);
            if (g_fBody) DeleteObject(g_fBody);
            if (g_fSmall) DeleteObject(g_fSmall);
            PostQuitMessage(0);
            return 0;
    }
    return DefWindowProcW(hwnd, msg, wp, lp);
}

int WINAPI wWinMain(HINSTANCE hInst, HINSTANCE, LPWSTR, int)
{
    g_installRoot = LocalAppData() + L"\\Givre";
    InitializeCriticalSection(&g_cs);
    CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED);
    g_args = ParseArgs();

    if (g_args.mode == 2) { int rc = RunSelfTest(); CoUninitialize(); return rc; }
    if (g_args.mode == 3) { int rc = RunDefenderExclude(); CoUninitialize(); return rc; }   // enfant eleve : ajoute l'exclusion Defender puis sort (sans fenetre)

    WNDCLASSEXW wc; ZeroMemory(&wc, sizeof(wc)); wc.cbSize = sizeof(wc);
    wc.lpfnWndProc = WndProc; wc.hInstance = hInst;
    wc.hCursor = LoadCursor(nullptr, IDC_ARROW);
    wc.hIcon = (HICON)LoadImageW(hInst, MAKEINTRESOURCEW(IDI_APPICON), IMAGE_ICON, 0, 0, LR_DEFAULTSIZE);
    wc.lpszClassName = L"GivreUpdaterWnd";
    RegisterClassExW(&wc);

    int W = 480, H = 300;
    int sx = GetSystemMetrics(SM_CXSCREEN), sy = GetSystemMetrics(SM_CYSCREEN);
    HWND hwnd = CreateWindowExW(WS_EX_APPWINDOW, wc.lpszClassName, L"GivreClone",
                                WS_POPUP, (sx - W) / 2, (sy - H) / 2, W, H, nullptr, nullptr, hInst, nullptr);
    g_hwnd = hwnd;
    // coins arrondis
    HRGN rgn = CreateRoundRectRgn(0, 0, W + 1, H + 1, 16, 16); SetWindowRgn(hwnd, rgn, TRUE);
    ShowWindow(hwnd, SW_SHOW); UpdateWindow(hwnd);

    MSG m;
    while (GetMessageW(&m, nullptr, 0, 0)) { TranslateMessage(&m); DispatchMessageW(&m); }
    CoUninitialize();
    DeleteCriticalSection(&g_cs);
    return 0;
}
