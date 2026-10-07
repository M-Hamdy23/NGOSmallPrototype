using System;
using System.Collections.Generic;
using _Project.Scripts.Player;
using Unity.Netcode;
using UnityEngine;

namespace _Project.Scripts.Game
{
    public class GameManager : NetworkBehaviour
    {
        public static GameManager Instance { get; private set; }

        [SerializeField] private int maxPlayers = 8;
        [SerializeField] private Transform redBase;
        [SerializeField] private Transform blueBase;
        [SerializeField] private Vector2 baseHalfExtents = new Vector2(3f, 7f);

        [SerializeField] private GameObject corePrefab;
        [SerializeField] private Transform[] redSpawns;
        [SerializeField] private Transform[] blueSpawns;
        [SerializeField] private Transform[] coreSpawns;


        public NetworkVariable<MatchState> matchStateNv = new NetworkVariable<MatchState>(MatchState.WaitingForPlayers);
        public NetworkVariable<int> redScore = new NetworkVariable<int>();
        public NetworkVariable<int> blueScore = new NetworkVariable<int>();

        private readonly Dictionary<ulong, NetworkPlayer> _players = new Dictionary<ulong, NetworkPlayer>();
        private int _redCount;
        private int _blueCount;
        private readonly Queue<(NetworkPlayer player, Vector3 position)> _pendingSpawns = new Queue<(NetworkPlayer, Vector3)>();

        private void Awake()
        {
            Instance = this;
        }

        public override void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public override void OnNetworkSpawn()
        {
            if (!IsServer) return;
            NetworkManager.Singleton.ConnectionApprovalCallback = ApproveConnection;
            NetworkManager.Singleton.OnClientDisconnectCallback += ServerOnClientDisconnect;

            ServerSpawnAllCores();
            matchStateNv.Value = MatchState.Playing;
            Debug.Log("[GameManager] Server started, cores spawned, match Playing");
        }

        private void ApproveConnection(NetworkManager.ConnectionApprovalRequest request, NetworkManager.ConnectionApprovalResponse response)
        {
            if (NetworkManager.ConnectedClientsList.Count >= maxPlayers)
            {
                response.Approved = false;
                response.Reason = "Game is full (8 players)";
                return;
            }

            // Full player object creation, team assignment and spawn positioning are
            // performed on the server when the player prefab spawns (ServerRegisterPlayer),
            // so that host, remote and dedicated-server clients all go through one code path.
            response.Approved = true;
        }

        private Vector3 GetTeamSpawnPosition(Team team, int slot)
        {
            Transform[] spawns = team == Team.Red ? redSpawns : blueSpawns;
            Transform marker = spawns[slot % spawns.Length];
            return marker.position + Vector3.up * 1f;
        }


        public void ServerRegisterPlayer(ulong clientId, NetworkPlayer player)
        {
            if (player.playerTeam.Value == Team.None)
            {
                Team team = _redCount <= _blueCount ? Team.Red : Team.Blue;
                if (team == Team.Red) _redCount++;
                else _blueCount++;
                int slot = (team == Team.Red ? _redCount - 1 : _blueCount - 1);
                player.playerTeam.Value = team;
                // NetworkTransform.Teleport must not run while the spawn sweep is still
                // initializing component states; queue the position for the next server update.
                _pendingSpawns.Enqueue((player, GetTeamSpawnPosition(team, slot)));
            }

            _players[clientId] = player;
        }

        private void Update()
        {
            if (!IsServer || !IsSpawned) return;
            while (_pendingSpawns.Count > 0)
            {
                (NetworkPlayer player, Vector3 position) = _pendingSpawns.Dequeue();
                if (player == null || !player.IsSpawned) continue;
                player.GetComponent<Unity.Netcode.Components.NetworkTransform>().Teleport(position, Quaternion.identity, Vector3.one);
                Debug.Log($"[GameManager] Spawned {player.name} ({player.playerTeam.Value}) at {position}");
            }
        }

        public void ServerUnregisterPlayer(ulong clientId)
        {
            _players.Remove(clientId);
        }

        public NetworkPlayer ServerGetPlayer(ulong clientId)
        {
            return _players.TryGetValue(clientId, out var player) && player != null ? player : null;
        }

        public Team ServerGetAssignedTeam(ulong clientId)
        {
            NetworkPlayer player = ServerGetPlayer(clientId);
            return player != null ? player.playerTeam.Value : Team.None;
        }

        public bool ServerIsPlayerInOwnBase(NetworkPlayer player)
        {
            if (player == null) return false;
            Transform homeBase = player.playerTeam.Value == Team.Red ? redBase : blueBase;
            if (homeBase == null) return false;
            Vector3 diff = player.transform.position - homeBase.position;
            return Mathf.Abs(diff.x) <= baseHalfExtents.x && Mathf.Abs(diff.z) <= baseHalfExtents.y;
        }

        public void ServerTeamScored(Team team)
        {
            if (!IsServer) return;
            if (team == Team.Red) redScore.Value++;
            else if (team == Team.Blue) blueScore.Value++;
            Debug.Log("[GameManager] Score: red=" + redScore.Value + " blue=" + blueScore.Value);
        }

        private void ServerOnClientDisconnect(ulong clientId)
        {
            NetworkPlayer player = ServerGetPlayer(clientId);
            if (player != null)
            {
                if (player.playerTeam.Value == Team.Red) _redCount = Mathf.Max(0, _redCount - 1);
                else if (player.playerTeam.Value == Team.Blue) _blueCount = Mathf.Max(0, _blueCount - 1);
                ServerUnregisterPlayer(clientId);
            }

            Debug.Log("[GameManager] Client " + clientId + " disconnected (full cleanup in step 5)");
        }

        private void ServerSpawnAllCores()
        {
            if (corePrefab == null)
            {
                Debug.LogError("[GameManager] Core prefab not assigned");
                return;
            }

            NetworkObject coreNetworkObject = corePrefab.GetComponent<NetworkObject>();
            NetworkSpawnManager spawnManager = NetworkManager.SpawnManager;
            foreach (Transform marker in coreSpawns)
            {
                spawnManager.InstantiateAndSpawn(coreNetworkObject, NetworkManager.ServerClientId,
                    true, false, false, marker.position, Quaternion.identity);
            }
        }
    }
}