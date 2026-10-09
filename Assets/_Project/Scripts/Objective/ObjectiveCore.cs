using System.Collections.Generic;
using _Project.Scripts.Game;
using _Project.Scripts.Player;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;

namespace _Project.Scripts.Objective
{
    public class ObjectiveCore : NetworkBehaviour
    {
        [SerializeField] private float pickupRadius = 3f;
        [SerializeField] private float interactionDuration = 1f;

        public NetworkVariable<CoreState> state = new NetworkVariable<CoreState>(CoreState.Available);
        public NetworkVariable<ulong> carrierClientId = new NetworkVariable<ulong>(ulong.MaxValue);

        public static IReadOnlyList<ObjectiveCore> All => Instances;
        private static readonly List<ObjectiveCore> Instances = new List<ObjectiveCore>();

        // NoteL server-only cached values
        private double _interactionEndTimeServer;
        private Renderer _renderer;
        private NetworkTransform _networkTransform;
        private Vector3 _homePosition;
        private static readonly Color ColorAvailable = new Color(1f, 0.85f, 0.2f);
        private static readonly Color ColorInteraction = new Color(1f, 0.45f, 0f);
        private static readonly Color ColorCompleted = new Color(0.2f, 0.9f, 0.3f);

        private void OnEnable()
        {
            Instances.Add(this);
            _renderer = GetComponent<Renderer>();
            _networkTransform = GetComponent<NetworkTransform>();
        }

        private void OnDisable()
        {
            Instances.Remove(this);
        }

        public static ObjectiveCore FindCoreById(ulong networkObjectId)
        {
            foreach (var objectiveCore in Instances)
            {
                if (objectiveCore.NetworkObjectId == networkObjectId)
                {
                    return objectiveCore;
                }
            }

            return null;
        }

        public override void OnNetworkSpawn()
        {
            if (IsServer)
            {
                _homePosition = transform.position;
            }

            state.OnValueChanged += UpdateVisuals;
            UpdateVisuals(CoreState.Available, state.Value);
        }

        public override void OnNetworkDespawn()
        {
            state.OnValueChanged -= UpdateVisuals;
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void PickupRequestServerRpc(RpcParams rpcParams = default)
        {
            ServerTryPickup(rpcParams.Receive.SenderClientId);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void InteractionRequestServerRpc(RpcParams rpcParams = default)
        {
            ServerTryStartInteraction(rpcParams.Receive.SenderClientId);
        }

        // ============================================================
        // SERVER AUTHORITY
        // All trajectory decisions for this Core are made here. Simplified:
        // the ServerRpc call is the only entry point (the caller is
        // validated by NGO and all server state transitions are
        // performed sequentially by the single server loop).

        private bool ServerTryPickup(ulong senderClientId)
        {
            if (!IsServer) return false;

            if (GameManager.Instance == null || GameManager.Instance.matchStateNv.Value != MatchState.Playing) return false;
            if (state.Value != CoreState.Available) return false;

            NetworkPlayer player = GameManager.Instance.ServerGetPlayer(senderClientId);
            if (player == null) return false;
            if (player.state.Value != PlayerState.Alive) return false;
            if (player.playerTeam.Value == Team.None) return false;
            if (player.carriedCoreId.Value != 0UL) return false;
            if (Vector3.Distance(player.transform.position, transform.position) > pickupRadius) return false;

            // RACE-CONDITION PROTECTION:
            // two clients can send this request at the same time. NGO executes
            // server RPCs sequentially on the single server loop: the first
            // valid request transitions State Available -> Carried; the
            // second one observes Carried and gets rejected. The Core
            // instance keeps NGO ownership on the server; possession is
            // represented by the carrier client ID, never by ownership transfer.
            state.Value = CoreState.Carried;
            carrierClientId.Value = senderClientId;
            player.carriedCoreId.Value = NetworkObjectId;

            Debug.Log("[ObjectiveCore] " + name + " picked by client " + senderClientId);
            return true;
        }

        private bool ServerTryStartInteraction(ulong senderClientId)
        {
            if (!IsServer) return false;

            if (GameManager.Instance == null || GameManager.Instance.matchStateNv.Value != MatchState.Playing) return false;
            if (state.Value != CoreState.Carried) return false;
            if (carrierClientId.Value != senderClientId) return false;

            NetworkPlayer player = GameManager.Instance.ServerGetPlayer(senderClientId);
            if (player == null) return false;
            if (player.state.Value != PlayerState.Alive) return false;
            if (player.carriedCoreId.Value != NetworkObjectId) return false;
            if (!GameManager.Instance.ServerIsPlayerInOwnBase(player)) return false;

            // Station the core on a free pad in the player's base before it
            // becomes visible, so it never flashes at its original position.
            if (!GameManager.Instance.ServerTryReserveCoreSlot(player.playerTeam.Value, this, out Vector3 slotPosition))
            {
                Debug.Log("[ObjectiveCore] " + name + " interaction rejected: no free core slot");
                return false;
            }

            _networkTransform.Teleport(slotPosition, transform.rotation, transform.localScale);

            state.Value = CoreState.InteractionInProgress;
            _interactionEndTimeServer = NetworkManager.Singleton.ServerTime.Time + interactionDuration;

            Debug.Log("[ObjectiveCore] " + name + " interaction started by client " + senderClientId +
                " (ends at " + _interactionEndTimeServer.ToString("F2") + ")");
            return true;
        }

        private void Update()
        {
            if (!IsServer || !IsSpawned) return;
            if (state.Value == CoreState.InteractionInProgress)
            {
                ServerTickInteraction();
            }
        }

        private void ServerTickInteraction()
        {
            NetworkPlayer player = GameManager.Instance != null
                ? GameManager.Instance.ServerGetPlayer(carrierClientId.Value)
                : null;

            // Cancellation: carrier gone/eliminated/lost the core -> Core goes back to Available.
            if (player == null || player.state.Value != PlayerState.Alive ||
                player.carriedCoreId.Value != NetworkObjectId)
            {
                ServerReleaseCore(CoreState.Available);
                Debug.Log("[ObjectiveCore] " + name + " interaction cancelled: invalid carrier");
                return;
            }

            // Cancellation: player moved out of their base -> pause back to Carried, requirable
            if (!GameManager.Instance.ServerIsPlayerInOwnBase(player))
            {
                GameManager.Instance.ServerReleaseCoreSlot(this);
                state.Value = CoreState.Carried;
                Debug.Log("[ObjectiveCore] " + name + " interaction cancelled: left base");
                return;
            }

            if (NetworkManager.Singleton.ServerTime.Time >= _interactionEndTimeServer)
            {
                ServerComplete(player.playerTeam.Value);
            }
        }

        private void ServerComplete(Team team)
        {
            NetworkPlayer player = GameManager.Instance != null
                ? GameManager.Instance.ServerGetPlayer(carrierClientId.Value)
                : null;
            if (player != null)
            {
                player.carriedCoreId.Value = 0UL;
            }
            state.Value = CoreState.Completed;
            carrierClientId.Value = ulong.MaxValue;
            if (GameManager.Instance != null)
            {
                GameManager.Instance.ServerTeamScored(team);
            }
            Debug.Log("[ObjectiveCore] " + name + " completed by " + team);
        }

        internal void ServerReleaseCore(CoreState nextState)
        {
            NetworkPlayer player = GameManager.Instance != null
                ? GameManager.Instance.ServerGetPlayer(carrierClientId.Value)
                : null;
            if (player != null && player.carriedCoreId.Value == NetworkObjectId)
            {
                player.carriedCoreId.Value = 0UL;
            }

            // Disconnect/elimination/cancel: free the pad and snap the core back to its spawn.
            if (GameManager.Instance != null) GameManager.Instance.ServerReleaseCoreSlot(this);
            _networkTransform.Teleport(_homePosition, transform.rotation, transform.localScale);

            state.Value = nextState;
            carrierClientId.Value = ulong.MaxValue;
        }

        private void UpdateVisuals(CoreState oldState, CoreState newState)
        {
            if (_renderer == null) return;
            switch (newState)
            {
                case CoreState.Available:
                    _renderer.enabled = true;
                    _renderer.material.color = ColorAvailable;
                    break;
                case CoreState.Carried:
                    _renderer.enabled = false;
                    break;
                case CoreState.InteractionInProgress:
                    _renderer.enabled = true;
                    _renderer.material.color = ColorInteraction;
                    break;
                case CoreState.Completed:
                    _renderer.enabled = true;
                    _renderer.material.color = ColorCompleted;
                    break;
            }
        }
    }
}

