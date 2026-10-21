using System;
using System.Collections.Generic;
using System.Reflection;
using Hearthstone_Deck_Tracker.Hearthstone;

namespace MyReconnector
{
    internal sealed class ReconnectHistory
    {
        private readonly string _matchId;
        private readonly DateTime _capturedUtc;
        private readonly Dictionary<int, BoardSnapshot> _boards;

        private ReconnectHistory(string matchId, DateTime now, Dictionary<int, BoardSnapshot> boards)
        {
            _matchId = matchId;
            _capturedUtc = now;
            _boards = new Dictionary<int, BoardSnapshot>(boards);
        }

        public static string MatchId(GameV2 game)
        {
            var server = game?.MetaData?.ServerInfo;
            return server == null || server.GameHandle == 0 || string.IsNullOrEmpty(server.Address) || server.Port == 0
                ? null
                : server.Address + ":" + server.Port + ":" + server.GameHandle;
        }

        public static ReconnectHistory Capture(GameV2 game, string matchId, DateTime now)
        {
            return string.IsNullOrEmpty(matchId) ? null : new ReconnectHistory(matchId, now, Boards(game));
        }

        public int Restore(GameV2 game, string matchId, DateTime now)
        {
            if (matchId != _matchId || now - _capturedUtc > TimeSpan.FromMinutes(2))
                return 0;
            var boards = Boards(game);
            var restored = 0;
            foreach (var entry in _boards)
            {
                // Replayed log data may already contain a fresher board for this player.
                if (!boards.ContainsKey(entry.Key))
                {
                    boards.Add(entry.Key, entry.Value);
                    restored++;
                }
            }
            return restored;
        }

        private static Dictionary<int, BoardSnapshot> Boards(GameV2 game)
        {
            // HDT exposes board reads publicly, but no history export/import API.
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var state = typeof(GameV2).GetField("_battlegroundsBoardState", flags)?.GetValue(game);
            var boards = state?.GetType().GetProperty("LastKnownBoardState", flags)?.GetValue(state)
                as Dictionary<int, BoardSnapshot>;
            if (boards == null)
                throw new NotSupportedException("HDT's Battlegrounds board history API has changed.");
            return boards;
        }
    }
}
