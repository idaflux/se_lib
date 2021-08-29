#define UNICODE
#define _UNICODE

#include <Windows.h>
#include <metahost.h>
#pragma comment(lib, "mscoree.lib")
#include <comdef.h>
#include <tchar.h>

struct LoadInfo {
	wchar_t* runtime;
	wchar_t* libFilePath;
	wchar_t* libFullTypeName;
	wchar_t* libMethodName;
};

extern "C" __declspec(dllexport) DWORD WINAPI RunManagedDll(LoadInfo* pli) {
	ICLRRuntimeHost* pClrHost = NULL;
	ICLRRuntimeInfo* pClrRuntimeInfo = NULL;
	ICLRMetaHost* pMetaHost = NULL;
	HRESULT hr;

	hr = CLRCreateInstance(CLSID_CLRMetaHost, IID_ICLRMetaHost, (LPVOID*)&pMetaHost);
	if (FAILED(hr)) {
		_com_error err(hr);
		MessageBox(NULL, err.ErrorMessage(), L"CLRCreateInstance", MB_OK);
		return -1;
	}

	hr = pMetaHost->GetRuntime(pli->runtime, IID_ICLRRuntimeInfo, (LPVOID*)&pClrRuntimeInfo);
	if (FAILED(hr)) {
		_com_error err(hr);
		MessageBox(NULL, err.ErrorMessage(), L"pMetaHost->GetRuntime", MB_OK);
		return -1;
	}

	hr = pClrRuntimeInfo->GetInterface(CLSID_CLRRuntimeHost, IID_ICLRRuntimeHost, (LPVOID*)&pClrHost);
	if (FAILED(hr)) {
		_com_error err(hr);
		MessageBox(NULL, err.ErrorMessage(), L"pClrRuntimeInfo->GetInterface", MB_OK);
		return -1;
	}

	hr = pClrHost->Start();
	if (FAILED(hr)) {
		_com_error err(hr);
		MessageBox(NULL, err.ErrorMessage(), L"pClrHost->Start", MB_OK);
		return -1;
	}

	pMetaHost->Release();
	pClrRuntimeInfo->Release();

	DWORD dwRet = 0;
	hr = pClrHost->ExecuteInDefaultAppDomain(pli->libFilePath, pli->libFullTypeName, pli->libMethodName, L"", &dwRet);
	if (FAILED(hr)) {
		_com_error err(hr);
		MessageBox(NULL, err.ErrorMessage(), L"pClrHost->ExecuteInDefaultAppDomain", MB_OK);
		return -1;
	}

	pClrHost->Release();

	return 0;
}

BOOL WINAPI DllMain(HINSTANCE module_handle, DWORD reason_for_call, LPVOID reserved) {
	return TRUE;
}
