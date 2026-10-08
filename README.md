# ngosmallprototype - Unity NGO + Edgegap Multiplayer Prototype

A technical-assessment prototype: a **Simple Realtime Game ** with the classic Core Rush rules, built with Unity Netcode for GameObjects (NGO), a **fully server-authoritative** design, and deployed as a **Linux headless dedicated server** on **Edgegap**, with an **Android touch client**.

<!-- TOC start -->

- [Game rules](#game-rules)
- [Architecture](#architecture)
  - [Authority model](#authority-model)
  - [Movement pipeline](#movement-pipeline)
  - [Core interaction](#core-interaction)
  - [Match state machine](#match-state-machine)
  - [Disconnect cleanup](#disconnect-cleanup)
  - [Key scripts](#key-scripts)
- [Requirements](#requirements)
- [Project structure](#project-structure)
- [Build instructions](#build-instructions)
  - [Editor play (dev loop)](#editor-play-dev-loop)
  - [Android client](#android-client)
  - [Linux dedicated server](#linux-dedicated-server)
- [Edgegap deployment](#edgegap-deployment)
  - [One-time setup](#one-time-setup)
  - [Deploy pipeline (scriptable)](#deploy-pipeline-scriptable)
- [Connection instructions](#connection-instructions)
- [Server port configuration](#server-port-configuration)
- [Known limitations](#known-limitations)
- [Demo recording script](#demo-recording-script)

<!-- TOC end -->

## Game rules

- Two teams (Red / Blue), 8 players max per match (4 red + 4 blue balance cap).
- **3 Cores** spawn neutral in the arena. Teammates race to pick them up.
  - With a Core, run it to **your base**, stand still and **hold interact for 1 second** to score. Moving cancels the interaction.
  - Dying with a Core drops it back to neutral (Available state).
- A **shared Orb** provides a temporary powerup (exclusive possession).
  - Throw the Orb: it flies in a straight line for up to 3 s.
  - **Hitting an enemy player eliminates them** (server-validated hit). Hitting a teammate, the thrower, or a wall/floor just recovers the Orb.
- **First team to score 2 Cores wins**. A team also wins by **eliminating every enemy** (wipe).
- Server-spawned Objectives only spawn when a match starts; the match ends with a `Finished` state visible on all clients.

## Architecture

### Authority model

The dedicated server is **authoritative for everything**. Clients only *request*:

- **Movement**: client -> `PlayerMovement` sends input (via a 20-to-60-Hz **send-on-change** `Rpc`), server clamps, validates and integrates; the owner's `NetworkTransform` snapshot keeps every client in sync.
- **Possession**: first *valid* request wins. Race conditions are resolved on the server using an exclusive state check (ObjectiveState/OrbState transitions are checked-and-set inside the interaction).
- **Hits**: `OrbProjectile` collision handling runs 100% on the server (`OnCollisionEnter` is only executed server-side); clients see the replicated result.
- **Scores / state**: all replicated values (`NetworkVariable`) - scores, core states, match state, player team/state - are set only on the server.

### Movement pipeline

```
Client input (Input System: WASD / joystick)
  -> send-on-change Rpc (60 Hz cap)
    -> server clamp + integrate (60-tick rate)
      -> NetworkTransform snapshot (interpolated, 50 ms catch-up)
```

### Core interaction

1. Client presses the interact/pickup action -> one small request Rpc.
2. Server validates: state must be `Available` (pickup) or the carrier must be the rightful team standing in-base (deposit).
3. A 1-second server-timer is used to verify deposit success; movement during that window cancels it.
4. If the carrier dies or leaves, the Core is released back to `Available` server-side.

### Match state machine

`WaitingForPlayers -> Starting (3 s replicated countdown) -> Playing -> Finished`

Scoring, the 3-second pre-round and the finished state are all `NetworkVariable`-driven so a late-joining client always sees consistent state.

### Disconnect cleanup

`GameManager.ServerOnClientDisconnect` (server-side) releases any carried Core / held Orb, re-balances team counters and despawns the per-client player object (players use `DontDestroyWithOwner` + manual server despawn).

### Key scripts

| Script | Role |
| --- | --- |
| `ConnectionManager` | IMGUI connect panel (Host/Port/Connect) + dedicated-server auto-start; delegates to the connection components below |
| `ConnectionBase` | Shared connection plumbing: transport config/validation guard + shared default port (`7777`) |
| `ServerConnection` | `StartServer` + dedicated-server port resolution (`ARBITRIUM_PORTS_MAPPING` -> `-port` -> `PORT` env -> 7777) |
| `ClientConnection` | `StartHost` / `StartClient` (client-side connect logic) |
| `NetworkPlayer` | Per-player replicated state (`playerTeam`, `state`, `hasOrb`, `carriedCoreId`), interact/throw request Rpcs, Input System actions |
| `PlayerMovement` | Server-authoritative movement; client only sends a normalized 2D input |
| `GameManager` | Match state machine, team assignment + spawn positioning, victory checks, elimination handling, connection approval |
| `ObjectiveCore` | Core state machine + 1-second deposit timer (server-only logic) |
| `SharedOrb` / `OrbProjectile` | Orb possession / throwing / flight timer, server-validated hit targeting |
| `GameHUD` | Minimal IMGUI read-out of match state / scores / local player |

**Namespace layout:** `_Project.Scripts.{Network, Player, Objective, Orb, Game, UI}` (anchored under `Assets/_Project/`).

## Requirements

- **Unity 6000.6.0f1 (Unity 6 LTS)** - URP (Mobile RP asset included)
- Packages: NGO 2.13.3, Unity Transport 6.6.0, Input System 1.20.1 (`Active Input Handling = Input System`), Multiplayer Play Mode 3.0.0 (dev-only), Edgegap plugin (`com.edgegap.unity-servers-plugin`)
- For the Edgegap pipeline: **Docker Desktop** + **Linux Build Support module** + a free **[Edgegap](https://app.edgegap.com/auth/register)** account
- Android device for the mobile test, or 2+ editor clients for a local test

## Project structure

```
Assets/
  _Project/
    Scenes/Arena.unity          # single scene: everything (layout, spawn markers, NetworkManager, GameManager, HUD)
    Prefabs/Player.prefab       # NetworkObject + NetworkTransform + NetworkPlayer + PlayerMovement
    Prefabs/Core.prefab         # NetworkObject + NetworkTransform + ObjectiveCore
    Prefabs/Orb.prefab          # NetworkObject + NetworkTransform + SharedOrb + OrbProjectile
    Scripts/...                   # the architecture table above
  EdgegapServerBootstrap/       # Edgegap runtime port-verification bootstrap (server-only)
  InputSystem_Actions.inputactions   # Player/Move, Player/Interact, Player/Throw
  DefaultNetworkPrefabs.asset   # registered: Player, Core, Orb
Builds/
  EdgegapServer/                # Linux dedicated server build output (container input)
  CoreRush-Android.apk          # Android touch client
```

## Build instructions

### Editor play (dev loop)

1. Open `Assets/_Project/Scenes/Arena.unity`.
2. Press the connection panel **Start Host** - the host client also counts as a player.
3. For a second player without a build, enable **Multiplayer Play Mode** (`Window > Multiplayer Play Mode`) and start a simulated client; or run a second editor instance.

### Android client

Benchmarked target - Android 8+, landscape:

```bash
# In Unity: File > Build Profiles > Android > Switch Platform
# Build Settings scenes: Assets/_Project/Scenes/Arena.unity
# Output: Builds/CoreRush-Android.apk
```

Touch controls (all via the `InputSystem_Actions` asset):
- **Left joystick** -> `Player/Move`
- **PICKUP / INTERACT** buttons -> `Player/Interact`
- **THROW** button -> `Player/Throw`
- Desktop equivalents: WASD/arrows, `E`, `Space`.

### Linux dedicated server

```bash
# In Unity: File > Build Profiles > Linux (Server sub-target)
# Output: Builds/EdgegapServer/ServerBuild
```

Headless: the build defines `UNITY_SERVER`, so `ConnectionManager` skips every visual path (no UI audio/sprites), starts `NetworkManager` automatically, and binds the resolved port. The game never presents a window.

**Local smoke test (optional, before containerizing)**:

```bash
./Builds/EdgegapServer/ServerBuild -port 7777 # or PORT=7777 ./Builds/EdgegapServer/ServerBuild
# then a client -> 127.0.0.1:7777
```

## Edgegap deployment

### One-time setup

1. Create an app **ngosmallprototype** (any name; below the deploy script uses `ngosmallprototype`).
2. Plugin tooling used here is API-only; either configure once through `Tools > Edgegap Hosting` (records the API token), or skip the UI and drive the API directly (next section).
3. Docker registry login - retrieve container-registry credentials:

```bash
curl -H "Authorization: token $EDGEGAP_API_TOKEN" https://api.edgegap.com/v1/wizard/registry-credentials
# -> { registry_url, project, username, token } -> docker login $registry_url -u $username --password-stdin
```

### Deploy pipeline (scriptable)

Everything needed for a fresh deployment:

```bash
# 1) install the Edgegap plugin + verify the app/version exists:
#    POST /v1/app                              { "name": "ngosmallprototype", "is_active": true, "image": "<base64 name>" }
#    POST /v1/app/ngosmallprototype/version             (docker_image/repo/tag, private_username/token, ports[7777 UDP])

# 2) build + push
docker build -f <EdgegapPluginEditor>/Dockerfile \
             --build-arg SERVER_BUILD_PATH=Builds/EdgegapServer \
             -t registry.edgegap.com/<project>/ngosmallprototype:v1 .
docker push registry.edgegap.com/<project>/ngosmallprototype:v1

# 3) deploy
POST https://api.edgegap.com/v2/deployments
     { "application": "ngosmallprototype", "version": "v1",
       "users": [ { "user_type": "ip_address", "user_data": { "ip_address": "<public ip>" } } ] }

# 4) poll until ready
GET https://api.edgegap.com/v1/status/<request_id>     # -> current_status: Status.READY
# final public endpoint:
GET https://api.edgegap.com/v1/status/<request_id>     # -> public_ip, fqdn, ports[external]
```

> The app version's **port mapping** (`internal: 7777 / external: <auto> / protocol: UDP`) is what makes clients able to join; the plugin's `EdgegapServerBootstrap` object (already added to `Arena.unity`) validates the mapping in the container log at startup.

> **Free-tier note**: only 1 active deployment is allowed - `DELETE /v1/stop/<request_id>` the old one (and allow it to fully terminate) before deploying again.

## Connection instructions

No address is hardcoded anywhere. Every client (editor or APK) gets a tiny IMGUI panel:

```
Connection
Status: Idle
Host: [ e08633476038.pr.edgegap.net ]     <- deployment fqdn (or public_ip)
Port: [ 32327 ]                            <- deployment external UDP port

[ Start Host ] [ Start Server ] [ Start Client ]
```

1. Get `public_ip` / `fqdn` + `external` port from `GET /v1/status/<request_id>` (or the Edgegap console).
2. Type them into the client on your Android device or a second editor instance and press **Start Client**.
3. The first player can also press **Start Host** for a fully local LAN test (any Wi-Fi endpoint).

> **Config-compatibility:** NGO clients join a deployment whose `NetworkConfig` hash matches their own. Since `TickRate` is part of that hash, an editor session opened before the TickRate change will fail to connect to a redeployed server - enter play mode **after** the scene change so the client re-reads the fresh config.

## Server port configuration

Resolution order (dedicated server only, `ServerConnection.ReadServerPort`):

1. `ARBITRIUM_PORTS_MAPPING` env (Edgegap injects the port-mapping JSON; we bind the **internal** port) - first,
2. `-port N` command line,
3. `PORT` env,
4. fallback `7777` (`ConnectionBase.DefaultPort`).

Documented mechanism: **no hardcoded port** in any scene/build artifact.

## Known limitations

- **No client-side prediction** for movement (deliberately out of assesment deadline scope). The client *feels* RTT + ~50 ms on its own movement. Future work: client prediction with server reconciliation, and smoothing the remote players' interpolation buffer.
- UI is debug-grade IMGUI (OnGUI), no game-feel/minimap. Swapping to a real uGUI UI is a drop-in.
- Single deployment/game instance; **no matchmaking service**, no session handoff, no "teams fill over time" (free tier permits 1 deployment).
- Org settings like `maxPlayers` and `startingDurationSeconds` are serialized on `GameManager`, not remotely configurable.
- Match is destroyed (not migrated) when a team wipes; Last-Man-Standing can also win by elimination - rejoining after a match requires restarting the server deployment.
- TextMeshPro is deliberately not used (vanilla uGUI has no text in this prototype; scores are IMGUI labels).
- No sound, no effects, no animation; meshes are Unity primitives (spheres/capsules/cylinders) to keep assesment scope.
- Linux servers are built from the *editor's current platform settings*; switching back between a Linux Server build and the Android client affects `EditorUserBuildSettings`, not the project.
