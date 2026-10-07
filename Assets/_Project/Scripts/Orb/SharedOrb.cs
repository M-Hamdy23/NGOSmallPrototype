using System.Collections.Generic;
using _Project.Scripts.Game;
using _Project.Scripts.Player;
using Unity.Netcode;
using UnityEngine;

namespace _Project.Scripts.Orb
{
    public class SharedOrb : NetworkBehaviour
    {
        [SerializeField] private float pickupRadius = 3f;
        [SerializeField] private float throwSpeed = 12f;
        [SerializeField] private float maxFlightSeconds = 3f;

        public NetworkVariable<OrbState> state = new NetworkVariable<OrbState>(OrbState.Available);
        public NetworkVariable<ulong> carrierClientId = new NetworkVariable<ulong>(ulong.MaxValue);

        public static IReadOnlyList<SharedOrb> All => Instances;
        private static readonly List<SharedOrb> Instances = new List<SharedOrb>();

        private Vector3 _spawnPosition;
        private double _flightEndServerTime;
        private ulong _throwerClientId = ulong.MaxValue;
        private Rigidbody _rigidbody;
        private Renderer _renderer;

        private static readonly Color ColorAvailable = new Color(0.7f, 0.9f, 1f);
        private static readonly Color ColorThrown = new Color(1f, 0.25f, 0.35f);

        private void OnEnable()
        {
            Instances.Add(this);
            _rigidbody = GetComponent<Rigidbody>();
            _renderer = GetComponent<Renderer>();
        }

        private void OnDisable()
        {
            Instances.Remove(this);
        }

        public override void OnNetworkSpawn()
        {
            _spawnPosition = transform.position;
            // Server-only physics: clients get a kinematic rigidbody and rely
            // on NetworkTransform replication for the visual position.
            if (!IsServer && _rigidbody != null)
            {
                _rigidbody.isKinematic = true;
            }
            state.OnValueChanged += UpdateVisuals;
            UpdateVisuals(OrbState.Available, state.Value);
        }

        public override void OnNetworkDespawn()
        {
            state.OnValueChanged -= UpdateVisuals;
        }

        public static SharedOrb FindOrbById(ulong networkObjectId)
        {
            foreach (var orb in Instances)
            {
                if (orb.NetworkObjectId == networkObjectId)
                {
                    return orb;
                }
            }
            return null;
        }

        // ============================================================
        // SERVER AUTHORITY (plan §7/§8)
        // Clients may only request possession or a throw direction.
        // Possession, flight and hit results are decided by the server.

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void PickupRequestServerRpc(RpcParams rpcParams = default)
        {
            ServerTryPickup(rpcParams.Receive.SenderClientId);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void ThrowRequestServerRpc(Vector2 direction, RpcParams rpcParams = default)
        {
            ServerTryThrow(rpcParams.Receive.SenderClientId, direction);
        }

        private bool ServerTryPickup(ulong senderClientId)
        {
            if (!IsServer) return false;

            if (GameManager.Instance == null || GameManager.Instance.matchStateNv.Value != MatchState.Playing) return false;
            if (state.Value != OrbState.Available) return false;

            NetworkPlayer player = GameManager.Instance.ServerGetPlayer(senderClientId);
            if (player == null) return false;
            if (player.state.Value != PlayerState.Alive) return false;
            if (player.playerTeam.Value == Team.None) return false;
            if (Vector3.Distance(player.transform.position, transform.position) > pickupRadius) return false;

            // Exclusive possession: the same compare-and-set pattern as the Cores.
            state.Value = OrbState.Carried;
            carrierClientId.Value = senderClientId;
            player.hasOrb.Value = true;

            Debug.Log("[SharedOrb] " + name + " picked by client " + senderClientId);
            return true;
        }

        private bool ServerTryThrow(ulong senderClientId, Vector2 direction)
        {
            if (!IsServer) return false;

            if (GameManager.Instance == null || GameManager.Instance.matchStateNv.Value != MatchState.Playing) return false;
            if (state.Value != OrbState.Carried) return false;
            if (carrierClientId.Value != senderClientId) return false;

            NetworkPlayer player = GameManager.Instance.ServerGetPlayer(senderClientId);
            if (player == null) return false;
            if (player.state.Value != PlayerState.Alive) return false;
            if (player.hasOrb.Value != true) return false;
            if (direction.sqrMagnitude < 0.001f) return false;

            Vector3 flatDirection = new Vector3(direction.x, 0f, direction.y).normalized;
            // Spawn fully outside the thrower capsule (radius 0.5 + orb radius 0.3)
            // so physics never kicks the orb out with a penetration impulse.
            Vector3 throwOrigin = player.transform.position + Vector3.up * 1f + flatDirection * 1f;

            // End possession on the server; possession never relied on NGO ownership.
            state.Value = OrbState.Thrown;
            carrierClientId.Value = ulong.MaxValue;
            player.hasOrb.Value = false;
            _throwerClientId = senderClientId;

            _rigidbody.isKinematic = false;
            _rigidbody.useGravity = false;
            _rigidbody.linearVelocity = flatDirection * throwSpeed;
            transform.position = throwOrigin;
            _flightEndServerTime = NetworkManager.Singleton.ServerTime.Time + maxFlightSeconds;

            Debug.Log("[SharedOrb] " + name + " thrown by client " + senderClientId +
                " direction=" + flatDirection);
            return true;
        }

        private void Update()
        {
            if (!IsServer || !IsSpawned) return;

            switch (state.Value)
            {
                case OrbState.Carried:
                    ServerFollowCarrier();
                    break;
                case OrbState.Thrown:
                    if (NetworkManager.Singleton.ServerTime.Time >= _flightEndServerTime)
                    {
                        ServerReturnToSpawn();
                    }
                    break;
            }
        }

        private void ServerFollowCarrier()
        {
            NetworkPlayer carrier = GameManager.Instance != null
                ? GameManager.Instance.ServerGetPlayer(carrierClientId.Value)
                : null;
            if (carrier == null || carrier.state.Value != PlayerState.Alive)
            {
                ServerReturnToSpawn();
                return;
            }
            transform.position = carrier.transform.position + Vector3.up * 1.2f;
        }

        internal void ServerReturnToSpawn()
        {
            if (_rigidbody != null)
            {
                _rigidbody.isKinematic = true;
                _rigidbody.linearVelocity = Vector3.zero;
            }
            GetComponent<Unity.Netcode.Components.NetworkTransform>()
                .Teleport(_spawnPosition, Quaternion.identity, Vector3.one);
            state.Value = OrbState.Available;
            carrierClientId.Value = ulong.MaxValue;
            _throwerClientId = ulong.MaxValue;
        }

        // Server-only helper for hit validation (plan §9): the projectile asks
        // the server whose team threw it before eliminating a target.
        internal Team ServerThrowerTeam()
        {
            NetworkPlayer thrower = GameManager.Instance != null
                ? GameManager.Instance.ServerGetPlayer(_throwerClientId)
                : null;
            return thrower != null ? thrower.playerTeam.Value : Team.None;
        }

        internal ulong ServerThrowerClientId => _throwerClientId;

        private void UpdateVisuals(OrbState oldState, OrbState newState)
        {
            if (_renderer == null) return;
            switch (newState)
            {
                case OrbState.Available:
                    _renderer.enabled = true;
                    _renderer.material.color = ColorAvailable;
                    break;
                case OrbState.Carried:
                    _renderer.enabled = false;
                    break;
                case OrbState.Thrown:
                    _renderer.enabled = true;
                    _renderer.material.color = ColorThrown;
                    break;
            }
        }
    }
}
