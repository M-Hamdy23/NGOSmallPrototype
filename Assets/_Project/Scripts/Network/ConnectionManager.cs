using System;
using Unity.Netcode;
using UnityEngine;

namespace _Project.Scripts.Network
{
    [RequireComponent(typeof(ServerConnection))]
    [RequireComponent(typeof(ClientConnection))]
    public class ConnectionManager : MonoBehaviour
    {
        private string _host = "127.0.0.1";
        private string _port = "7777";
        private string _status = "Idle";
        [SerializeField] private ServerConnection serverConnection;
        [SerializeField] private ClientConnection clientConnection;

        private void Start()
        {
            if (serverConnection == null) serverConnection = gameObject.GetComponent<ServerConnection>();
            if (clientConnection == null) clientConnection = gameObject.GetComponent<ClientConnection>();

            if (IsDedicatedServer())
            {
                ConfigureServerLoop();
                _status = serverConnection.StartServer();
            }
        }

        /// <summary>
        /// A headless server's frame rate directly caps how often NetworkTransform states
        /// are sent: NGO samples authority transforms once per NetworkUpdate (once per
        /// frame), so a throttled server loop sends only a handful of updates per second
        /// and remote players step instead of moving smoothly. Keep the loop running
        /// while unfocused and running well above the network tick rate.
        /// </summary>
        private static void ConfigureServerLoop()
        {
            Application.runInBackground = true;
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = 120;
        }

        private void OnValidate()
        {
            if (serverConnection == null) serverConnection = gameObject.GetComponent<ServerConnection>();
            if (clientConnection == null) clientConnection = gameObject.GetComponent<ClientConnection>();
        }

        private void OnGUI()
        {
            GUILayout.BeginArea(new Rect(10, 10, 320, 300));
            GUILayout.Label("Connection");
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
                _status = clientConnection.StartHost(ParsePort(_port));
            }

            if (GUILayout.Button("Start Server"))
            {
                _status = serverConnection.StartServer(ParsePort(_port));
            }

            if (GUILayout.Button("Start Client"))
            {
                _status = clientConnection.StartClient(_host, ParsePort(_port));
            }

            GUILayout.EndArea();
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


        private static ushort ParsePort(string text)
        {
            return ushort.TryParse(text, out ushort port) ? port : ConnectionBase.DefaultPort;
        }
    }
}