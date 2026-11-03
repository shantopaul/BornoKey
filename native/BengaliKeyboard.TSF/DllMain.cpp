#include "TextService.h"
#include <new>

namespace BornoKey::TSF
{
    HINSTANCE g_hInstance = nullptr;
    std::atomic<long> g_dllRefCount{ 0 };

    class TextServiceClassFactory final : public IClassFactory
    {
    public:
        STDMETHODIMP QueryInterface(REFIID riid, void** ppvObj) override
        {
            if (ppvObj == nullptr)
            {
                return E_INVALIDARG;
            }

            *ppvObj = nullptr;
            if (IsEqualIID(riid, IID_IUnknown) || IsEqualIID(riid, IID_IClassFactory))
            {
                *ppvObj = static_cast<IClassFactory*>(this);
                AddRef();
                return S_OK;
            }

            return E_NOINTERFACE;
        }

        STDMETHODIMP_(ULONG) AddRef() override
        {
            ++g_dllRefCount;
            return 2;
        }

        STDMETHODIMP_(ULONG) Release() override
        {
            --g_dllRefCount;
            return 1;
        }

        STDMETHODIMP CreateInstance(IUnknown* pUnkOuter, REFIID riid, void** ppvObj) override
        {
            if (ppvObj == nullptr)
            {
                return E_INVALIDARG;
            }

            *ppvObj = nullptr;
            if (pUnkOuter != nullptr)
            {
                return CLASS_E_NOAGGREGATION;
            }

            auto* instance = new (std::nothrow) TextService();
            if (instance == nullptr)
            {
                return E_OUTOFMEMORY;
            }

            const HRESULT hr = instance->QueryInterface(riid, ppvObj);
            instance->Release();
            return hr;
        }

        STDMETHODIMP LockServer(BOOL fLock) override
        {
            if (fLock)
            {
                ++g_dllRefCount;
            }
            else
            {
                --g_dllRefCount;
            }
            return S_OK;
        }
    };

    static TextServiceClassFactory g_classFactory;
}

BOOL APIENTRY DllMain(HINSTANCE hModule, DWORD ul_reason_for_call, LPVOID /*lpReserved*/)
{
    if (ul_reason_for_call == DLL_PROCESS_ATTACH)
    {
        BornoKey::TSF::g_hInstance = hModule;
        DisableThreadLibraryCalls(hModule);
    }
    return TRUE;
}

STDAPI DllGetClassObject(REFCLSID rclsid, REFIID riid, LPVOID* ppvObj)
{
    if (ppvObj == nullptr)
    {
        return E_INVALIDARG;
    }

    *ppvObj = nullptr;
    if (!IsEqualCLSID(rclsid, BornoKey::TSF::CLSID_BornoKeyTextService))
    {
        return CLASS_E_CLASSNOTAVAILABLE;
    }

    return BornoKey::TSF::g_classFactory.QueryInterface(riid, ppvObj);
}

STDAPI DllCanUnloadNow(void)
{
    return (BornoKey::TSF::g_dllRefCount.load() <= 0) ? S_OK : S_FALSE;
}
