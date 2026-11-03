#include "TextService.h"
#include <shlwapi.h>
#include <shlobj.h>

namespace BornoKey::TSF
{
    namespace
    {
        constexpr wchar_t kServiceDescription[] = L"BornoKey Bengali Input Service (Ridmik)";

        // Security control (security/security-checklist.md Finding CHK-08):
        // Verify that the TSF in-proc server DLL resides inside an Administrator-protected
        // Program Files directory before registering COM InprocServer32.
        bool IsModuleInTrustedProgramFiles(const wchar_t* modulePath) noexcept
        {
            if (modulePath == nullptr || modulePath[0] == L'\0')
            {
                return false;
            }

            wchar_t programFiles[MAX_PATH]{};
            if (SUCCEEDED(SHGetFolderPathW(nullptr, CSIDL_PROGRAM_FILES, nullptr, 0, programFiles)))
            {
                if ( PathIsPrefixW(programFiles, modulePath) == TRUE )
                {
                    return true;
                }
            }

            wchar_t programFilesX86[MAX_PATH]{};
            if (SUCCEEDED(SHGetFolderPathW(nullptr, CSIDL_PROGRAM_FILESX86, nullptr, 0, programFilesX86)))
            {
                if ( PathIsPrefixW(programFilesX86, modulePath) == TRUE )
                {
                    return true;
                }
            }

            return false;
        }
    }
}

STDAPI DllRegisterServer(void)
{
    using namespace BornoKey::TSF;

    wchar_t modulePath[MAX_PATH]{};
    const DWORD copied = GetModuleFileNameW(g_hInstance, modulePath, MAX_PATH);
    if (copied == 0 || copied >= MAX_PATH)
    {
        return E_FAIL;
    }

    if (!IsModuleInTrustedProgramFiles(modulePath))
    {
        return E_ACCESSDENIED;
    }

    wchar_t clsidStr[64]{};
    if (StringFromGUID2(CLSID_BornoKeyTextService, clsidStr, ARRAYSIZE(clsidStr)) == 0)
    {
        return E_FAIL;
    }

    std::wstring regPath = L"SOFTWARE\\Classes\\CLSID\\";
    regPath += clsidStr;

    HKEY hClsidKey = nullptr;
    if (RegCreateKeyExW(HKEY_LOCAL_MACHINE, regPath.c_str(), 0, nullptr, 0, KEY_WRITE, nullptr, &hClsidKey, nullptr) != ERROR_SUCCESS)
    {
        return E_ACCESSDENIED;
    }

    RegSetValueExW(
        hClsidKey,
        nullptr,
        0,
        REG_SZ,
        reinterpret_cast<const BYTE*>(kServiceDescription),
        static_cast<DWORD>(sizeof(kServiceDescription)));

    HKEY hInprocKey = nullptr;
    const LSTATUS inprocStatus = RegCreateKeyExW(hClsidKey, L"InprocServer32", 0, nullptr, 0, KEY_WRITE, nullptr, &hInprocKey, nullptr);
    if (inprocStatus == ERROR_SUCCESS)
    {
        RegSetValueExW(
            hInprocKey,
            nullptr,
            0,
            REG_SZ,
            reinterpret_cast<const BYTE*>(modulePath),
            static_cast<DWORD>((wcslen(modulePath) + 1) * sizeof(wchar_t)));

        constexpr wchar_t kThreadingModel[] = L"Apartment";
        RegSetValueExW(
            hInprocKey,
            L"ThreadingModel",
            0,
            REG_SZ,
            reinterpret_cast<const BYTE*>(kThreadingModel),
            static_cast<DWORD>(sizeof(kThreadingModel)));

        RegCloseKey(hInprocKey);
    }

    RegCloseKey(hClsidKey);
    if (inprocStatus != ERROR_SUCCESS)
    {
        return E_FAIL;
    }

    ITfInputProcessorProfileMgr* profileMgr = nullptr;
    HRESULT hr = CoCreateInstance(
        CLSID_TF_InputProcessorProfiles,
        nullptr,
        CLSCTX_INPROC_SERVER,
        IID_ITfInputProcessorProfileMgr,
        reinterpret_cast<void**>(&profileMgr));

    if (SUCCEEDED(hr) && profileMgr != nullptr)
    {
        hr = profileMgr->RegisterProfile(
            CLSID_BornoKeyTextService,
            kBanglaBangladeshLangId,
            GUID_BornoKeyProfile,
            kServiceDescription,
            static_cast<ULONG>(wcslen(kServiceDescription)),
            modulePath,
            static_cast<ULONG>(wcslen(modulePath)),
            0,
            nullptr,
            0,
            TRUE,
            0);
        profileMgr->Release();
    }

    ITfCategoryMgr* categoryMgr = nullptr;
    if (SUCCEEDED(CoCreateInstance(
        CLSID_TF_CategoryMgr,
        nullptr,
        CLSCTX_INPROC_SERVER,
        IID_ITfCategoryMgr,
        reinterpret_cast<void**>(&categoryMgr))) && categoryMgr != nullptr)
    {
        categoryMgr->RegisterCategory(CLSID_BornoKeyTextService, GUID_TFCAT_TIP_KEYBOARD, CLSID_BornoKeyTextService);
        categoryMgr->RegisterCategory(CLSID_BornoKeyTextService, GUID_TFCAT_TIPCAP_UIELEMENTENABLED, CLSID_BornoKeyTextService);
        categoryMgr->RegisterCategory(CLSID_BornoKeyTextService, GUID_TFCAT_TIPCAP_IMMERSIVESUPPORT, CLSID_BornoKeyTextService);
        categoryMgr->RegisterCategory(CLSID_BornoKeyTextService, GUID_TFCAT_TIPCAP_SYSTRAYSUPPORT, CLSID_BornoKeyTextService);
        categoryMgr->Release();
    }

    return hr;
}

STDAPI DllUnregisterServer(void)
{
    using namespace BornoKey::TSF;

    ITfInputProcessorProfileMgr* profileMgr = nullptr;
    if (SUCCEEDED(CoCreateInstance(
        CLSID_TF_InputProcessorProfiles,
        nullptr,
        CLSCTX_INPROC_SERVER,
        IID_ITfInputProcessorProfileMgr,
        reinterpret_cast<void**>(&profileMgr))) && profileMgr != nullptr)
    {
        profileMgr->UnregisterProfile(
            CLSID_BornoKeyTextService,
            kBanglaBangladeshLangId,
            GUID_BornoKeyProfile,
            0);
        profileMgr->Release();
    }

    wchar_t clsidStr[64]{};
    if (StringFromGUID2(CLSID_BornoKeyTextService, clsidStr, ARRAYSIZE(clsidStr)) != 0)
    {
        std::wstring regPath = L"SOFTWARE\\Classes\\CLSID\\";
        regPath += clsidStr;
        SHDeleteKeyW(HKEY_LOCAL_MACHINE, regPath.c_str());
    }

    return S_OK;
}
