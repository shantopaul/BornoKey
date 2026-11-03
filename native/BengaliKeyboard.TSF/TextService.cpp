#include "TextService.h"

namespace BornoKey::TSF
{
    TextService::TextService() noexcept
    {
        ++g_dllRefCount;
    }

    TextService::~TextService() noexcept
    {
        Deactivate();
        --g_dllRefCount;
    }

    STDMETHODIMP TextService::QueryInterface(REFIID riid, void** ppvObj)
    {
        if (ppvObj == nullptr)
        {
            return E_INVALIDARG;
        }

        *ppvObj = nullptr;

        if (IsEqualIID(riid, IID_IUnknown) ||
            IsEqualIID(riid, IID_ITfTextInputProcessor) ||
            IsEqualIID(riid, IID_ITfTextInputProcessorEx))
        {
            *ppvObj = static_cast<ITfTextInputProcessorEx*>(this);
        }
        else if (IsEqualIID(riid, IID_ITfKeyEventSink))
        {
            *ppvObj = static_cast<ITfKeyEventSink*>(this);
        }
        else if (IsEqualIID(riid, IID_ITfCompositionSink))
        {
            *ppvObj = static_cast<ITfCompositionSink*>(this);
        }
        else
        {
            return E_NOINTERFACE;
        }

        AddRef();
        return S_OK;
    }

    STDMETHODIMP_(ULONG) TextService::AddRef()
    {
        return ++_refCount;
    }

    STDMETHODIMP_(ULONG) TextService::Release()
    {
        const ULONG remaining = --_refCount;
        if (remaining == 0)
        {
            delete this;
        }
        return remaining;
    }

    STDMETHODIMP TextService::Activate(ITfThreadMgr* ptim, TfClientId tid)
    {
        return ActivateEx(ptim, tid, 0);
    }

    STDMETHODIMP TextService::ActivateEx(ITfThreadMgr* ptim, TfClientId tid, DWORD dwFlags)
    {
        if (ptim == nullptr || tid == TF_CLIENTID_NULL)
        {
            return E_INVALIDARG;
        }

        _threadMgr = ptim;
        _threadMgr->AddRef();
        _clientId = tid;
        _activateFlags = dwFlags;

        ITfKeystrokeMgr* keystrokeMgr = nullptr;
        if (SUCCEEDED(_threadMgr->QueryInterface(IID_ITfKeystrokeMgr, reinterpret_cast<void**>(&keystrokeMgr))) && keystrokeMgr != nullptr)
        {
            keystrokeMgr->AdviseKeyEventSink(_clientId, static_cast<ITfKeyEventSink*>(this), TRUE);
            keystrokeMgr->Release();
        }

        return S_OK;
    }

    STDMETHODIMP TextService::Deactivate()
    {
        if (_threadMgr != nullptr)
        {
            ITfKeystrokeMgr* keystrokeMgr = nullptr;
            if (SUCCEEDED(_threadMgr->QueryInterface(IID_ITfKeystrokeMgr, reinterpret_cast<void**>(&keystrokeMgr))) && keystrokeMgr != nullptr)
            {
                keystrokeMgr->UnadviseKeyEventSink(_clientId);
                keystrokeMgr->Release();
            }

            _threadMgr->Release();
            _threadMgr = nullptr;
        }

        _clientId = TF_CLIENTID_NULL;
        _activateFlags = 0;
        return S_OK;
    }

    STDMETHODIMP TextService::OnSetFocus(BOOL /*fForeground*/)
    {
        return S_OK;
    }

    STDMETHODIMP TextService::OnTestKeyDown(ITfContext* /*pic*/, WPARAM /*wParam*/, LPARAM /*lParam*/, BOOL* pfEaten)
    {
        if (pfEaten == nullptr)
        {
            return E_INVALIDARG;
        }
        *pfEaten = FALSE;
        return S_OK;
    }

    STDMETHODIMP TextService::OnKeyDown(ITfContext* /*pic*/, WPARAM /*wParam*/, LPARAM /*lParam*/, BOOL* pfEaten)
    {
        if (pfEaten == nullptr)
        {
            return E_INVALIDARG;
        }
        *pfEaten = FALSE;
        return S_OK;
    }

    STDMETHODIMP TextService::OnTestKeyUp(ITfContext* /*pic*/, WPARAM /*wParam*/, LPARAM /*lParam*/, BOOL* pfEaten)
    {
        if (pfEaten == nullptr)
        {
            return E_INVALIDARG;
        }
        *pfEaten = FALSE;
        return S_OK;
    }

    STDMETHODIMP TextService::OnKeyUp(ITfContext* /*pic*/, WPARAM /*wParam*/, LPARAM /*lParam*/, BOOL* pfEaten)
    {
        if (pfEaten == nullptr)
        {
            return E_INVALIDARG;
        }
        *pfEaten = FALSE;
        return S_OK;
    }

    STDMETHODIMP TextService::OnPreservedKey(ITfContext* /*pic*/, REFGUID /*rguid*/, BOOL* pfEaten)
    {
        if (pfEaten == nullptr)
        {
            return E_INVALIDARG;
        }
        *pfEaten = FALSE;
        return S_OK;
    }

    STDMETHODIMP TextService::OnCompositionTerminated(TfEditCookie /*ecWrite*/, ITfComposition* /*pComposition*/)
    {
        return S_OK;
    }
}
