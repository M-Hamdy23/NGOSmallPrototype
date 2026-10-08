using System;
using Unity.Netcode;

namespace _Project.Scripts.Network
{
    public class ServerConnection : ConnectionBase
    {
        public string StartServer()
        {
            ushort serverPort = ReadServerPort();
            return StartServer(serverPort);
        }

        private static ushort ReadServerPort()
        {
            string mapping = Environment.GetEnvironmentVariable("ARBITRIUM_PORTS_MAPPING");
            if (!string.IsNullOrEmpty(mapping))
            {
                ushort? mapped = ReadInternalPortFromMapping(mapping);
                if (mapped.HasValue)
                {
                    return mapped.Value;
                }
            }

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

            return ConnectionBase.DefaultPort;
        }

        // Edgegap injects e.g. {"ports":{"game-7777":{"name":"...","internal":7777,
        // "external":32512,"protocol":"UDP"}}} - the server must bind the internal port.
        private static ushort? ReadInternalPortFromMapping(string mapping)
        {
            if (string.IsNullOrEmpty(mapping))
            {
                return null;
            }

            const string key = "\"internal\":";
            int keyIndex = mapping.IndexOf(key, StringComparison.OrdinalIgnoreCase);
            if (keyIndex < 0)
            {
                return null;
            }

            int valueStart = keyIndex + key.Length;
            int valueEnd = valueStart;
            while (valueEnd < mapping.Length && (char.IsDigit(mapping[valueEnd]) ||
                                                 (valueEnd == valueStart && mapping[valueEnd] == '-')))
            {
                valueEnd++;
            }

            if (int.TryParse(mapping.Substring(valueStart, valueEnd - valueStart), out int port) &&
                port > 0 && port <= ushort.MaxValue)
            {
                return (ushort)port;
            }

            return null;
        }

        public string StartServer(ushort port)
        {
            if (!TryConfigureTransport("0.0.0.0", port, out string error)) return error;
            return NetworkManager.Singleton.StartServer()
                ? "Server listening on port " + port
                : "Server failed";
        }
    }
}