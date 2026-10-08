using _Project.Scripts.Network;
using UnityEngine;
using UnityEngine.UI;

namespace _Project.Scripts.UI
{
    /// <summary>
    /// Mobile-friendly, Canvas-based replacement for the old IMGUI connection panel.
    /// Shows a full-screen setup card while offline, then collapses to a small
    /// top-right chip with a "Leave" button once a session is running.
    /// </summary>
    public class ConnectionUI : MonoBehaviour
    {
        [Header("Binding")]
        [SerializeField] private ConnectionManager connectionManager;

        [Header("Setup Screen")]
        [SerializeField] private GameObject connectionScreen;
        [SerializeField] private InputField hostInput;
        [SerializeField] private InputField portInput;
        [SerializeField] private Text statusText;
        [SerializeField] private Button hostButton;
        [SerializeField] private Button joinButton;
        [SerializeField] private Button serverButton;

        [Header("In-Session Chip")]
        [SerializeField] private GameObject disconnectChip;
        [SerializeField] private Text chipText;
        [SerializeField] private Button disconnectButton;

        [Header("Defaults")]
        [SerializeField] private string defaultHost = "127.0.0.1";
        [SerializeField] private string defaultPort = "7777";

        private void Awake()
        {
            if (connectionManager == null)
            {
                connectionManager = FindAnyObjectByType<ConnectionManager>();
            }

            if (hostInput != null && string.IsNullOrEmpty(hostInput.text)) hostInput.text = defaultHost;
            if (portInput != null && string.IsNullOrEmpty(portInput.text)) portInput.text = defaultPort;
        }

        private void OnEnable()
        {
            if (hostButton != null) hostButton.onClick.AddListener(OnHostClicked);
            if (joinButton != null) joinButton.onClick.AddListener(OnJoinClicked);
            if (serverButton != null) serverButton.onClick.AddListener(OnServerClicked);
            if (disconnectButton != null) disconnectButton.onClick.AddListener(OnDisconnectClicked);
        }

        private void OnDisable()
        {
            if (hostButton != null) hostButton.onClick.RemoveListener(OnHostClicked);
            if (joinButton != null) joinButton.onClick.RemoveListener(OnJoinClicked);
            if (serverButton != null) serverButton.onClick.RemoveListener(OnServerClicked);
            if (disconnectButton != null) disconnectButton.onClick.RemoveListener(OnDisconnectClicked);
        }

        private void Update()
        {
            if (connectionManager == null) return;

            bool listening = connectionManager.IsListening;

            if (connectionScreen != null) connectionScreen.SetActive(!listening);
            if (disconnectChip != null) disconnectChip.SetActive(listening);

            if (statusText != null) statusText.text = connectionManager.Status;

            if (listening && chipText != null)
            {
                string role = connectionManager.IsHost ? "Host"
                    : connectionManager.IsServer ? "Server"
                    : "Client";
                chipText.text = $"{role}  •  {connectionManager.ConnectedClients} player(s)";
            }
        }

        private void OnHostClicked()
        {
            if (connectionManager == null) return;
            connectionManager.StartHost(ReadPort());
        }

        private void OnJoinClicked()
        {
            if (connectionManager == null) return;
            string host = hostInput != null ? hostInput.text : defaultHost;
            connectionManager.StartClient(host, ReadPort());
        }

        private void OnServerClicked()
        {
            if (connectionManager == null) return;
            connectionManager.StartServer(ReadPort());
        }

        private void OnDisconnectClicked()
        {
            connectionManager?.Shutdown();
        }

        private ushort ReadPort()
        {
            string text = portInput != null ? portInput.text : defaultPort;
            return ConnectionManager.ParsePort(text);
        }
    }
}
