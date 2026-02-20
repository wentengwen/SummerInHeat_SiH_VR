using System.Runtime.InteropServices;
using UnityVRMod.Core;

namespace UnityVRMod.Features.VRVisualization.OpenXR
{
    internal static class NativeBridge
    {
        private const string NativeHelperDll = "UnityGraphicsHelper";

        [DllImport(NativeHelperDll, CallingConvention = CallingConvention.Cdecl, EntryPoint = "DirectCopyResource")]
        public static extern void DirectCopyResource_Internal(IntPtr pDest, IntPtr pSrc);

        [DllImport(NativeHelperDll, CallingConvention = CallingConvention.Cdecl, EntryPoint = "SetDevicePointerFromCSharp")]
        public static extern void SetDevicePointerFromCSharp(IntPtr d3d11DevicePtr);

        [DllImport(NativeHelperDll, CallingConvention = CallingConvention.Cdecl, EntryPoint = "CreateAndRegisterSRV")]
        public static extern int CreateAndRegisterSRV_Internal(IntPtr pTextureResource, int srvFormatDXGI, out IntPtr ppSRV);

        [DllImport(NativeHelperDll, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ReleaseNativeObject")]
        public static extern void ReleaseNativeObject_Internal(IntPtr pObject);

        [DllImport(NativeHelperDll, CallingConvention = CallingConvention.Cdecl, EntryPoint = "GetD3D11Device")]
        private static extern IntPtr GetCachedD3D11Device_Internal();

        [DllImport(NativeHelperDll, CallingConvention = CallingConvention.Cdecl, EntryPoint = "GetDeviceFromResource")]
        private static extern IntPtr GetDeviceFromResource_Internal(IntPtr pResource);

        public static IntPtr GetD3D11DevicePointer(Texture textureForFallback)
        {
            if (SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Direct3D11)
            {
                VRModCore.LogWarning("Graphics device is not Direct3D 11.");
                return IntPtr.Zero;
            }

            try
            {
                IntPtr devicePtr = GetCachedD3D11Device_Internal();
                if (devicePtr != IntPtr.Zero) return devicePtr;
            }
            catch { }

            if (textureForFallback == null) return IntPtr.Zero;

            try
            {
                IntPtr nativeTexturePtr = textureForFallback.GetNativeTexturePtr();
                if (nativeTexturePtr == IntPtr.Zero) return IntPtr.Zero;
                return GetDeviceFromResource_Internal(nativeTexturePtr);
            }
            catch { }

            return IntPtr.Zero;
        }
    }
}
