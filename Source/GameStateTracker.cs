using System;
using System.Globalization;
using System.Reflection;
using BepInEx.Logging;

namespace valheimCLI
{
    public enum GameState
    {
        Unknown,
        MainMenu,
        Loading,
        InWorld,
        InWorldNoPlayer
    }

    public class GameStateTracker
    {
        private static readonly FieldInfo? GameRespawnWaitField = typeof(Game).GetField("m_respawnWait", BindingFlags.Instance | BindingFlags.NonPublic);

        private readonly ManualLogSource _logger;
        private GameState _currentState = GameState.Unknown;
        private GameState _previousState = GameState.Unknown;
        private string _currentStatusLine = "state=Unknown phase=unknown";

        public event Action<GameState, GameState>? OnStateChanged;

        public GameState CurrentState => _currentState;
        public GameState PreviousState => _previousState;
        public string CurrentStatusLine => _currentStatusLine;

        public GameStateTracker(ManualLogSource logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Call this from Update() to poll game state
        /// </summary>
        public void Update()
        {
            GameState newState = DetectCurrentState();
            _currentStatusLine = BuildStatusLine(newState);

            if (newState != _currentState)
            {
                _previousState = _currentState;
                _currentState = newState;
                _logger.LogInfo($"Game state changed: {_previousState} -> {_currentState}");
                OnStateChanged?.Invoke(_previousState, _currentState);
            }
        }

        private GameState DetectCurrentState()
        {
            // Check if we're in the main menu
            // FejdStartup is the main menu controller
            if (FejdStartup.instance != null && Game.instance == null)
            {
                return GameState.MainMenu;
            }

            // Check if Game exists (we're in a world)
            if (Game.instance != null)
            {
                // Check if ZNet is connecting
                if (ZNet.instance != null && ZNet.instance.InConnectingScreen())
                {
                    return GameState.Loading;
                }

                // Check if player is loaded
                if (Player.m_localPlayer != null)
                {
                    return GameState.InWorld;
                }

                // Game exists but no player yet (loading/respawning)
                return GameState.InWorldNoPlayer;
            }

            // During initial load or transition
            if (ZNet.instance != null && ZNet.instance.InConnectingScreen())
            {
                return GameState.Loading;
            }

            return GameState.Unknown;
        }

        private static string BuildStatusLine(GameState state)
        {
            string phase = DetectLoadPhase(state);
            bool gamePresent = Game.instance != null;
            bool mainMenuPresent = FejdStartup.instance != null && Game.instance == null;
            bool localPlayerPresent = Player.m_localPlayer != null;
            ZNet? znet = ZNet.instance;
            ZoneSystem? zoneSystem = ZoneSystem.instance;
            bool znetPresent = znet != null;
            bool znetConnecting = znet != null && znet.InConnectingScreen();
            bool zoneSystemPresent = zoneSystem != null;
            bool znetScenePresent = ZNetScene.instance != null;
            bool locationsGenerated = zoneSystem != null && zoneSystem.LocationsGenerated;
            float locationProgress = zoneSystem != null ? zoneSystem.GenerateLocationsProgress : 0f;
            float estimatedLocationSeconds = zoneSystem != null ? NormalizeEstimatedSeconds(zoneSystem.GetEstimatedGenerationCompletionTimeFromNow()) : -1f;
            int locationCount = zoneSystem != null ? zoneSystem.GetLocationList().Count : 0;
            bool activeAreaLoaded = false;
            if (zoneSystem != null && znet != null)
            {
                try
                {
                    activeAreaLoaded = zoneSystem.IsActiveAreaLoaded();
                }
                catch
                {
                    activeAreaLoaded = false;
                }
            }

            float respawnWait = 0f;
            if (Game.instance != null && GameRespawnWaitField != null)
            {
                object? value = GameRespawnWaitField.GetValue(Game.instance);
                if (value is float wait)
                {
                    respawnWait = wait;
                }
            }

            return "state=" + StateToString(state) +
                   " phase=" + phase +
                   " game=" + Bool(gamePresent) +
                   " mainMenu=" + Bool(mainMenuPresent) +
                   " localPlayer=" + Bool(localPlayerPresent) +
                   " znet=" + Bool(znetPresent) +
                   " znetConnecting=" + Bool(znetConnecting) +
                   " znetScene=" + Bool(znetScenePresent) +
                   " zoneSystem=" + Bool(zoneSystemPresent) +
                   " locationsGenerated=" + Bool(locationsGenerated) +
                   " locationProgress=" + locationProgress.ToString("F3", CultureInfo.InvariantCulture) +
                   " estimatedLocationSeconds=" + estimatedLocationSeconds.ToString("F1", CultureInfo.InvariantCulture) +
                   " locationCount=" + locationCount.ToString(CultureInfo.InvariantCulture) +
                   " activeAreaLoaded=" + Bool(activeAreaLoaded) +
                   " respawnWait=" + respawnWait.ToString("F1", CultureInfo.InvariantCulture);
        }

        private static string DetectLoadPhase(GameState state)
        {
            if (state == GameState.MainMenu)
            {
                return "main_menu";
            }

            if (state == GameState.InWorld)
            {
                return "ready";
            }

            if (ZNet.instance != null && ZNet.instance.InConnectingScreen())
            {
                return "connecting_screen";
            }

            if (Game.instance != null && Player.m_localPlayer == null)
            {
                if (ZoneSystem.instance != null && !ZoneSystem.instance.LocationsGenerated)
                {
                    return "generating_locations";
                }

                if (ZoneSystem.instance != null && ZNet.instance != null)
                {
                    try
                    {
                        if (!ZoneSystem.instance.IsActiveAreaLoaded())
                        {
                            return "loading_active_area";
                        }
                    }
                    catch
                    {
                        return "loading_active_area";
                    }
                }

                return "respawning";
            }

            if (state == GameState.Loading)
            {
                return "loading";
            }

            return "unknown";
        }

        private static float NormalizeEstimatedSeconds(float seconds)
        {
            if (float.IsNaN(seconds) || float.IsInfinity(seconds) || seconds < 0f || seconds > 86400f)
            {
                return -1f;
            }

            return seconds;
        }

        private static string Bool(bool value)
        {
            return value ? "true" : "false";
        }

        public static string StateToString(GameState state)
        {
            return state switch
            {
                GameState.Unknown => "Unknown",
                GameState.MainMenu => "MainMenu",
                GameState.Loading => "Loading",
                GameState.InWorld => "InWorld",
                GameState.InWorldNoPlayer => "InWorldNoPlayer",
                _ => "Unknown"
            };
        }

        public static GameState StringToState(string state)
        {
            return state.ToLowerInvariant() switch
            {
                "unknown" => GameState.Unknown,
                "mainmenu" => GameState.MainMenu,
                "loading" => GameState.Loading,
                "inworld" => GameState.InWorld,
                "inworldnoplayer" => GameState.InWorldNoPlayer,
                _ => GameState.Unknown
            };
        }
    }
}
