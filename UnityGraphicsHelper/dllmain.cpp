#include "pch.h"
#include "framework.h"

#include <d3d11.h>
#include <d3d11_1.h>
#include <fstream>
#include <string>
#include <chrono>
#include <iomanip>
#include <sstream>
#include <windows.h>

#include "IUnityInterface.h"
#include "IUnityGraphics.h"
#include "IUnityGraphicsD3D11.h"

#ifndef E_FAIL
#define E_FAIL 0x80004005
#endif

static ID3D11Device* g_D3D11Device = nullptr;
static ID3D11DeviceContext* s_ImmediateContext = nullptr;

extern "C" __declspec(dllexport) void UnityPluginLoad(IUnityInterfaces* unityInterfaces)
{
    IUnityGraphics* graphics = unityInterfaces->Get<IUnityGraphics>();
    if (graphics && graphics->GetRenderer() == kUnityGfxRendererD3D11)
    {
        IUnityGraphicsD3D11* d3d11 = unityInterfaces->Get<IUnityGraphicsD3D11>();
        if (d3d11)
        {
            g_D3D11Device = d3d11->GetDevice();
            if (g_D3D11Device)
            {
                g_D3D11Device->GetImmediateContext(&s_ImmediateContext);
            }
        }
    }
}

extern "C" __declspec(dllexport) void UnityPluginUnload()
{
    if (s_ImmediateContext)
    {
        s_ImmediateContext->Release();
        s_ImmediateContext = nullptr;
    }
    g_D3D11Device = nullptr;
}

extern "C" __declspec(dllexport) void SetDevicePointerFromCSharp(void* deviceFromCSharp)
{
    ID3D11Device* newDevice = static_cast<ID3D11Device*>(deviceFromCSharp);
    if (newDevice != g_D3D11Device)
    {
        if (s_ImmediateContext)
        {
            s_ImmediateContext->Release();
            s_ImmediateContext = nullptr;
        }
        g_D3D11Device = newDevice;
        if (g_D3D11Device)
        {
            g_D3D11Device->GetImmediateContext(&s_ImmediateContext);
        }
    }
}

extern "C" __declspec(dllexport) void* GetD3D11Device()
{
    return g_D3D11Device;
}

extern "C" __declspec(dllexport) void* GetDeviceFromResource(void* pResource)
{
    if (!pResource) return nullptr;
    ID3D11Resource* d3d11Resource = static_cast<ID3D11Resource*>(pResource);
    ID3D11Device* d3d11Device = nullptr;
    d3d11Resource->GetDevice(&d3d11Device);
    return d3d11Device;
}

extern "C" __declspec(dllexport) void DirectCopyResource(void* pDest, void* pSrc)
{
    if (!s_ImmediateContext || !pDest || !pSrc) return;
    ID3D11Resource* pDestResource = static_cast<ID3D11Resource*>(pDest);
    ID3D11Resource* pSrcResource = static_cast<ID3D11Resource*>(pSrc);
    s_ImmediateContext->CopyResource(pDestResource, pSrcResource);
}

extern "C" __declspec(dllexport) HRESULT CreateAndRegisterSRV(void* pTextureResource, int srvFormatDXGI, void** ppSRV)
{
    if (!g_D3D11Device || !pTextureResource) 
    {
        if (ppSRV) *ppSRV = nullptr;
        return E_FAIL;
    }
    
    ID3D11Texture2D* pTexture2D = static_cast<ID3D11Texture2D*>(pTextureResource);
    D3D11_TEXTURE2D_DESC texDesc;
    pTexture2D->GetDesc(&texDesc);

    D3D11_SHADER_RESOURCE_VIEW_DESC srvDesc = {};
    srvDesc.Format = static_cast<DXGI_FORMAT>(srvFormatDXGI);
    srvDesc.ViewDimension = D3D11_SRV_DIMENSION_TEXTURE2D;
    srvDesc.Texture2D.MostDetailedMip = 0;
    srvDesc.Texture2D.MipLevels = (texDesc.MipLevels == 0) ? -1 : texDesc.MipLevels;

    ID3D11ShaderResourceView* pNewSRV = nullptr;
    HRESULT hr = g_D3D11Device->CreateShaderResourceView(pTexture2D, &srvDesc, &pNewSRV);

    if (SUCCEEDED(hr)) 
    {
        if (ppSRV) *ppSRV = pNewSRV;
    } 
    else 
    {
        if (ppSRV) *ppSRV = nullptr;
    }
    return hr;
}

extern "C" __declspec(dllexport) void ReleaseNativeObject(void* pObject)
{
    if (pObject)
    {
        ((IUnknown*)pObject)->Release();
    }
}