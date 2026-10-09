using System;
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

        /// <summary>
        /// Mobile keyboards and copy-pasted Edgegap endpoints commonly leave stray
        /// whitespace, a URL scheme, or an embedded port ("fqdn:port") in the address
        /// field. UnityTransport rejects any of those with a bare StartClient() == false,
        /// which surfaces as the generic "Client failed" status. Normalize the address
        /// and lift an embedded port back out before configuring the transport.
        /// </summary>
        protected static string SanitizeHost(string host, ref ushort port)
        {
            if (string.IsNullOrEmpty(host))
            {
                return host;
            }

            host = host.Trim();

            int scheme = host.IndexOf("://", StringComparison.Ordinal);
            if (scheme >= 0)
            {
                host = host.Substring(scheme + 3);
            }

            int path = host.IndexOf('/');
            if (path >= 0)
            {
                host = host.Substring(0, path);
            }

            // Leave bracketed IPv6 literals untouched; otherwise split "host:port".
            if (!host.StartsWith("[", StringComparison.Ordinal))
            {
                int colon = host.IndexOf(':');
                if (colon >= 0 && host.IndexOf(':', colon + 1) < 0)
                {
                    if (ushort.TryParse(host.Substring(colon + 1), out ushort embeddedPort))
                    {
                        port = embeddedPort;
                        host = host.Substring(0, colon);
                    }
                }
            }

            return host.Trim();
        }
    }
}
