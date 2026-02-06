using System;
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
        private readonly ManualLogSource _logger;
        private GameState _currentState = GameState.Unknown;
        private GameState _previousState = GameState.Unknown;

        public event Action<GameState, GameState>? OnStateChanged;

        public GameState CurrentState => _currentState;
        public GameState PreviousState => _previousState;

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
