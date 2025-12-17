using System.Runtime.InteropServices;
using SwiftlyS2.Shared.Natives;

namespace AddonsManager.Structs;

public enum HostStateRequestType_t : int
{
    HSR_IDLE = 1,
    HSR_GAME,
    HSR_SOURCETV_RELAY,
    HSR_QUIT
};

public enum HostStateRequestMode_t : int
{
    HM_LEVEL_LOAD_SERVER = 1,
    HM_CONNECT,
    HM_CHANGE_LEVEL,
    HM_LEVEL_LOAD_LISTEN,
    HM_LOAD_SAVE,
    HM_PLAY_DEMO,
    HM_SOURCETV_RELAY,
    HM_ADDON_DOWNLOAD
};

[StructLayout(LayoutKind.Sequential)]
public unsafe struct CHostStateRequest
{
    public HostStateRequestType_t Type;
    public CUtlString LoopModeType;
    public CUtlString Desc;
    public byte Active;
    public uint ID;
    public HostStateRequestMode_t Mode;
    public CUtlString LevelName;
    public byte Changelevel;
    public CUtlString SaveGame;
    public CUtlString Address;
    public CUtlString DemoFile;
    public byte LoadMap;
    public CUtlString Addons;
    public KeyValues* pKV;
}