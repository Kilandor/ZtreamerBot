using BepInEx;
using BepInEx.Configuration;
using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using ZeepSDK.Scripting.ZUA;
using ZeepSDK.Scripting;
using System.Collections.Generic;
using HarmonyLib;
using Newtonsoft.Json;
using ZeepSDK.Messaging;

namespace ZtreamerBot
{
    [BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
    public class Plugin : BaseUnityPlugin
    {
        private Harmony harmony;
        public static Plugin Instance;
        public Action<string> StreamerBotUDPAction;
        public Thread udpListenerThread;
        public UdpClient udpClient;
        public CancellationTokenSource udpCancellationToken;
        public bool isAutoStarted = false;

        public ConfigEntry<int> udpPort;
        public ConfigEntry<bool> modEnabled;
        public ConfigEntry<bool> autoStart;

        public static ConfigEntry<bool> startServer;
        public static ConfigEntry<bool> stopServer;
        public ConfigEntry<bool> debugEnabled;

        private void Awake()
        {
            Instance = this;
            ConfigSetup();
            
            harmony = new Harmony(MyPluginInfo.PLUGIN_GUID);
            harmony.PatchAll();

            ScriptingApi.RegisterEvent<OnStreamerbotUDPEvent>();
            ScriptingApi.RegisterType<Dictionary<string, object>>();

            Logger.LogInfo($"Plugin {MyPluginInfo.PLUGIN_GUID} is loaded!");
        }
        
        private void OnDestroy()
        {
            harmony?.UnpatchSelf();
            harmony = null;
        }

        private void ConfigSetup()
        {
            modEnabled = Config.Bind("1. General", "Plugin Enabled", true, "Is the plugin currently enabled?");
            modEnabled.SettingChanged += this.ModEnabled;
            udpPort = Config.Bind("1. General", "UDP Port", 12345,
                "The port to listen on. Should match the port defined in the UDP Broadcast Sub-Action from Streamerbot. Restart required to apply.");
            autoStart = Config.Bind("1. General", "Auto-Start when launching zeepkist", true,
                "Auto-start server when when the game is starting");


            startServer = Config.Bind("2. Server Controls", "Start Server", false, "[Button] Starts the server.");
            startServer.SettingChanged += this.StartServer;
            stopServer = Config.Bind("2. Server Controls", "Stop Server", false, "[Button] Stops the server.");
            stopServer.SettingChanged += this.StopServer;

            //debugEnabled = Config.Bind("9. Dev / Debug", "Debug Logs", false, "Provides extra output in logs for troubleshooting.");

        }

        public void ModEnabled(object sender, EventArgs e)
        {
            if (!Plugin.Instance.modEnabled.Value)
            {
                Logger.LogInfo($"Stopping server due to configuraiton being disabled.");
                //Trick to cause the settingchange event to fire
                stopServer.Value = !stopServer.Value;
            }
        }

        public void StartServer(object sender, EventArgs e)
        {
            if (!Plugin.Instance.modEnabled.Value)
                return;
            if (isAutoStarted)
            {
                Logger.LogInfo($"UDP Listener auto-started.");
                isAutoStarted = false;
            }

            if (udpClient != null)
            {
                MessengerApi.LogError("[Ztreamerbot] UDP Listener is already running.");
                Logger.LogInfo($"UDP Listener is already running.");
                return;
            }
                
            udpCancellationToken = new CancellationTokenSource();
            udpListenerThread = new Thread(() => StartUdpListener(udpPort.Value, udpCancellationToken.Token));
            udpListenerThread.IsBackground = true;
            udpListenerThread.Start();
            
            MessengerApi.LogSuccess("[Ztreamerbot] UDP Listener started.");
            Logger.LogInfo($"UDP listener thread started on port {udpPort.Value}.");
            
        }
        
        public void StopServer(object sender, EventArgs e)
        {
            if (udpClient == null)
            {
                MessengerApi.LogError("[Ztreamerbot] UDP Listener is not running.");
                Logger.LogInfo($"UDP Listener is not running.");
                return;
            }

            if (udpCancellationToken != null)
            {
                udpCancellationToken.Cancel();
                udpListenerThread?.Join(); // Wait for thread to finish
                udpCancellationToken.Dispose();
                udpCancellationToken = null;
            }

            udpClient?.Close();
            udpClient = null;
            
            MessengerApi.LogSuccess("[Ztreamerbot] UDP Listener stopped.");
            Logger.LogInfo($"UDP listener thread stopped.");
            
        }

        private void StartUdpListener(int port, CancellationToken cancellationToken)
        {
            udpClient = new UdpClient(AddressFamily.InterNetwork);
            IPEndPoint remoteEndpoint = new IPEndPoint(IPAddress.Parse("0.0.0.0"), port);

            try
            {
                udpClient.Client.Bind(remoteEndpoint);
                udpClient.EnableBroadcast = true;

                while (!cancellationToken.IsCancellationRequested)
                {
                    if (udpClient.Available > 0)
                    {
                        byte[] receivedBytes = udpClient.Receive(ref remoteEndpoint);
                        string receivedMessage = Encoding.UTF8.GetString(receivedBytes);

                        StreamerBotUDPAction?.Invoke(receivedMessage);
                    }

                    Thread.Sleep(10);
                }
            }
            catch (SocketException ex)
            {
                MessengerApi.LogError("[Ztreamerbot] SocketException", 5);
                Logger.LogError($"SocketException: {ex.Message}");
            }
            catch (Exception ex)
            {
                //MessengerApi.LogError("[Ztreamerbot] Exception", 5);
                Logger.LogError($"Exception: {ex.Message}");
            }
            finally
            {
                udpClient?.Close();
                udpClient = null;
                Logger.LogInfo("UDP listener stopped.");
            }
        }

        public Dictionary<string, object> ParseUDPPayload(string json)
        {
            try
            {
                return JsonConvert.DeserializeObject<Dictionary<string, object>>(json);
            }
            catch (JsonException ex)
            {
                Logger.LogError($"JSON parse error: {ex.Message}\nPayload: {json}");
            }
            catch (Exception ex)
            {
                Logger.LogError($"Unexpected error while parsing UDP payload: {ex.Message}");
            }

            return new Dictionary<string, object>();
        }
        
        [HarmonyPatch(typeof(MainMenuUI), nameof(MainMenuUI.Awake))]
        public class SetupMainMenuUIAwake
        {
            public static void Prefix()
            {
                if (Plugin.Instance.modEnabled.Value && Plugin.Instance.autoStart.Value)
                {
                    Plugin.Instance.isAutoStarted = true;
                    //Trick to cause the settingchange event to fire
                    startServer.Value = !startServer.Value;
                    //Logger.LogInfo($"UDP Listener auto-started.");
                }
            }
        }
    }

    public class OnStreamerbotUDPEvent : ILuaEvent
    {
        public string Name => "Streamerbot_OnUDP";

        private Action<string> _streambotUDPAction;

        public void Subscribe()
        {
            _streambotUDPAction = jsonPayload =>
            {
                // Parse the JSON payload
                Dictionary<string, object> data = Plugin.Instance.ParseUDPPayload(jsonPayload);

                // Call Lua function with the Lua table
                ScriptingApi.CallFunction(Name, data);
            };

            // Assume you have a custom event to trigger this
            Plugin.Instance.StreamerBotUDPAction += _streambotUDPAction;
        }

        public void Unsubscribe()
        {
            if (_streambotUDPAction != null)
            {
                Plugin.Instance.StreamerBotUDPAction -= _streambotUDPAction;
                _streambotUDPAction = null;
            }
        }
    }
}
