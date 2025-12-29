using System.Runtime.InteropServices;
using SwiftlyS2.Shared.Natives;

namespace AddonsManager.Structs;

[StructLayout(LayoutKind.Explicit, Size = 104)]
public unsafe struct CHostStateRequest
{
    [FieldOffset(40)] public CUtlString LevelName;
    [FieldOffset(88)] public CUtlString Addons;
    [FieldOffset(96)] public KeyValues* pKV;
}