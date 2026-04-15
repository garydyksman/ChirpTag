using IOD.CaptureTheFlag.NanoFramework.Enum;
using IOD.CaptureTheFlag.NanoFramework.Interfaces;
using IOD.CaptureTheFlag.NanoFramework.Types;
using System.Diagnostics;

namespace IOD.CaptureTheFlag.NanoFramework.Implementations
{
    public class MockGameHttpClient : IGameHttpClient
    {
        // ---------------------------------------------------------------
        // Toggle these to simulate different game states during development
        // None    → device shows "No game available"
        // Waiting → device registers and shows "Waiting for players"
        // Active  → device starts game with full player list
        // ---------------------------------------------------------------
        private static GameStatus _gameStatus = GameStatus.Active;

        private static readonly PlayerInfo[] MockPlayers = new PlayerInfo[]
        {
            new PlayerInfo { DeviceId = 0x01, Name = "GaryD",  Team = "A", CombatScore = 5, EnemyFlagId = 0x02 },
            new PlayerInfo { DeviceId = 0x02, Name = "Sarah", Team = "B", CombatScore = 3, EnemyFlagId = 0x01 },
            new PlayerInfo { DeviceId = 0x03, Name = "Bob",   Team = "A", CombatScore = 4, EnemyFlagId = 0x02 },
            new PlayerInfo { DeviceId = 0x04, Name = "Lisa",  Team = "B", CombatScore = 2, EnemyFlagId = 0x01 },
        };

        public GameInfo GetCurrentGame()
        {
            return new GameInfo
            {
                Status = _gameStatus,
                Players = _gameStatus == GameStatus.Active
                    ? MockPlayers
                    : new PlayerInfo[0],

            };
        }

        public PlayerSetup Register(string playerName)
        {
            foreach (PlayerInfo player in MockPlayers)
                if (player.Name == playerName)
                    return new PlayerSetup { DeviceId = player.DeviceId };

            // Unknown player — assign next available ID
            return new PlayerSetup { DeviceId = (byte)(MockPlayers.Length + 1) };
        }

        public void ReportCombat(byte winnerId, byte loserId, bool loserHadKey)
        {
            DebugLog.Write($"[Mock] Combat: winner={winnerId} loser={loserId} hadKey={loserHadKey}");
        }

        public bool ReportDeliver(byte deviceId, byte[] key)
        {
            DebugLog.Write($"[Mock] Deliver: deviceId={deviceId}");
            return true;
        }

        public byte GetRespawnNumber(byte deviceId)
        {
            return (byte)((deviceId % 5) + 1);
        }
    }
}
