using Unity.Netcode.Components;
using UnityEngine;

namespace _Project.Scripts.Player
{
    /// <summary>
    /// AnticipatedNetworkTransform applies authoritative snapshots immediately, which
    /// remote instances would render as stepping. This subclass renders remote players
    /// by converging on the latest authoritative position with a critically damped,
    /// velocity-aware spring.
    ///
    /// A plain first-order exponential smoothing (Lerp toward the target each frame)
    /// carries no velocity state, so when 60 Hz snapshots arrive the visual speed ripples
    /// each tick and reads as judder. <see cref="Vector3.SmoothDamp"/> keeps a velocity
    /// and makes the visual accelerate/decelerate continuously, removing the per-tick
    /// speed ripple while staying frame-rate independent.
    ///
    /// Writes happen in LateUpdate so they land after the base class applied the latest
    /// authoritative snapshot in the same frame. Owner instances are driven by
    /// <see cref="PlayerMovement"/> prediction and authority instances are left untouched.
    /// </summary>
    public class SmoothedAnticipatedNetworkTransform : AnticipatedNetworkTransform
    {
        #region Configuration

        [SerializeField, Tooltip("Approximate seconds for the visual to converge on the authoritative position. Smaller = snappier (less lag), larger = smoother.")]
        private float remoteSmoothTime = 0.08f;

        [SerializeField, Tooltip("Maximum convergence speed (m/s), so a remote can never visibly sprint to catch up to a correction.")]
        private float remoteMaxSpeed = 30f;

        [SerializeField, Tooltip("Distance beyond which the remote snaps instead of gliding (teleports, respawn).")]
        private float remoteSnapDistance = 1f;

        #endregion

        #region State

        private Vector3 _visualPos;
        private Vector3 _visualVelocity;
        private bool _hasAuthoritativeState;
        private bool _hasVisual;

        #endregion

        #region Rendering

        private void LateUpdate()
        {
            if (!IsSpawned || !_hasAuthoritativeState) return;
            if (CanCommitToTransform || IsOwner) return;

            Vector3 authoritative = AuthoritativeState.Position;

            if (!_hasVisual)
            {
                _visualPos = authoritative;
                _visualVelocity = Vector3.zero;
                _hasVisual = true;
                transform.position = authoritative;
                return;
            }

            if ((authoritative - _visualPos).sqrMagnitude > remoteSnapDistance * remoteSnapDistance)
            {
                // Teleport / respawn-sized correction: snap instead of gliding across the arena.
                _visualPos = authoritative;
                _visualVelocity = Vector3.zero;
            }
            else
            {
                _visualPos = Vector3.SmoothDamp(
                    _visualPos,
                    authoritative,
                    ref _visualVelocity,
                    remoteSmoothTime,
                    remoteMaxSpeed,
                    Time.deltaTime);
            }

            transform.position = _visualPos;
        }

        #endregion

        #region Lifecycle

        protected override void OnNetworkTransformStateUpdated(ref NetworkTransformState oldState, ref NetworkTransformState newState)
        {
            base.OnNetworkTransformStateUpdated(ref oldState, ref newState);
            _hasAuthoritativeState = true;
        }

        public override void OnNetworkDespawn()
        {
            _hasAuthoritativeState = false;
            _hasVisual = false;
            _visualVelocity = Vector3.zero;
            base.OnNetworkDespawn();
        }

        #endregion
    }
}
