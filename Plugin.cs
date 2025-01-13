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
using Newtonsoft.Json;

namespace ZtreamerBot
{
    [BepInPlugin(pluginGUID, pluginName, pluginVersion)]
    public class Plugin : BaseUnityPlugin
    {
        public const string pluginGUID = "com.metalted.zeepkist.Ztreamerbot";
        public const string pluginName = "Ztreamerbot";
        public const string pluginVersion = "1.0";

        public static Plugin Instance;
        public Action<string> StreamerBotUDPAction;

        public ConfigEntry<int> udpPort;

        private void Awake()
        {
            udpPort = Config.Bind("Settings", "UDP Port", 12345, "The port to listen on. Should match the port defined in the UDP Broadcast Sub-Action from Streamerbot. Restart required to apply.");

            Thread udpListenerThread = new Thread(() => StartUdpListener(udpPort.Value));
            udpListenerThread.IsBackground = true;
            udpListenerThread.Start();

            Instance = this;

            ScriptingApi.RegisterEvent<OnStreamerbotUDPEvent>();
            ScriptingApi.RegisterType<Dictionary<string, object>>();

            Logger.LogInfo($"Plugin {pluginGUID} is loaded!");
            Logger.LogInfo($"UDP listener thread started on port {udpPort}.");
        }

        private void StartUdpListener(int port)
        {
            using (UdpClient udpClient = new UdpClient(AddressFamily.InterNetwork))
            {
                IPEndPoint remoteEndpoint = new IPEndPoint(IPAddress.Parse("0.0.0.0"), port);

                try
                {
                    udpClient.Client.Bind(remoteEndpoint);
                    udpClient.EnableBroadcast = true;

                    while (true)
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
                    Logger.LogError($"SocketException: {ex.Message}");
                }
                catch (Exception ex)
                {
                    Logger.LogError($"Exception: {ex.Message}");
                }
            }
        }

        public Dictionary<string, object> ParseUDPPayload(string json)
        {
            return JsonConvert.DeserializeObject<Dictionary<string, object>>(json);
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
