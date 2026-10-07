using SwiftlyS2.Shared.Natives;

namespace AddonsManager.Structs;

public enum HostStateRequestType_t
{
    HSR_IDLE = 1,
    HSR_GAME,
    HSR_SOURCETV_RELAY,
    HSR_QUIT
};

public enum HostStateRequestMode_t
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

public unsafe struct CHostStateRequest
{
    public HostStateRequestType_t m_iType;
    public CUtlString m_LoopModeType;
    public CUtlString m_Desc;
    public bool m_bActive;
    public uint m_ID;
    public HostStateRequestMode_t m_iMode;
    public CUtlString m_LevelName;
    public bool m_bChangelevel;
    public CUtlString m_SaveGame;
    public CUtlString m_Address;
    public CUtlString m_DemoFile;
    public bool m_bLoadMap;
    public CUtlString m_Addons;
    public KeyValues* m_pKV;
};