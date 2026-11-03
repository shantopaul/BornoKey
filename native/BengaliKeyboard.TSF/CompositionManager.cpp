#include "TextService.h"
#include <new>

namespace BornoKey::TSF
{
    CompositionEditSession::CompositionEditSession(ITfContext* context, const std::wstring& text) noexcept
        : _context(context), _text(text)
    {
        if (_context != nullptr)
        {
            _context->AddRef();
        }
    }

    CompositionEditSession::~CompositionEditSession() noexcept
    {
        if (_context != nullptr)
        {
            _context->Release();
            _context = nullptr;
        }
    }

    STDMETHODIMP CompositionEditSession::QueryInterface(REFIID riid, void** ppvObj)
    {
        if (ppvObj == nullptr)
        {
            return E_INVALIDARG;
        }

        *ppvObj = nullptr;
        if (IsEqualIID(riid, IID_IUnknown) || IsEqualIID(riid, IID_ITfEditSession))
        {
            *ppvObj = static_cast<ITfEditSession*>(this);
            AddRef();
            return S_OK;
        }

        return E_NOINTERFACE;
    }

    STDMETHODIMP_(ULONG) CompositionEditSession::AddRef()
    {
        return ++_refCount;
    }

    STDMETHODIMP_(ULONG) CompositionEditSession::Release()
    {
        const ULONG remaining = --_refCount;
        if (remaining == 0)
        {
            delete this;
        }
        return remaining;
    }

    STDMETHODIMP CompositionEditSession::DoEditSession(TfEditCookie ec)
    {
        if (_context == nullptr || _text.empty() || _text.size() > kMaxCompositionLength)
        {
            return E_INVALIDARG;
        }

        ITfInsertAtSelection* insertAtSelection = nullptr;
        HRESULT hr = _context->QueryInterface(
            IID_ITfInsertAtSelection,
            reinterpret_cast<void**>(&insertAtSelection));

        if (FAILED(hr) || insertAtSelection == nullptr)
        {
            return hr;
        }

        ITfRange* insertedRange = nullptr;
        hr = insertAtSelection->InsertTextAtSelection(
            ec,
            0,
            _text.c_str(),
            static_cast<LONG>(_text.size()),
            &insertedRange);

        if (SUCCEEDED(hr) && insertedRange != nullptr)
        {
            insertedRange->Collapse(ec, TF_ANCHOR_END);

            TF_SELECTION selection{};
            selection.range = insertedRange;
            selection.style.ase = TF_AE_NONE;
            selection.style.fInterimChar = FALSE;
            _context->SetSelection(ec, 1, &selection);

            insertedRange->Release();
        }

        insertAtSelection->Release();
        return hr;
    }

    HRESULT TextService::CommitUnicodeString(ITfContext* context, const std::wstring& text) noexcept
    {
        if (context == nullptr || _clientId == TF_CLIENTID_NULL)
        {
            return E_INVALIDARG;
        }

        if (text.empty() || text.size() > kMaxCompositionLength)
        {
            return E_INVALIDARG;
        }

        auto* editSession = new (std::nothrow) CompositionEditSession(context, text);
        if (editSession == nullptr)
        {
            return E_OUTOFMEMORY;
        }

        HRESULT sessionHr = S_OK;
        const HRESULT hr = context->RequestEditSession(
            _clientId,
            editSession,
            TF_ES_ASYNCDONTCARE | TF_ES_READWRITE,
            &sessionHr);

        editSession->Release();
        return SUCCEEDED(hr) ? sessionHr : hr;
    }
}
