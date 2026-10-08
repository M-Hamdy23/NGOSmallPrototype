using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

namespace _Project.Scripts.Network
{
    public abstract class ConnectionBase : MonoBehaviour
    {
        public const ushort DefaultPort = 7777;

        protected bool TryConfigureTransport(string host, ushort port, out string error)
        {
            if (NetworkManager.Singleton == null)
            {
                error = "No NetworkManager in scene";
                return false;
            }

            if (NetworkManager.Singleton.IsListening)
            {
                error = "Already running - shutdown first";
                return false;
            }

            UnityTransport transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
            if (transport == null)
            {
                error = "No UnityTransport on NetworkManager";
                return false;
            }

            transport.SetConnectionData(host, port, "0.0.0.0");
            error = null;
            return true;
        }
    }
}
