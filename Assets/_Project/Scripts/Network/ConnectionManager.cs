using System;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

namespace CoreRush.Network
{
    public class ConnectionManager : MonoBehaviour
    {
        private string _host = "127.0.0.1";
        private string _port = "7777";
        private string _status = "Idle";

        private void Start()
        {
            if (IsDedicatedServer())
            {
                ushort serverPort = ReadServerPort();
                StartServer(serverPort);
            }
        }

        private void OnGUI()
        {
            GUILayout.BeginArea(new Rect(10, 10, 320, 300));
            GUILayout.Label("Core Rush - Connection");
            GUILayout.Label("Status: " + _status);

            if (NetworkManager.Singleton != null &&
                (NetworkManager.Singleton.IsServer || NetworkManager.Singleton.IsClient))
            {
                GUILayout.Label("Connected as " +
                    (NetworkManager.Singleton.IsHost ? "Host" : NetworkManager.Singleton.IsServer ? "Server" : "Client") +
                    "  Clients: " + NetworkManager.Singleton.ConnectedClientsIds.Count);
                if (GUILayout.Button("Shutdown"))
                {
                    NetworkManager.Singleton.Shutdown();
                    _status = "Idle";
                }
                GUILayout.EndArea();
                return;
            }

            GUILayout.Label("Host:");
            _host = GUILayout.TextField(_host);
            GUILayout.Label("Port:");
            _port = GUILayout.TextField(_port);

            if (GUILayout.Button("Start Host"))
            {
                StartHost(ParsePort(_port));
            }
            if (GUILayout.Button("Start Server"))
            {
                StartServer(ParsePort(_port));
            }
            if (GUILayout.Button("Start Client"))
            {
                StartClient(_host, ParsePort(_port));
            }
            GUILayout.EndArea();
        }

        private void StartHost(ushort port)
        {
            if (!ConfigureTransport("0.0.0.0", port)) return;
            if (NetworkManager.Singleton.StartHost())
            {
                _status = "Host running on port " + port;
            }
            else
            {
                _status = "Host failed";
            }
        }

        private void StartServer(ushort port)
        {
            if (!ConfigureTransport("0.0.0.0", port)) return;
            if (NetworkManager.Singleton.StartServer())
            {
                _status = "Server listening on port " + port;
            }
            else
            {
                _status = "Server failed";
            }
        }

        private void StartClient(string host, ushort port)
        {
            if (!ConfigureTransport(host, port)) return;
            if (NetworkManager.Singleton.StartClient())
            {
                _status = "Connecting to " + host + ":" + port + "...";
            }
            else
            {
                _status = "Client failed";
            }
        }

        private bool ConfigureTransport(string host, ushort port)
        {
            if (NetworkManager.Singleton == null)
            {
                _status = "No NetworkManager in scene";
                return false;
            }
            if (NetworkManager.Singleton.IsListening)
            {
                _status = "Already running - shutdown first";
                return false;
            }
            UnityTransport transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
            if (transport == null)
            {
                _status = "No UnityTransport on NetworkManager";
                return false;
            }
            transport.SetConnectionData(host, port, "0.0.0.0");
            return true;
        }

        private static bool IsDedicatedServer()
        {
#if UNITY_SERVER
            return true;
#else
            if (Application.isBatchMode) return true;
            string[] args = Environment.GetCommandLineArgs();
            foreach (string arg in args)
            {
                if (arg == "--server") return true;
            }
            return false;
#endif
        }

        private static ushort ReadServerPort()
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == "-port" && ushort.TryParse(args[i + 1], out ushort argPort))
                {
                    return argPort;
                }
            }
            string envPort = Environment.GetEnvironmentVariable("PORT");
            if (ushort.TryParse(envPort, out ushort envResult))
            {
                return envResult;
            }
            return 7777;
        }

        private static ushort ParsePort(string text)
        {
            return ushort.TryParse(text, out ushort port) ? port : (ushort)7777;
        }
    }
}
