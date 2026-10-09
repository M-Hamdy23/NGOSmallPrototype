using Unity.Netcode;

namespace _Project.Scripts.Network
{
    public class ClientConnection : ConnectionBase
    {
        public string StartHost(ushort port)
        {
            if (!TryConfigureTransport("0.0.0.0", port, out string error)) return error;
            return NetworkManager.Singleton.StartHost()
                ? "Host running on port " + port
                : "Host failed";
        }

        public string StartClient(string host, ushort port)
        {
            host = SanitizeHost(host, ref port);

            if (string.IsNullOrEmpty(host))
            {
                return "Enter the server address (Edgegap fqdn or IP)";
            }

            if (!TryConfigureTransport(host, port, out string error)) return error;
            return NetworkManager.Singleton.StartClient()
                ? "Connecting to " + host + ":" + port + "..."
                : "Client failed - check address " + host + ":" + port;
        }
    }
}
