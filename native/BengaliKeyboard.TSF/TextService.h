#pragma once

#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <ole2.h>
#include <msctf.h>
#include <atomic>
#include <string>

namespace BornoKey::TSF
{
    // {8F4E2D1A-5C3B-4E9A-9D2B-7A1E6C8F0B3D}
    inline constexpr GUID CLSID_BornoKeyTextService =
    { 0x8f4e2d1a, 0x5c3b, 0x4e9a, { 0x9d, 0x2b, 0x7a, 0x1e, 0x6c, 0x8f, 0x0b, 0x3d } };

    // {D4A1E9C2-8B7F-4C6E-A3D1-5F2B9E8C4A7D}
    inline constexpr GUID GUID_BornoKeyProfile =
    { 0xd4a1e9c2, 0x8b7f, 0x4c6e, { 0xa3, 0xd1, 0x5f, 0x2b, 0x9e, 0x8c, 0x4a, 0x7d } };

    // Bengali (Bangladesh) LANGID: 0x0845
    inline constexpr LANGID kBanglaBangladeshLangId = MAKELANGID(LANG_BENGALI, SUBLANG_BENGALI_BANGLADESH);
    inline constexpr size_t kMaxCompositionLength = 256;

    extern HINSTANCE g_hInstance;
    extern std::atomic<long> g_dllRefCount;

    class CompositionEditSession final : public ITfEditSession
    {
    public:
        CompositionEditSession(ITfContext* context, const std::wstring& text) noexcept;
        ~CompositionEditSession() noexcept;

        // IUnknown
        STDMETHODIMP QueryInterface(REFIID riid, void** ppvObj) override;
        STDMETHODIMP_(ULONG) AddRef() override;
        STDMETHODIMP_(ULONG) Release() override;

        // ITfEditSession
        STDMETHODIMP DoEditSession(TfEditCookie ec) override;

    private:
        std::atomic<ULONG> _refCount{ 1 };
        ITfContext* _context{ nullptr };
        std::wstring _text;
    };

    class TextService final :
        public ITfTextInputProcessorEx,
        public ITfKeyEventSink,
        public ITfCompositionSink
    {
    public:
        TextService() noexcept;
        ~TextService() noexcept;

        // IUnknown
        STDMETHODIMP QueryInterface(REFIID riid, void** ppvObj) override;
        STDMETHODIMP_(ULONG) AddRef() override;
        STDMETHODIMP_(ULONG) Release() override;

        // ITfTextInputProcessor
        STDMETHODIMP Activate(ITfThreadMgr* ptim, TfClientId tid) override;
        STDMETHODIMP Deactivate() override;

        // ITfTextInputProcessorEx
        STDMETHODIMP ActivateEx(ITfThreadMgr* ptim, TfClientId tid, DWORD dwFlags) override;

        // ITfKeyEventSink
        STDMETHODIMP OnSetFocus(BOOL fForeground) override;
        STDMETHODIMP OnTestKeyDown(ITfContext* pic, WPARAM wParam, LPARAM lParam, BOOL* pfEaten) override;
        STDMETHODIMP OnKeyDown(ITfContext* pic, WPARAM wParam, LPARAM lParam, BOOL* pfEaten) override;
        STDMETHODIMP OnTestKeyUp(ITfContext* pic, WPARAM wParam, LPARAM lParam, BOOL* pfEaten) override;
        STDMETHODIMP OnKeyUp(ITfContext* pic, WPARAM wParam, LPARAM lParam, BOOL* pfEaten) override;
        STDMETHODIMP OnPreservedKey(ITfContext* pic, REFGUID rguid, BOOL* pfEaten) override;

        // ITfCompositionSink
        STDMETHODIMP OnCompositionTerminated(TfEditCookie ecWrite, ITfComposition* pComposition) override;

        HRESULT CommitUnicodeString(ITfContext* context, const std::wstring& text) noexcept;

    private:
        std::atomic<ULONG> _refCount{ 1 };
        ITfThreadMgr* _threadMgr{ nullptr };
        TfClientId _clientId{ TF_CLIENTID_NULL };
        DWORD _activateFlags{ 0 };
    };
}
