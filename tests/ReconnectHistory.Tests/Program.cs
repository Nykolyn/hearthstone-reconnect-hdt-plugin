using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using HearthMirror.Objects;
using Hearthstone_Deck_Tracker.Hearthstone;
using Hearthstone_Deck_Tracker.Hearthstone.Entities;

internal static class Program
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static Type _history;
    private static readonly DateTime Now = new DateTime(2026, 10, 20, 12, 0, 0, DateTimeKind.Utc);

    private static int Main(string[] args)
    {
        AppDomain.CurrentDomain.AssemblyResolve += (s, e) =>
        {
            var name = new AssemblyName(e.Name).Name;
            foreach (var file in new[] { name + ".dll", name + ".exe", "Hearthstone Deck Tracker.exe" })
            {
                var path = Path.Combine(args[0], file);
                if (File.Exists(path) && AssemblyName.GetAssemblyName(path).Name == name)
                    return Assembly.LoadFrom(path);
            }
            return null;
        };
        try { Run(); return 0; }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }

    private static void Run()
    {
        _history = typeof(MyReconnector.ReconnectorPlugin).Assembly.GetType("MyReconnector.ReconnectHistory");
        Check(_history != null, "Reconnect must preserve opponent board history (implementation missing).");
        var game = (GameV2)FormatterServices.GetUninitializedObject(typeof(GameV2));
        var server = new GameServerInfo { Address = "server", Port = 3724, GameHandle = 123 };
        typeof(GameV2).GetField("<MetaData>k__BackingField", Private)
            .SetValue(game, new GameMetaData { ServerInfo = server });
        var matchId = _history.GetMethod("MatchId");
        Check((string)matchId.Invoke(null, new object[] { game }) == "server:3724:123",
            "Match identity must include server, port and game handle.");
        server.GameHandle = 0;
        Check(matchId.Invoke(null, new object[] { game }) == null, "Zero game handle is unknown.");
        server.GameHandle = 123;
        server.Address = "";
        Check(matchId.Invoke(null, new object[] { game }) == null, "Empty server address is unknown.");
        server.Address = "server";
        server.Port = 0;
        Check(matchId.Invoke(null, new object[] { game }) == null, "Zero port is unknown.");
        server.Port = 3724;
        var field = typeof(GameV2).GetField("_battlegroundsBoardState", Private);
        var board = Activator.CreateInstance(field.FieldType, new object[] { game });
        field.SetValue(game, board);
        var states = (Dictionary<int, BoardSnapshot>)field.FieldType
            .GetProperty("LastKnownBoardState", Private).GetValue(board);
        var reset = field.FieldType.GetMethod("Reset");
        var original = new BoardSnapshot(new Entity[0], 5);
        states[3] = original;
        var capture = Capture(game, "server:3724:123");
        reset.Invoke(board, null);
        Restore(capture, game, "server:3724:123", Now.AddSeconds(1));
        Check(states.ContainsKey(3) && states[3].Equals(original), "Same match restores the cleared board.");

        capture = Capture(game, "server:3724:123");
        reset.Invoke(board, null);
        Restore(capture, game, "server:3724:123", Now.AddSeconds(2));
        Check(states.ContainsKey(3), "A second reconnect retains the first restored history.");

        capture = Capture(game, "server:3724:123");
        var newer = new BoardSnapshot(new Entity[0], 7);
        states[3] = newer;
        states[4] = newer;
        Restore(capture, game, "server:3724:123", Now.AddSeconds(1));
        Check(states[3].Equals(newer) && states.ContainsKey(4), "Restoration preserves fresh boards.");

        foreach (var id in new[] { "server:3724:124", "other:3724:123", null })
        {
            states[3] = original;
            capture = Capture(game, "server:3724:123");
            reset.Invoke(board, null);
            Restore(capture, game, id, Now.AddSeconds(1));
            Check(states.Count == 0, "Unknown or different match must not receive old boards.");
        }
        states[3] = original;
        capture = Capture(game, "server:3724:123");
        reset.Invoke(board, null);
        Restore(capture, game, "server:3724:123", Now.AddMinutes(3));
        Check(states.Count == 0, "Expired capture must not restore.");
        Check(Capture(game, null) == null, "Unknown match must not be captured.");
        var plugin = new MyReconnector.ReconnectorPlugin();
        var pending = plugin.GetType().GetField("_history", Private);
        pending.SetValue(plugin, Capture(game, "server:3724:123"));
        plugin.OnUnload();
        Check(pending.GetValue(plugin) == null, "Unload must discard pending history.");
        Console.WriteLine("PASS: match identity, same-match/repeated reconnect, fresh boards, different/unknown match, expiry, unload.");
    }

    private static object Capture(GameV2 game, string id) =>
        _history.GetMethod("Capture").Invoke(null, new object[] { game, id, Now });

    private static void Restore(object capture, GameV2 game, string id, DateTime now) =>
        _history.GetMethod("Restore").Invoke(capture, new object[] { game, id, now });

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
