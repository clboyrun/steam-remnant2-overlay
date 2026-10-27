#include "hooks.h"
#include "log.h"
#include "ui.h"

#include <windows.h>
#include <d3d12.h>
#include <dxgi1_4.h>

#include <MinHook.h>
#include <imgui.h>
#include <imgui_impl_dx12.h>
#include <imgui_impl_win32.h>

#include <atomic>
#include <format>
#include <mutex>
#include <vector>

extern IMGUI_IMPL_API LRESULT ImGui_ImplWin32_WndProcHandler(HWND, UINT, WPARAM, LPARAM);

namespace r2o::hooks
{
    namespace
    {
        // vtable slots, counted from IUnknown::QueryInterface.
        constexpr int PresentSlot = 8;          // IDXGISwapChain::Present
        constexpr int ResizeBuffersSlot = 13;   // IDXGISwapChain::ResizeBuffers
        constexpr int Present1Slot = 22;        // IDXGISwapChain1::Present1
        constexpr int ResizeBuffers1Slot = 39;  // IDXGISwapChain3::ResizeBuffers1
        constexpr int ExecuteCommandListsSlot = 10; // ID3D12CommandQueue::ExecuteCommandLists

        using PresentFn = HRESULT(STDMETHODCALLTYPE*)(IDXGISwapChain3*, UINT, UINT);
        using Present1Fn = HRESULT(STDMETHODCALLTYPE*)(IDXGISwapChain3*, UINT, UINT, const DXGI_PRESENT_PARAMETERS*);
        using ResizeBuffersFn = HRESULT(STDMETHODCALLTYPE*)(IDXGISwapChain3*, UINT, UINT, UINT, DXGI_FORMAT, UINT);
        using ResizeBuffers1Fn = HRESULT(STDMETHODCALLTYPE*)(IDXGISwapChain3*, UINT, UINT, UINT, DXGI_FORMAT, UINT,
                                                              const UINT*, IUnknown* const*);
        using ExecuteCommandListsFn = void(STDMETHODCALLTYPE*)(ID3D12CommandQueue*, UINT, ID3D12CommandList* const*);

        PresentFn g_present = nullptr;
        Present1Fn g_present1 = nullptr;
        ResizeBuffersFn g_resizeBuffers = nullptr;
        ResizeBuffers1Fn g_resizeBuffers1 = nullptr;
        ExecuteCommandListsFn g_executeCommandLists = nullptr;

        struct Frame
        {
            ID3D12CommandAllocator* Allocator = nullptr;
            ID3D12Resource* BackBuffer = nullptr;
            D3D12_CPU_DESCRIPTOR_HANDLE Rtv{};
            UINT64 FenceValue = 0;
        };

        // Everything we create on the game's device. Rebuilt whenever the swap chain is resized.
        struct Renderer
        {
            ID3D12Device* Device = nullptr;
            ID3D12DescriptorHeap* RtvHeap = nullptr;
            ID3D12DescriptorHeap* SrvHeap = nullptr;
            ID3D12GraphicsCommandList* CommandList = nullptr;
            ID3D12Fence* Fence = nullptr;
            HANDLE FenceEvent = nullptr;
            UINT64 FenceCounter = 0;
            std::vector<Frame> Frames;
            bool Ready = false;
        } g;

        // The game's direct queue, seen in ExecuteCommandLists; the swap chain doesn't expose it.
        std::atomic<ID3D12CommandQueue*> g_queue{nullptr};
        // Present (render thread) and the window procedure (game thread) both touch ImGui. Recursive because
        // ImGui's Win32 handler can call SetCapture, which re-enters the window procedure on the same thread.
        std::recursive_mutex g_lock;
        HWND g_window = nullptr;
        WNDPROC g_gameWndProc = nullptr;
        bool g_imguiReady = false;
        bool g_unsupported = false;

        template <typename T> void Release(T*& p)
        {
            if (p) p->Release();
            p = nullptr;
        }

        void WaitForGpu()
        {
            auto* queue = g_queue.load();
            if (!queue || !g.Fence) return;
            const UINT64 value = ++g.FenceCounter;
            if (FAILED(queue->Signal(g.Fence, value))) return;
            if (g.Fence->GetCompletedValue() < value && SUCCEEDED(g.Fence->SetEventOnCompletion(value, g.FenceEvent)))
                WaitForSingleObject(g.FenceEvent, 2000);
        }

        void DestroyRenderer()
        {
            if (!g.Device) return;
            WaitForGpu();
            if (g.Ready) ImGui_ImplDX12_Shutdown();
            for (auto& f : g.Frames)
            {
                Release(f.Allocator);
                Release(f.BackBuffer);
            }
            g.Frames.clear();
            Release(g.CommandList);
            Release(g.RtvHeap);
            Release(g.SrvHeap);
            Release(g.Fence);
            if (g.FenceEvent) CloseHandle(g.FenceEvent);
            g.FenceEvent = nullptr;
            Release(g.Device);
            g.Ready = false;
        }

        bool IsInputMessage(UINT msg)
        {
            return (msg >= WM_MOUSEFIRST && msg <= WM_MOUSELAST) || msg == WM_KEYDOWN || msg == WM_CHAR
                   || msg == WM_DEADCHAR || msg == WM_INPUT;
        }

        LRESULT CALLBACK WndProc(HWND hwnd, UINT msg, WPARAM wParam, LPARAM lParam)
        {
            if ((msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN) && wParam == ui::ToggleKey)
            {
                if (!(lParam & (1 << 30))) ui::Toggle(); // ignore auto-repeat
                return 0;
            }
            if ((msg == WM_KEYUP || msg == WM_SYSKEYUP) && wParam == ui::ToggleKey) return 0;

            if (ui::IsVisible() && g_imguiReady)
            {
                {
                    std::lock_guard guard(g_lock);
                    ImGui_ImplWin32_WndProcHandler(hwnd, msg, wParam, lParam);
                }
                // While the panel is open the game doesn't see mouse or typing. Key-ups still go through so
                // nothing stays held, and raw input still gets DefWindowProc for cleanup.
                if (msg == WM_INPUT) return DefWindowProcW(hwnd, msg, wParam, lParam);
                if (IsInputMessage(msg)) return 0;
            }
            return CallWindowProcW(g_gameWndProc, hwnd, msg, wParam, lParam);
        }

        bool CreateRenderTargets(IDXGISwapChain3* swapChain)
        {
            const UINT size = g.Device->GetDescriptorHandleIncrementSize(D3D12_DESCRIPTOR_HEAP_TYPE_RTV);
            D3D12_CPU_DESCRIPTOR_HANDLE rtv = g.RtvHeap->GetCPUDescriptorHandleForHeapStart();
            for (UINT i = 0; i < g.Frames.size(); i++)
            {
                auto& f = g.Frames[i];
                if (FAILED(swapChain->GetBuffer(i, IID_PPV_ARGS(&f.BackBuffer)))) return false;
                f.Rtv = rtv;
                g.Device->CreateRenderTargetView(f.BackBuffer, nullptr, f.Rtv);
                rtv.ptr += size;
            }
            return true;
        }

        bool CreateRenderer(IDXGISwapChain3* swapChain)
        {
            if (FAILED(swapChain->GetDevice(IID_PPV_ARGS(&g.Device))))
            {
                // A DirectX 11 swap chain (the game started with -dx11).
                g_unsupported = true;
                Log("The game isn't running on DirectX 12; the in-game panel only supports DirectX 12.");
                return false;
            }

            // Make sure the queue we caught belongs to this device; otherwise catch another one.
            ID3D12Device* queueDevice = nullptr;
            g_queue.load()->GetDevice(IID_PPV_ARGS(&queueDevice));
            const bool sameDevice = queueDevice == g.Device;
            Release(queueDevice);
            if (!sameDevice)
            {
                g_queue = nullptr;
                Release(g.Device);
                return false;
            }

            DXGI_SWAP_CHAIN_DESC desc{};
            swapChain->GetDesc(&desc);
            g.Frames.resize(desc.BufferCount);

            D3D12_DESCRIPTOR_HEAP_DESC rtvDesc{D3D12_DESCRIPTOR_HEAP_TYPE_RTV, desc.BufferCount, D3D12_DESCRIPTOR_HEAP_FLAG_NONE, 0};
            D3D12_DESCRIPTOR_HEAP_DESC srvDesc{D3D12_DESCRIPTOR_HEAP_TYPE_CBV_SRV_UAV, 1, D3D12_DESCRIPTOR_HEAP_FLAG_SHADER_VISIBLE, 0};
            if (FAILED(g.Device->CreateDescriptorHeap(&rtvDesc, IID_PPV_ARGS(&g.RtvHeap)))
                || FAILED(g.Device->CreateDescriptorHeap(&srvDesc, IID_PPV_ARGS(&g.SrvHeap)))
                || FAILED(g.Device->CreateFence(0, D3D12_FENCE_FLAG_NONE, IID_PPV_ARGS(&g.Fence))))
            {
                Log("Couldn't create descriptor heaps or fence.");
                return false;
            }
            g.FenceEvent = CreateEventW(nullptr, FALSE, FALSE, nullptr);

            for (auto& f : g.Frames)
                if (FAILED(g.Device->CreateCommandAllocator(D3D12_COMMAND_LIST_TYPE_DIRECT, IID_PPV_ARGS(&f.Allocator)))) return false;
            if (FAILED(g.Device->CreateCommandList(0, D3D12_COMMAND_LIST_TYPE_DIRECT, g.Frames[0].Allocator, nullptr,
                                                   IID_PPV_ARGS(&g.CommandList))))
                return false;
            g.CommandList->Close();
            if (!CreateRenderTargets(swapChain)) return false;

            if (!g_imguiReady)
            {
                ImGui::CreateContext();
                ui::Setup();
                ImGui_ImplWin32_Init(desc.OutputWindow);
                g_window = desc.OutputWindow;
                g_gameWndProc = reinterpret_cast<WNDPROC>(
                    SetWindowLongPtrW(g_window, GWLP_WNDPROC, reinterpret_cast<LONG_PTR>(WndProc)));
                g_imguiReady = true;
            }

            ImGui_ImplDX12_Init(g.Device, int(desc.BufferCount), desc.BufferDesc.Format, g.SrvHeap,
                                g.SrvHeap->GetCPUDescriptorHandleForHeapStart(), g.SrvHeap->GetGPUDescriptorHandleForHeapStart());
            g.Ready = true;
            Log(std::format("Renderer ready: {} buffers, {}x{}, format {}.", desc.BufferCount, desc.BufferDesc.Width,
                            desc.BufferDesc.Height, int(desc.BufferDesc.Format)));
            return true;
        }

        void Barrier(ID3D12Resource* resource, D3D12_RESOURCE_STATES before, D3D12_RESOURCE_STATES after)
        {
            D3D12_RESOURCE_BARRIER barrier{};
            barrier.Type = D3D12_RESOURCE_BARRIER_TYPE_TRANSITION;
            barrier.Transition.pResource = resource;
            barrier.Transition.Subresource = D3D12_RESOURCE_BARRIER_ALL_SUBRESOURCES;
            barrier.Transition.StateBefore = before;
            barrier.Transition.StateAfter = after;
            g.CommandList->ResourceBarrier(1, &barrier);
        }

        void Render(IDXGISwapChain3* swapChain)
        {
            if (g_unsupported) return;
            std::lock_guard guard(g_lock);

            auto* queue = g_queue.load();
            if (!queue) return; // haven't seen the game's queue yet
            if (!g.Ready && !CreateRenderer(swapChain))
            {
                DestroyRenderer();
                return;
            }
            ui::Tick();
            if (ui::IsIdle()) return; // nothing on screen: cost the game nothing

            ImGui_ImplDX12_NewFrame();
            ImGui_ImplWin32_NewFrame();
            ImGui::NewFrame();
            ui::Draw();
            ImGui::Render();

            const UINT index = swapChain->GetCurrentBackBufferIndex();
            if (index >= g.Frames.size()) return;
            auto& f = g.Frames[index];

            // Don't reuse this frame's allocator until the GPU is done with what we recorded last time.
            if (g.Fence->GetCompletedValue() < f.FenceValue && SUCCEEDED(g.Fence->SetEventOnCompletion(f.FenceValue, g.FenceEvent)))
                WaitForSingleObject(g.FenceEvent, 1000);

            f.Allocator->Reset();
            g.CommandList->Reset(f.Allocator, nullptr);
            Barrier(f.BackBuffer, D3D12_RESOURCE_STATE_PRESENT, D3D12_RESOURCE_STATE_RENDER_TARGET);
            g.CommandList->OMSetRenderTargets(1, &f.Rtv, FALSE, nullptr);
            g.CommandList->SetDescriptorHeaps(1, &g.SrvHeap);
            ImGui_ImplDX12_RenderDrawData(ImGui::GetDrawData(), g.CommandList);
            Barrier(f.BackBuffer, D3D12_RESOURCE_STATE_RENDER_TARGET, D3D12_RESOURCE_STATE_PRESENT);
            g.CommandList->Close();

            ID3D12CommandList* lists[] = {g.CommandList};
            queue->ExecuteCommandLists(1, lists);
            queue->Signal(g.Fence, f.FenceValue = ++g.FenceCounter);
        }

        HRESULT STDMETHODCALLTYPE HookPresent(IDXGISwapChain3* swapChain, UINT syncInterval, UINT flags)
        {
            if (!(flags & DXGI_PRESENT_TEST)) Render(swapChain);
            return g_present(swapChain, syncInterval, flags);
        }

        HRESULT STDMETHODCALLTYPE HookPresent1(IDXGISwapChain3* swapChain, UINT syncInterval, UINT flags,
                                               const DXGI_PRESENT_PARAMETERS* parameters)
        {
            if (!(flags & DXGI_PRESENT_TEST)) Render(swapChain);
            return g_present1(swapChain, syncInterval, flags, parameters);
        }

        // The swap chain's buffers can't be resized while we hold references to them: drop everything first.
        HRESULT STDMETHODCALLTYPE HookResizeBuffers(IDXGISwapChain3* swapChain, UINT count, UINT width, UINT height,
                                                    DXGI_FORMAT format, UINT flags)
        {
            {
                std::lock_guard guard(g_lock);
                DestroyRenderer();
            }
            return g_resizeBuffers(swapChain, count, width, height, format, flags);
        }

        HRESULT STDMETHODCALLTYPE HookResizeBuffers1(IDXGISwapChain3* swapChain, UINT count, UINT width, UINT height,
                                                     DXGI_FORMAT format, UINT flags, const UINT* nodeMasks,
                                                     IUnknown* const* queues)
        {
            {
                std::lock_guard guard(g_lock);
                DestroyRenderer();
            }
            return g_resizeBuffers1(swapChain, count, width, height, format, flags, nodeMasks, queues);
        }

        void STDMETHODCALLTYPE HookExecuteCommandLists(ID3D12CommandQueue* queue, UINT count, ID3D12CommandList* const* lists)
        {
            if (!g_queue.load() && queue->GetDesc().Type == D3D12_COMMAND_LIST_TYPE_DIRECT) g_queue = queue;
            g_executeCommandLists(queue, count, lists);
        }

        template <typename T> bool Hook(void* target, void* detour, T* original, const char* name)
        {
            const MH_STATUS status = MH_CreateHook(target, detour, reinterpret_cast<void**>(original));
            if (status != MH_OK) Log(std::format("Couldn't hook {}: {}", name, MH_StatusToString(status)));
            return status == MH_OK;
        }
    }

    bool Install()
    {
        // A throwaway window, device, queue and swap chain, only to find where DXGI and D3D12 keep these functions.
        WNDCLASSEXW wc{sizeof wc, CS_HREDRAW | CS_VREDRAW, DefWindowProcW, 0, 0, GetModuleHandleW(nullptr)};
        wc.lpszClassName = L"Remnant2OverlayProbe";
        RegisterClassExW(&wc);
        HWND window = CreateWindowExW(0, wc.lpszClassName, L"", WS_OVERLAPPEDWINDOW, 0, 0, 100, 100, nullptr, nullptr,
                                      wc.hInstance, nullptr);

        ID3D12Device* device = nullptr;
        ID3D12CommandQueue* queue = nullptr;
        IDXGIFactory2* factory = nullptr;
        IDXGISwapChain1* swapChain1 = nullptr;
        IDXGISwapChain3* swapChain = nullptr;
        bool ok = false;

        D3D12_COMMAND_QUEUE_DESC queueDesc{};
        queueDesc.Type = D3D12_COMMAND_LIST_TYPE_DIRECT;
        DXGI_SWAP_CHAIN_DESC1 desc{};
        desc.Width = 100;
        desc.Height = 100;
        desc.Format = DXGI_FORMAT_R8G8B8A8_UNORM;
        desc.SampleDesc.Count = 1;
        desc.BufferUsage = DXGI_USAGE_RENDER_TARGET_OUTPUT;
        desc.BufferCount = 2;
        desc.SwapEffect = DXGI_SWAP_EFFECT_FLIP_DISCARD;

        if (!window) Log("Couldn't create the probe window.");
        else if (FAILED(D3D12CreateDevice(nullptr, D3D_FEATURE_LEVEL_11_0, IID_PPV_ARGS(&device)))) Log("D3D12CreateDevice failed.");
        else if (FAILED(device->CreateCommandQueue(&queueDesc, IID_PPV_ARGS(&queue)))) Log("CreateCommandQueue failed.");
        else if (FAILED(CreateDXGIFactory1(IID_PPV_ARGS(&factory)))) Log("CreateDXGIFactory1 failed.");
        else if (FAILED(factory->CreateSwapChainForHwnd(queue, window, &desc, nullptr, nullptr, &swapChain1))) Log("CreateSwapChainForHwnd failed.");
        else if (FAILED(swapChain1->QueryInterface(IID_PPV_ARGS(&swapChain)))) Log("No IDXGISwapChain3.");
        else if (MH_Initialize() != MH_OK) Log("MH_Initialize failed.");
        else
        {
            void** sc = *reinterpret_cast<void***>(swapChain);
            void** q = *reinterpret_cast<void***>(queue);
            ok = Hook(sc[PresentSlot], reinterpret_cast<void*>(HookPresent), &g_present, "Present")
                 && Hook(sc[ResizeBuffersSlot], reinterpret_cast<void*>(HookResizeBuffers), &g_resizeBuffers, "ResizeBuffers")
                 && Hook(q[ExecuteCommandListsSlot], reinterpret_cast<void*>(HookExecuteCommandLists), &g_executeCommandLists, "ExecuteCommandLists");
            // Optional: newer paths the game may use instead.
            Hook(sc[Present1Slot], reinterpret_cast<void*>(HookPresent1), &g_present1, "Present1");
            Hook(sc[ResizeBuffers1Slot], reinterpret_cast<void*>(HookResizeBuffers1), &g_resizeBuffers1, "ResizeBuffers1");
            ok = ok && MH_EnableHook(MH_ALL_HOOKS) == MH_OK;
        }

        Release(swapChain);
        Release(swapChain1);
        Release(factory);
        Release(queue);
        Release(device);
        if (window) DestroyWindow(window);
        UnregisterClassW(wc.lpszClassName, wc.hInstance);
        return ok;
    }
}
