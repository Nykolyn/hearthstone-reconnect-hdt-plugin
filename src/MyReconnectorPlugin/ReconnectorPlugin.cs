using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Hearthstone_Deck_Tracker.API;
using Hearthstone_Deck_Tracker.Plugins;
using Hearthstone_Deck_Tracker.Utility.Logging;
using ReconnectorCore;

namespace MyReconnector
{
    public class ReconnectorPlugin : IPlugin
    {
        public string Name => "My Reconnector";

        public string Description =>
            "Instantly disconnects Hearthstone from the server so it reconnects to the match in progress. " +
            "Useful for skipping Battlegrounds combat animations.\n\n" +
            "HDT must run as administrator for the disconnect to work.";

        public string ButtonText => "No settings";

        public string Author => "Nykolyn";

        /// <summary>
        /// Comes from &lt;Version&gt; in Directory.Build.props, the single place a release is numbered.
        /// </summary>
        public Version Version { get; } = typeof(ReconnectorPlugin).Assembly.GetName().Version;

        public MenuItem MenuItem { get; private set; }

        /// <summary>Matches the standalone app; repeated reconnects in one match can crash the game.</summary>
        private static readonly TimeSpan Cooldown = TimeSpan.FromSeconds(4);

        private PluginConfig _config;
        private ReconnectWindow _button;
        private GlobalHotkey _hotkey;
        private MenuItem _showButtonItem;
        private MenuItem _unlockItem;
        private MenuItem _hotkeyItem;
        private DateTime _lastReconnectUtc = DateTime.MinValue;
        private bool _warnedNotElevated;
        private string _reportedPrimerError;
        private string _reportedPrimedRange;

        public void OnLoad()
        {
            _config = PluginConfig.Load();
            CreateMenu();

            // HDT already knows where Hearthstone is installed; the locator falls back to the
            // running process if this is empty. Config is fully qualified on purpose — importing
            // the Hearthstone_Deck_Tracker namespace would make `Core` ambiguous with API.Core.
            try
            {
                GameServerLocator.HearthstoneDirectoryHint =
                    Hearthstone_Deck_Tracker.Config.Instance.HearthstoneDirectory;
            }
            catch (Exception ex) { Log.Warn("My Reconnector: could not read Hearthstone directory — " + ex.Message); }

            if (_config.ShowButton)
                AttachButton();

            if (_config.EnableHotkey)
                AttachHotkey();

            if (!Reconnect.IsElevated())
                Log.Warn("My Reconnector: HDT is not running as administrator — reconnect will not work.");
        }

        public void OnUnload()
        {
            DetachButton();
            DetachHotkey();
            _config?.Save();
        }

        private void AttachHotkey()
        {
            if (_hotkey != null)
                return;

            _hotkey = new GlobalHotkey();
            _hotkey.Pressed += DoReconnect;

            if (!_hotkey.Register())
            {
                Log.Warn("My Reconnector: could not register " + _hotkey.Description +
                         " — another program already owns it (the standalone HsReconnector.exe " +
                         "registers the same combination). The overlay button still works.");
                if (_hotkeyItem != null)
                    _hotkeyItem.Header = "Hotkey " + _hotkey.Description + " (unavailable — in use)";
            }
            else
            {
                Log.Info("My Reconnector: " + _hotkey.Description + " registered.");
            }
        }

        private void DetachHotkey()
        {
            if (_hotkey == null)
                return;
            _hotkey.Pressed -= DoReconnect;
            _hotkey.Dispose();
            _hotkey = null;
        }

        public void OnButtonPress()
        {
        }

        public void OnUpdate()
        {
            try
            {
                bool hsRunning = Core.Game != null && Core.Game.IsRunning;

                // Keep the game-server address warm in the background so the click itself does no
                // disk I/O — that latency lands straight on the reconnect. This also primes the
                // reverse-DNS entry that otherwise costs ~15s on every connect, which is why it
                // runs whenever Hearthstone is up rather than only during a match: priming a new
                // range early is what keeps the *next* connect fast. Deliberately ahead of the
                // button handling below, so it still runs with the overlay button switched off.
                if (hsRunning)
                {
                    GameServerLocator.BeginRefresh();
                    ReportPrimerState();
                }

                if (_button == null)
                    return;

                // Visible while unlocked (so it can be positioned) or during a match.
                bool inMatch = hsRunning && !Core.Game.IsInMenu;
                bool show = _button.Unlocked || inMatch;

                // Show()/Hide() rather than Visibility: a hidden top-level window still sits in
                // the z-order and can steal clicks from the game.
                if (show && !_button.IsVisible)
                    _button.Show();
                else if (!show && _button.IsVisible)
                    _button.Hide();

                if (show)
                    _button.UpdatePosition(ReconnectWindow.FindHearthstoneWindow());
            }
            catch (Exception ex)
            {
                Log.Error(ex);
            }
        }

        private void CreateMenu()
        {
            MenuItem = new MenuItem { Header = "My Reconnector" };

            _showButtonItem = new MenuItem
            {
                Header = "Show overlay button",
                IsCheckable = true,
                IsChecked = _config.ShowButton
            };
            _showButtonItem.Checked += (s, e) =>
            {
                _config.ShowButton = true;
                _config.Save();
                AttachButton();
                WarnIfNotElevated();
            };
            _showButtonItem.Unchecked += (s, e) =>
            {
                _config.ShowButton = false;
                _config.Save();
                DetachButton();
            };

            _unlockItem = new MenuItem
            {
                Header = "Unlock button position (drag to move)",
                IsCheckable = true
            };
            _unlockItem.Checked += (s, e) => { if (_button != null) _button.Unlocked = true; };
            _unlockItem.Unchecked += (s, e) =>
            {
                if (_button != null)
                    _button.Unlocked = false;
                _config.Save();
            };

            _hotkeyItem = new MenuItem
            {
                Header = "Hotkey Ctrl+F12 (works in game)",
                IsCheckable = true,
                IsChecked = _config.EnableHotkey
            };
            _hotkeyItem.Checked += (s, e) =>
            {
                _config.EnableHotkey = true;
                _config.Save();
                AttachHotkey();
            };
            _hotkeyItem.Unchecked += (s, e) =>
            {
                _config.EnableHotkey = false;
                _config.Save();
                DetachHotkey();
            };

            var reconnectNow = new MenuItem { Header = "Reconnect now" };
            reconnectNow.Click += (s, e) => DoReconnect();

            MenuItem.Items.Add(_showButtonItem);
            MenuItem.Items.Add(_unlockItem);
            MenuItem.Items.Add(_hotkeyItem);
            MenuItem.Items.Add(new Separator());
            MenuItem.Items.Add(reconnectNow);
        }

        private void AttachButton()
        {
            if (_button != null)
                return;

            // Its own window, not Core.OverlayCanvas: HDT's overlay is click-through, so a
            // control parented there is visible but never clickable.
            _button = new ReconnectWindow(_config);
            _button.Clicked += DoReconnect;
            _button.PositionChanged += () => _config.Save();
            _button.UpdatePosition(ReconnectWindow.FindHearthstoneWindow());
        }

        private void DetachButton()
        {
            if (_button == null)
                return;
            _button.Close();
            _button = null;
        }

        private void DoReconnect()
        {
            // The overlay button greys itself out after a press, but the hotkey and the menu item
            // bypass that, so the guard lives here where every entry point passes through it.
            if (DateTime.UtcNow - _lastReconnectUtc < Cooldown)
                return;
            _lastReconnectUtc = DateTime.UtcNow;

            if (!Reconnect.IsElevated())
            {
                _button?.ShowStatus("RUN HDT AS ADMIN");
                WarnIfNotElevated();
                return;
            }

            // Prefer the exact game server connection (from Hearthstone's log); if it is unknown
            // Disconnect() falls back to the game-server port and never touches Battle.net.
            Task.Run(() =>
            {
                try
                {
                    string addr;
                    ushort port;
                    if (!GameServerLocator.TryGetCached(out addr, out port))
                        GameServerLocator.Scan(out addr, out port);

                    var result = Reconnect.Disconnect(addr, port);

                    Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        if (!result.HearthstoneRunning)
                            _button?.ShowStatus("HS NOT RUNNING");
                        else if (result.Success)
                            _button?.ShowStatus("RECONNECTING…");
                        else
                            _button?.ShowStatus(result.Error ?? "FAILED");
                    }));

                    if (result.Success)
                        Log.Info($"My Reconnector: closed {result.ClosedCount} connection(s) — {result.Target}");
                    else
                        Log.Warn($"My Reconnector: disconnect failed — {result.Error}");
                }
                catch (Exception ex)
                {
                    Log.Error(ex);
                }
            });
        }

        /// <summary>Logs the reverse-DNS priming outcome once, so a blocked hosts write is visible.</summary>
        private void ReportPrimerState()
        {
            if (ReverseDnsPrimer.LastError != null)
            {
                if (_reportedPrimerError == ReverseDnsPrimer.LastError)
                    return;
                _reportedPrimerError = ReverseDnsPrimer.LastError;
                Log.Warn("My Reconnector: could not write the hosts file (" + _reportedPrimerError +
                         ") — Hearthstone will keep stalling ~15s on connect. Antivirus often " +
                         "blocks hosts changes; allow it, or add the range by hand.");
                return;
            }

            var range = ReverseDnsPrimer.LastPrimedRange;
            if (range != null && range != _reportedPrimedRange)
            {
                _reportedPrimedRange = range;
                Log.Info("My Reconnector: primed reverse-DNS stubs for " + range +
                         " — connects no longer wait on the DNS timeout.");
            }
        }

        private void WarnIfNotElevated()
        {
            if (_warnedNotElevated || Reconnect.IsElevated())
                return;
            _warnedNotElevated = true;
            MessageBox.Show(
                "Hearthstone Deck Tracker is not running as administrator.\n\n" +
                "Closing TCP connections requires admin rights — restart HDT as administrator " +
                "for the reconnect button to work.",
                "My Reconnector", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
