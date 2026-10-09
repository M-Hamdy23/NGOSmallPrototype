# Networking Architecture

a short explanation of how the prototype is networked. The guiding rule throughout is: **the dedicated server decides, clients only request**, and clients are told the result via replicated state.

## Topology

```
                  Edgegap (container orchestration)
                            |
                 Linux dedicated server (headless, UDP)
                            |
          +-----------------+-----------------+
          |                 |                 |
    Android client     Editor client 1    Editor client 2
```

All game traffic is a single Unity Transport **UDP** connection per client to the dedicated server. There is no peer-to-peer traffic and no relay; clients never talk to each other directly. The server is the single source of truth for match state, teams, movement, objectives, possession, hits and scoring.

## Authority model

- The server owns every gameplay object (Player, Core, Orb) and every decision.
- Clients send **requests only** (movement inputs, pickup/interact/throw). They never send authoritative positions, speeds, hit reports or scores.
- All replicated gameplay state uses `NetworkVariable`s that only the server writes. Clients render what they receive.

| Concern | Client sends | Server does |
| --- | --- | --- |
| Movement | sequenced input only (`MoveCmd`) | validates, integrates in order, publishes `MoveAck` |
| Pickup / deposit | request Rpc | state check-and-set, 1 s timer, scoring |
| Orb | pickup / throw-direction request | possession, throw, collision, hit, elimination |
| Match | nothing | state machine, countdown, victory |

## Connection and transport

- `ConnectionBase` (`Abstract/ConnectionBase.cs`) is the shared plumbing: it fetches the `UnityTransport` on the `NetworkManager`, validates that no session is already running, then calls `transport.SetConnectionData(host, port)`.
- `ClientConnection` implements `StartHost` / `StartClient`.
- `ServerConnection` implements `StartServer` and resolves the bind port with no hardcoding: `ARBITRIUM_PORTS_MAPPING.internal` (Edgegap) → `-port` arg → `PORT` env → fallback `7777`.
- `ConnectionManager` detects dedicated-server mode (`UNITY_SERVER` / batch mode / `--server`) and auto-starts the server; it also raises the headless frame rate to 120 so transform replication stays smooth.
- The Edgegap `EdgegapServerBootstrap` (in `Assets/EdgegapServerBootstrap/`) parses `ARBITRIUM_PORTS_MAPPING` at startup and logs a warning if the transport's port/protocol does not match the deployment's port mapping.

The public Edgegap address and external port are **runtime inputs** on the client (`ConnectionUI` host/port fields), never compiled in.

## Movement (server-authoritative with client prediction)

`PlayerMovement.cs` implements:

```
Owner client (FixedUpdate)
  sample Input System / joystick -> clamp to unit length
  -> MoveCmd { seq, input }  (input only, never position/speed)
     -> predict locally (same StepPosition math)
     -> MoveCmdServerRpc (reliable, owner-only)

Server
  validate at ingest: reject stale/duplicate/gap-too-large sequences,
                      zero NaN/Infinity, clamp magnitude,
                      zero input if eliminated or match not Playing
  -> integrate commands strictly in original order (<= 8 per tick)
  -> publish MoveAck { seq, pos } through a NetworkVariable

Owner
  on ack: snap to authoritative pos, replay every still-un-acked command
Others
  render via NetworkTransform + AnticipatedNetworkTransform interpolation
```

Key properties:

- The client predicts for responsiveness, but prediction only affects the local visual; the server's `MoveAck` always wins on reconciliation.
- Speed is resolved from server state per command (`GameManager.matchState`, `PlayerState`, orb possession → 50 % carrier speed) and arena bounds are clamped on both sides; the client cannot choose any of these.
- Backlogs are drained in order, never collapsed into "latest input × count", so replay stays deterministic.

## Gameplay systems

**Objective / race condition (`ObjectiveCore.cs`).** Cores are server-owned; gameplay possession is a `CarrierClientId` value, not an NGO ownership transfer. Each pickup is a check-and-set on the server: the first valid request flips `Available → Carried`; a second simultaneous request sees a non-`Available` state and is rejected. Deposit starts a **1-second server timer** and snaps the Core onto a free pad in the carrier's base (2 pads per base, reserved on the server, so scored Cores never overlap); leaving the base, dying or disconnecting cancels it and returns the Core to its spawn.

**Orb (`SharedOrb.cs` / `OrbProjectile.cs`).** One shared Orb; possession is a single server-side carrier id. While carried, the server applies the reduced speed. A throw sends a **direction only** (never a target); the server owns the Rigidbody, resolves the collision in `OnCollisionEnter` (server-only), and eliminates an enemy on a valid hit.

**Elimination (`GameManager.ServerEliminatePlayer`).** Server sets `PlayerState.Eliminated` and releases any Core/Orb the player held, so nothing stays orphaned.

**Match state machine (`GameManager.cs`).** `WaitingForPlayers → Starting (3 s replicated countdown) → Playing → Finished`, all driven by `NetworkVariable`s. Start needs 2+ connected clients; victory is either **2 Cores scored** or **all opponents eliminated**, checked on the server.

**Disconnect cleanup (`GameManager.ServerOnClientDisconnect`).** Server-side: release carried Core/Orb, cancel interactions, rebalance team counters, despawn the player object (`DontDestroyWithOwner` + manual server despawn). The match keeps running.

## Message summary

- **ServerRpcs (requests):** `MoveCmdServerRpc`, Core pickup/interact, Orb pickup/throw.
- **ClientRpcs:** none required for gameplay state — persistent state is `NetworkVariable`-driven; presentation is derived on the client.
- **NetworkVariables:** match state, scores, countdown, winning team, player team/state/carriedCore/hasOrb, Core state/carrier, Orb state/carrier, movement `MoveAck`.

## Deliberate trade-offs

- No lag compensation: hits validate against current server positions.
- No reconnection; a finished match requires a fresh deployment/server.
- Remote players trail by roughly one interpolation window under heavy jitter.
- Movement RPCs are reliable for simplicity in this prototype; unreliable/sequenced delivery is the natural production evolution.
