using System;
using Unity.Netcode;
using UnityEngine;

namespace _Project.Scripts.Network
{
    [RequireComponent(typeof(ServerConnection))]
    [RequireComponent(typeof(ClientConnection))]
    public class ConnectionManager : MonoBehaviour
    {
        [SerializeField] private ServerConnection serverConnection;
        [SerializeField] private ClientConnection clientConnection;

        public string Status { get; private set; } = "Idle";
        public bool IsListening => NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;
        public bool IsHost => NetworkManager.Singleton != null && NetworkManager.Singleton.IsHost;
        public bool IsServer => NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer;
        public bool IsClient => NetworkManager.Singleton != null && NetworkManager.Singleton.IsClient;
        public int ConnectedClients =>
            NetworkManager.Singleton != null ? NetworkManager.Singleton.ConnectedClientsIds.Count : 0;

        private void Awake()
        {
            if (serverConnection == null) serverConnection = GetComponent<ServerConnection>();
            if (clientConnection == null) clientConnection = GetComponent<ClientConnection>();
        }

        private void Start()
        {
            if (IsDedicatedServer())
            {
                ConfigureServerLoop();
                Status = serverConnection.StartServer();
            }
        }

        public string StartHost(ushort port) => Status = clientConnection.StartHost(port);

        public string StartServer(ushort port) => Status = serverConnection.StartServer(port);

        public string StartClient(string host, ushort port) => Status = clientConnection.StartClient(host, port);

        public void Shutdown()
        {
            if (NetworkManager.Singleton != null)
            {
                NetworkManager.Singleton.Shutdown();
            }

            Status = "Idle";
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

        private static bool IsDedicatedServer()
        {
#if UNITY_SERVER && !UNITY_EDITOR
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

        public static ushort ParsePort(string text)
        {
            return ushort.TryParse(text, out ushort port) ? port : ConnectionBase.DefaultPort;
        }
    }
}
