using System;
using System.Threading;

namespace valheimCLI
{
    internal sealed class NativeSessionSave : ISessionSave
    {
        private readonly ZNet _net;
        private readonly World _world;
        private readonly Game _game;
        private Thread? _thread;
        public NativeSessionSave(ZNet net, World world, Game game) { _net = net; _world = world; _game = game; }
        public bool SameWorld => ReferenceEquals(ZNet.instance, _net) && ReferenceEquals(ZNet.World, _world) && ReferenceEquals(Game.instance, _game);
        public bool IsSaving => _net.IsSaving();
        public string SkipReason => SaveOutcome.SkipReason(
            SaveSystem.HasSessionFlag(SaveSystemSessionFlags.DontSaveWorld), ZNet.m_loadError,
            ZoneSystem.instance != null && ZoneSystem.instance.SkipSaving(),
            DungeonDB.instance != null && DungeonDB.instance.SkipSaving(), _net.EnoughDiskSpaceAvailable(out bool _));
        public uint SaveNumber => SaveSystem.GetSaveNumber();
        public bool Started => _thread != null;
        public bool Writing => _thread != null && _thread.IsAlive;
        public void Start()
        {
            Thread? previous = _net.m_saveThread;
            try
            {
                _game.SavePlayerProfile(setLogoutPoint: true);
                _net.Save(sync: false, saveOtherPlayerProfiles: true, waitForNextFrame: false);
            }
            finally { if (!ReferenceEquals(previous, _net.m_saveThread)) _thread = _net.m_saveThread; }
        }
    }
}
