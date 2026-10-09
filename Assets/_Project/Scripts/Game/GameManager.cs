using System;
using System.Collections.Generic;
using _Project.Scripts.Objective;
using _Project.Scripts.Player;
using Unity.Netcode;
using UnityEngine;

namespace _Project.Scripts.Game
{
    public class GameManager : NetworkBehaviour
    {
        public static GameManager Instance { get; private set; }

        [SerializeField] private int maxPlayers = 8;
        [SerializeField] private int minPlayersToStart = 3;
        [SerializeField] private int coresToWin = 2;
        [SerializeField] private float startingDurationSeconds = 3f;
        [SerializeField] private Vector2 baseHalfExtents = new Vector2(3f, 7f);

        // Relative to each base's transform; one slot per core that can be scored.
        [SerializeField] private Vector3[] coreSlotOffsets = new Vector3[]
        {
            new Vector3(0f, 0.65f, -3f),
            new Vector3(0f, 0.65f, 3f)
        };

        [SerializeField] private GameObject corePrefab;
        [SerializeField] private GameObject orbPrefab;
        [SerializeField] private Transform redBase;
        [SerializeField] private Transform blueBase;
        [SerializeField] private Transform orbSpawn;
        [SerializeField] private Transform[] redSpawns;
        [SerializeField] private Transform[] blueSpawns;
        [SerializeField] private Transform[] coreSpawns;


        public NetworkVariable<MatchState> matchStateNv = new NetworkVariable<MatchState>(MatchState.WaitingForPlayers);
        public NetworkVariable<int> redScore = new NetworkVariable<int>();
        public NetworkVariable<int> blueScore = new NetworkVariable<int>();
        public NetworkVariable<int> startingCountdown = new NetworkVariable<int>(-1); // -1 = inactive
        public NetworkVariable<Team> winningTeam = new NetworkVariable<Team>(Team.None);
        public int MinimumPlayersToStart => minPlayersToStart;
        private readonly Dictionary<ulong, NetworkPlayer> _players = new Dictionary<ulong, NetworkPlayer>();
        private readonly Dictionary<ulong, (Team team, int slot)> _coreSlots = new Dictionary<ulong, (Team, int)>();
        private int _redCount;
        private int _blueCount;
        private readonly Queue<(NetworkPlayer player, Vector3 position)> _pendingSpawns = new Queue<(NetworkPlayer, Vector3)>();
        private double _startingEndServerTime;

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
            ServerSpawnOrb();
            matchStateNv.Value = MatchState.WaitingForPlayers;
            Debug.Log("[GameManager] Server started, cores spawned, WaitingForPlayers");
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
            response.CreatePlayerObject = true;
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

            ServerTickMatchState();
        }

        // ============================================================
        // MATCH STATE MACHINE : transitions and timers live on
        // the server; clients only read the replicated state.
        private void ServerTickMatchState()
        {
            double now = NetworkManager.Singleton.ServerTime.Time;
            switch (matchStateNv.Value)
            {
                case MatchState.WaitingForPlayers:
                    if (NetworkManager.ConnectedClientsList.Count >= minPlayersToStart)
                    {
                        _startingEndServerTime = now + startingDurationSeconds;
                        matchStateNv.Value = MatchState.Starting;
                        startingCountdown.Value = Mathf.CeilToInt((float)startingDurationSeconds);
                        Debug.Log($"[GameManager] {minPlayersToStart}+ players connected, match Starting");
                    }

                    break;

                case MatchState.Starting:
                    int remaining = Mathf.CeilToInt((float)(_startingEndServerTime - now));
                    if (remaining >= 0 && remaining != startingCountdown.Value)
                    {
                        startingCountdown.Value = remaining;
                    }

                    if (now >= _startingEndServerTime)
                    {
                        matchStateNv.Value = MatchState.Playing;
                        startingCountdown.Value = -1;
                        Debug.Log("[GameManager] Match Playing");
                    }

                    break;

                case MatchState.Playing:
                case MatchState.Finished:
                    break;
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

        // ============================================================
        // CORE SLOTS: each base has a fixed set of pads a carried Core
        // snaps to while being scored, so two Cores at one base never
        // overlap. Reservations live on the server only.
        public bool ServerTryReserveCoreSlot(Team team, ObjectiveCore core, out Vector3 position)
        {
            position = Vector3.zero;
            if (core == null || coreSlotOffsets == null || coreSlotOffsets.Length == 0) return false;

            ulong coreId = core.NetworkObjectId;

            // A Core already stationed in this base keeps its pad.
            if (_coreSlots.TryGetValue(coreId, out (Team team, int slot) existing) && existing.team == team)
            {
                position = GetCoreSlotPosition(team, existing.slot);
                return true;
            }

            for (int i = 0; i < coreSlotOffsets.Length; i++)
            {
                if (IsCoreSlotFree(team, i, coreId))
                {
                    _coreSlots[coreId] = (team, i);
                    position = GetCoreSlotPosition(team, i);
                    return true;
                }
            }

            return false;
        }

        public void ServerReleaseCoreSlot(ObjectiveCore core)
        {
            if (core != null) _coreSlots.Remove(core.NetworkObjectId);
        }

        private bool IsCoreSlotFree(Team team, int slot, ulong exceptCoreId)
        {
            foreach (KeyValuePair<ulong, (Team team, int slot)> entry in _coreSlots)
            {
                if (entry.Key == exceptCoreId) continue;
                if (entry.Value.team == team && entry.Value.slot == slot) return false;
            }

            return true;
        }

        private Vector3 GetCoreSlotPosition(Team team, int slot)
        {
            Transform homeBase = team == Team.Red ? redBase : blueBase;
            if (homeBase == null || coreSlotOffsets == null || slot < 0 || slot >= coreSlotOffsets.Length)
            {
                return homeBase != null ? homeBase.position : Vector3.zero;
            }

            return homeBase.position + homeBase.rotation * coreSlotOffsets[slot];
        }

        public void ServerTeamScored(Team team)
        {
            if (!IsServer) return;
            if (team == Team.Red) redScore.Value++;
            else if (team == Team.Blue) blueScore.Value++;
            Debug.Log("[GameManager] Score: red=" + redScore.Value + " blue=" + blueScore.Value);

            // Victory A : first team to complete coresToWin cores wins.
            if (matchStateNv.Value == MatchState.Playing)
            {
                if (redScore.Value >= coresToWin) ServerFinishMatch(Team.Red);
                else if (blueScore.Value >= coresToWin) ServerFinishMatch(Team.Blue);
            }
        }

        // ============================================================
        // DISCONNECT CLEANUP: centralized on the server.
        // Any Core/Orb possessed by the disconnected player is released
        // and team counters rebalanced; the match keeps running.
        // The player prefab uses DontDestroyWithOwner so the object is
        // still readable here; we release possessions and despawn it ourselves.
        private void ServerOnClientDisconnect(ulong clientId)
        {
            NetworkPlayer player = ServerGetPlayer(clientId);
            if (player != null)
            {
                if (player.carriedCoreId.Value != 0)
                {
                    ObjectiveCore carried = ObjectiveCore.FindCoreById(player.carriedCoreId.Value);
                    if (carried != null)
                    {
                        carried.ServerReleaseCore(CoreState.Available);
                        Debug.Log("[GameManager] Released core after disconnect of " + clientId);
                    }
                }

                if (player.hasOrb.Value)
                {
                    foreach (_Project.Scripts.Orb.SharedOrb orb in _Project.Scripts.Orb.SharedOrb.All)
                    {
                        if (orb.carrierClientId.Value == clientId)
                        {
                            orb.ServerReturnToSpawn();
                            Debug.Log("[GameManager] Returned orb after disconnect of " + clientId);
                        }
                    }
                }

                if (player.playerTeam.Value == Team.Red) _redCount = Mathf.Max(0, _redCount - 1);
                else if (player.playerTeam.Value == Team.Blue) _blueCount = Mathf.Max(0, _blueCount - 1);

                if (player.IsSpawned)
                {
                    player.NetworkObject.Despawn();
                }

                ServerUnregisterPlayer(clientId);
            }

            Debug.Log("[GameManager] Client " + clientId + " disconnected");
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

        private void ServerSpawnOrb()
        {
            if (orbPrefab == null || orbSpawn == null)
            {
                Debug.LogError("[GameManager] Orb prefab/spawn not assigned");
                return;
            }

            NetworkObject orbNetworkObject = orbPrefab.GetComponent<NetworkObject>();
            NetworkManager.SpawnManager.InstantiateAndSpawn(orbNetworkObject, NetworkManager.ServerClientId,
                true, false, false, orbSpawn.position, Quaternion.identity);
        }

        // ============================================================
        // ELIMINATION: server-only. Releases everything the
        // player possessed so no gameplay object stays orphaned.
        public void ServerEliminatePlayer(NetworkPlayer player)
        {
            if (!IsServer || player == null || player.state.Value != PlayerState.Alive) return;

            player.state.Value = PlayerState.Eliminated;

            if (player.carriedCoreId.Value != 0)
            {
                ObjectiveCore carried = ObjectiveCore.FindCoreById(player.carriedCoreId.Value);
                if (carried != null)
                {
                    carried.ServerReleaseCore(CoreState.Available);
                }
            }

            foreach (_Project.Scripts.Orb.SharedOrb orb in _Project.Scripts.Orb.SharedOrb.All)
            {
                if (orb.carrierClientId.Value == player.OwnerClientId)
                {
                    orb.ServerReturnToSpawn();
                }
            }

            Debug.Log($"[GameManager] Player {player.OwnerClientId} eliminated; core/orb released");

            // Victory B : all active players of a team eliminated.
            ServerCheckEliminationVictory();
        }

        private void ServerCheckEliminationVictory()
        {
            if (matchStateNv.Value != MatchState.Playing) return;

            int redAlive = 0;
            int blueAlive = 0;
            foreach (NetworkPlayer player in _players.Values)
            {
                if (player == null || player.state.Value != PlayerState.Alive) continue;
                if (player.playerTeam.Value == Team.Red) redAlive++;
                else if (player.playerTeam.Value == Team.Blue) blueAlive++;
            }

            if (redAlive > 0 && blueAlive == 0) ServerFinishMatch(Team.Red);
            else if (blueAlive > 0 && redAlive == 0) ServerFinishMatch(Team.Blue);
        }

        public void ServerFinishMatch(Team winner)
        {
            if (!IsServer || matchStateNv.Value == MatchState.Finished) return;
            matchStateNv.Value = MatchState.Finished;
            winningTeam.Value = winner;
            Debug.Log("[GameManager] Match Finished, winner=" + winner);
        }
    }
}