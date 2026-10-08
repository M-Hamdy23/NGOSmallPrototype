using System.Collections.Generic;
using _Project.Scripts.Game;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace _Project.Scripts.Player
{
    /// <summary>
    /// Server-authoritative movement driven by sequenced input commands, with
    /// owner-side prediction and reconciliation.
    ///
    /// Flow:
    ///   Owner  : sample input, predict locally, send a <see cref="MoveCmd"/> per tick.
    ///   Server : validate each command at ingest, integrate it strictly in order, and
    ///            publish a <see cref="MoveAck"/> (last processed sequence + position).
    ///   Owner  : on each ack, snap to the authoritative position, then replay every
    ///            command the server has not processed yet.
    ///
    /// The client only ever sends inputs, never positions or speeds. Speed, alive
    /// state, match state, orb state and arena bounds are resolved from server state.
    /// </summary>
    public class PlayerMovement : NetworkBehaviour
    {
        #region Message types

        /// <summary>One input sample from the owner, tagged with a monotonic sequence number.</summary>
        public struct MoveCmd : INetworkSerializable
        {
            public uint seq;
            public Vector2 input;

            public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
            {
                serializer.SerializeValue(ref seq);
                serializer.SerializeValue(ref input);
            }
        }

        /// <summary>Server acknowledgement: the last processed sequence and the resulting position.</summary>
        public struct MoveAck : INetworkSerializable, System.IEquatable<MoveAck>
        {
            public uint seq;
            public Vector3 pos;

            public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
            {
                serializer.SerializeValue(ref seq);
                serializer.SerializeValue(ref pos);
            }

            public bool Equals(MoveAck other) => seq == other.seq && pos == other.pos;
        }

        #endregion

        #region Configuration

        [Header("Movement")]
        [SerializeField, Tooltip("Units per second while carrying nothing.")]
        private float moveSpeed = 5f;

        [SerializeField, Tooltip("Speed multiplier applied while carrying the orb.")]
        private float carrierSpeedMultiplier = 0.5f;

        [SerializeField, Tooltip("Half-size of the playable arena on X (left/right) and Z (forward/back).")]
        private Vector2 arenaHalfExtents = new Vector2(29f, 14.5f);

        [SerializeField, Tooltip("Input System action (Player/Move) sampled for keyboard, gamepad and the on-screen stick.")]
        private InputActionReference moveActionReference;

        // ~4 s of command history at 60 Hz. Also the maximum accepted sequence gap,
        // which bounds how many unprocessed commands a flooding sender can queue.
        private const int CommandBufferCapacity = 240;

        // Backlog catch-up budget. Commands are always integrated in order, never
        // merged, so a backlog simply takes a few extra ticks to drain.
        private const int MaxServerStepsPerTick = 8;

        // Validation rejects are throttled so a hostile flood cannot spam the log.
        private const float RejectLogCooldown = 1f;

        #endregion

        #region Runtime state

        /// <summary>Latest server acknowledgement. Read-only: external code cannot mutate networking state.</summary>
        public MoveAck LastAck => _moveAck.Value;

        /// <summary>Last non-zero input direction (unit length). Also used as the throw-aim fallback.</summary>
        public Vector2 LastMoveDirection => _lastMoveDirection;

        private NetworkVariable<MoveAck> _moveAck = new NetworkVariable<MoveAck>();

        private NetworkPlayer _networkPlayer;
        private SmoothedAnticipatedNetworkTransform _anticipatedTransform;

        // Server-side simulation state.
        private readonly Queue<MoveCmd> _pendingCommands = new Queue<MoveCmd>();
        private uint _lastProcessedSeq;

        // Owner-side prediction state.
        private readonly List<MoveCmd> _sentCommands = new List<MoveCmd>(CommandBufferCapacity);
        private uint _nextSeq = 1;
        private Vector3 _predictedPos;
        private bool _hasReconciled;
        private uint _lastAckedSeq;

        // Input state.
        private Vector2 _lastMoveDirection = Vector2.right;
        private Vector2 _externalInput;
        private float _lastRejectLogTime = -RejectLogCooldown;

        #endregion

        #region Unity lifecycle

        private void FixedUpdate()
        {
            if (!IsSpawned) return;

            if (IsServer)
            {
                ServerSimulate();
            }
            else if (IsOwner)
            {
                PredictStep();
            }
        }

        private void Update()
        {
            if (!IsSpawned) return;

            // The owner renders from its predicted state every frame; non-owner players
            // are rendered by the NetworkTransform's own smoothing.
            if (!IsServer && IsOwner && _anticipatedTransform != null)
            {
                _anticipatedTransform.AnticipateMove(_predictedPos);
            }
        }

        public override void OnNetworkSpawn()
        {
            name = $"Player_{OwnerClientId}";
            _networkPlayer = GetComponent<NetworkPlayer>();
            _anticipatedTransform = GetComponent<SmoothedAnticipatedNetworkTransform>();

            // Clear any state carried over by a reused or respawned network object.
            ResetMovementState();
            _moveAck.OnValueChanged += OnMoveAckChanged;

            // The prefab sits at the pre-teleport spawn point; the server teleports the
            // player to its team base shortly after spawn. This transient self-heals on
            // the first ack, so it is not treated as authoritative here.
            _predictedPos = transform.position;
            _lastMoveDirection = transform.forward.normalized != Vector3.zero
                ? new Vector2(transform.forward.x, transform.forward.z)
                : Vector2.right;

            if (IsOwner)
            {
                SetInputActionsEnabled(true);
            }
        }

        public override void OnNetworkDespawn()
        {
            _moveAck.OnValueChanged -= OnMoveAckChanged;
            SetInputActionsEnabled(false);
            ResetMovementState();
        }

        /// <summary>Re-asserts our prediction if the NetworkTransform applies an authoritative value out of turn.</summary>
        public override void OnReanticipate(double lastRoundTripTime)
        {
            if (!IsOwner || IsServer) return;
            if (_anticipatedTransform != null) _anticipatedTransform.AnticipateMove(_predictedPos);
        }

        #endregion

        #region Input

        /// <summary>Feeds movement from an external source (e.g. the mobile joystick).</summary>
        public void SetExternalInput(Vector2 input)
        {
            _externalInput = input;
        }

        private Vector2 ReadLocalInput()
        {
            // Mobile joystick / external controllers take priority when active.
            if (_externalInput != Vector2.zero)
            {
                return Vector2.ClampMagnitude(_externalInput, 1f);
            }

            // Input System action (Player/Move): WASD composite, gamepad left stick and
            // the on-screen stick. Clamped here as well so client prediction uses the
            // same magnitude the server enforces (WASD diagonals are ~1.41 raw).
            if (moveActionReference != null)
            {
                return Vector2.ClampMagnitude(moveActionReference.action.ReadValue<Vector2>(), 1f);
            }

            return Vector2.zero;
        }

        private void SetInputActionsEnabled(bool enableInput)
        {
            if (moveActionReference == null) return;

            if (enableInput) moveActionReference.action.Enable();
            else moveActionReference.action.Disable();
        }

        #endregion

        #region Owner prediction

        private void PredictStep()
        {
            Vector2 input = ReadLocalInput();
            var cmd = new MoveCmd { seq = _nextSeq++, input = input };

            _sentCommands.Add(cmd);
            if (_sentCommands.Count > CommandBufferCapacity)
            {
                _sentCommands.RemoveAt(0);
            }

            if (input != Vector2.zero)
            {
                // Kept unit length; also the throw-aim fallback direction.
                _lastMoveDirection = input.normalized;
            }

            _predictedPos = StepPosition(_predictedPos, input, GetMoveSpeed() * Time.fixedDeltaTime);
            MoveCmdServerRpc(cmd);
        }

        /// <summary>
        /// Reconciliation: reset to the authoritative position for the acknowledged
        /// sequence, then deterministically replay every un-acked command. Because both
        /// sides use the same step math at the same fixed rate, drift converges to zero
        /// and corrections are imperceptible.
        /// </summary>
        private void OnMoveAckChanged(MoveAck previousValue, MoveAck newValue)
        {
            if (!IsOwner || IsServer) return;

            // Only accept acknowledgements for sequences this client actually sent, and
            // only if newer than the last one processed. This rejects default acks
            // (seq 0), never-sent sequences and stale/out-of-order acks, so malformed
            // data cannot corrupt prediction state.
            if (newValue.seq == 0) return;
            if (newValue.seq >= _nextSeq) return;
            if (newValue.seq <= _lastAckedSeq) return;

            _lastAckedSeq = newValue.seq;

            Vector3 pos = newValue.pos;
            for (int i = 0; i < _sentCommands.Count; i++)
            {
                if (_sentCommands[i].seq <= newValue.seq) continue;
                pos = StepPosition(pos, _sentCommands[i].input, GetMoveSpeed() * Time.fixedDeltaTime);
            }

            _sentCommands.RemoveAll(cmd => cmd.seq <= newValue.seq);
            _predictedPos = pos;

            if (!_hasReconciled)
            {
                _hasReconciled = true;
                if (_anticipatedTransform != null) _anticipatedTransform.AnticipateMove(_predictedPos);
            }
        }

        #endregion

        #region Server simulation

        [Rpc(SendTo.Server, Delivery = RpcDelivery.Reliable, InvokePermission = RpcInvokePermission.Owner)]
        private void MoveCmdServerRpc(MoveCmd cmd)
        {
            if (!IsServer) return;

            // Every command is validated here at ingest, so the simulation loop can
            // assume the pending queue only contains sanitized commands.

            if (cmd.seq <= _lastProcessedSeq)
            {
                LogRejectThrottled("[Movement] Sequence rejected (stale/duplicate)");
                return;
            }

            // After the stale check above, cmd.seq > _lastProcessedSeq, so this
            // subtraction cannot wrap. A jump larger than the client's buffer capacity
            // is de-sync traffic; the bound also caps the pending-queue size a flood
            // can create.
            if (cmd.seq - _lastProcessedSeq > CommandBufferCapacity)
            {
                LogRejectThrottled("[Movement] Sequence rejected (gap too large)");
                return;
            }

            // NaN/Infinity is deviant traffic. Queue a zero-input command instead of
            // dropping it, so ack cadence and command ordering stay uniform.
            if (float.IsNaN(cmd.input.x) || float.IsNaN(cmd.input.y) ||
                float.IsInfinity(cmd.input.x) || float.IsInfinity(cmd.input.y))
            {
                LogRejectThrottled("[Movement] Invalid movement input rejected");
                cmd.input = Vector2.zero;
            }
            else
            {
                // The server is authoritative over the final input magnitude.
                cmd.input = Vector2.ClampMagnitude(cmd.input, 1f);
            }

            // Eliminated players cannot move; zero their input before it enters the
            // queue (server-side only).
            if (_networkPlayer != null && _networkPlayer.state.Value != PlayerState.Alive)
            {
                cmd.input = Vector2.zero;
                LogRejectThrottled("[Movement] Player eliminated - movement disabled");
            }

            _pendingCommands.Enqueue(cmd);
        }

        private void ServerSimulate()
        {
            Vector3 pos = transform.position;
            bool stepped = false;

            if (IsOwner)
            {
                // Host: the local player is simulated directly, one step per tick.
                Vector2 input = ReadLocalInput();
                if (input != Vector2.zero) _lastMoveDirection = input.normalized;

                pos = StepPosition(pos, input, GetMoveSpeed() * Time.fixedDeltaTime);
                _lastProcessedSeq = _nextSeq - 1;
                stepped = true;
            }
            else
            {
                // Drain the backlog in original order, bounded to MaxServerStepsPerTick.
                // Never consolidate it into "latest input x count": that collapses
                // distinct commands into a step the client did not predict and cannot
                // replay. Leftovers are processed on subsequent ticks.
                int steps = Mathf.Min(_pendingCommands.Count, MaxServerStepsPerTick);
                for (int i = 0; i < steps; i++)
                {
                    MoveCmd cmd = _pendingCommands.Dequeue();
                    _lastProcessedSeq = cmd.seq;

                    // Speed is resolved per command so rule changes mid-catch-up (orb
                    // pickup, elimination, match state) apply to in-flight commands.
                    pos = StepPosition(pos, cmd.input, GetMoveSpeed() * Time.fixedDeltaTime);
                }

                stepped = steps > 0;
            }

            if (!stepped) return;

            transform.position = pos;

            var ack = new MoveAck { seq = _lastProcessedSeq, pos = pos };
            if (!_moveAck.Value.Equals(ack))
            {
                _moveAck.Value = ack;
            }
        }

        #endregion

        #region Movement math and helpers

        /// <summary>Advances a position by one input step, clamped to the arena bounds.</summary>
        private Vector3 StepPosition(Vector3 pos, Vector2 input, float travel)
        {
            Vector3 next = pos + new Vector3(input.x, 0f, input.y) * travel;
            next.x = Mathf.Clamp(next.x, -arenaHalfExtents.x, arenaHalfExtents.x);
            next.z = Mathf.Clamp(next.z, -arenaHalfExtents.y, arenaHalfExtents.y);
            return next;
        }

        /// <summary>
        /// Speed for the current step. On the server these values are authoritative; on
        /// the owner the same NetworkVariables are read from replication, so prediction
        /// tracks server behaviour with only a replication-latency delay before the next
        /// ack corrects it.
        /// </summary>
        private float GetMoveSpeed()
        {
            if (_networkPlayer == null) return moveSpeed;

            // Match state and alive state gate all movement.
            GameManager gm = GameManager.Instance;
            if (gm == null || gm.matchStateNv.Value != MatchState.Playing) return 0f;
            if (_networkPlayer.state.Value != PlayerState.Alive) return 0f;

            return _networkPlayer.hasOrb.Value ? moveSpeed * carrierSpeedMultiplier : moveSpeed;
        }

        /// <summary>Clears both the server queue and the owner prediction buffers.</summary>
        private void ResetMovementState()
        {
            _pendingCommands.Clear();
            _lastProcessedSeq = 0;
            _sentCommands.Clear();
            _nextSeq = 1;
            _lastAckedSeq = 0;
            _hasReconciled = false;
            _lastMoveDirection = Vector2.right;
        }

        private void LogRejectThrottled(string message)
        {
            float now = Time.time;
            if (now - _lastRejectLogTime < RejectLogCooldown) return;

            _lastRejectLogTime = now;
            Debug.Log(message);
        }

        #endregion
    }
}
