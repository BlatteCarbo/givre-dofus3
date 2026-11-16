#include "../src/version_proxy.cpp"
#include <thread>

namespace {
int shutdown_calls = 0;
std::int32_t shutdown_status = 1;
std::int32_t WINAPI FakeShutdown() {
    ++shutdown_calls;
    return shutdown_status;
}
}

int main() {
    // Use a real loader reference, but no Dofus process, hooks or game runtime.
    g_host_module = LoadLibraryW(L"kernel32.dll");
    if (g_host_module == nullptr) { return 1; }
    g_host_shutdown = FakeShutdown;
    g_unity_thread_id = GetCurrentThreadId();
    g_state.store(2);
    std::thread caller([] { DNB_Shutdown(); });
    caller.join();
    if (shutdown_calls != 0 || g_host_module == nullptr || g_state != 2) { return 2; }
    UnityMessageHook(HC_ACTION, 0, 0);
    if (shutdown_calls != 1 || g_host_module == nullptr || g_state != 4) { return 3; }
    UnityMessageHook(HC_ACTION, 0, 0);
    if (shutdown_calls != 1) { return 4; } // No implicit retry on subsequent ticks.
    shutdown_status = 0;
    {
        const HostCall active_callback;
        DNB_Shutdown();
        if (shutdown_calls != 1 || g_host_module == nullptr) { return 5; }
    }
    DNB_Shutdown();
    if (shutdown_calls != 2 || g_host_module != nullptr || g_state != 3) { return 6; }
    return 0;
}
