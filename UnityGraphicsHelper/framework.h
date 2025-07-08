// UnityGraphicsHelper.h
#pragma once

#include "IUnityInterface.h" // Main Unity plugin interface

// Function to be exported
extern "C" UNITY_INTERFACE_EXPORT void* UNITY_INTERFACE_API GetD3D11Device();