# NGOSmallPrototype — Unity NGO + Edgegap Multiplayer Prototype

A small technical-assessment prototype called **Core Rush**: a two-team realtime arena game built with **Unity Netcode for GameObjects (NGO)**, a **fully server-authoritative dedicated server** deployed as a **Linux container on Edgegap**, and an **Android touch client**.

The point of the project is the networking, not the art: server authority, NGO state replication, race-condition protection, timed interactions, server-side hit validation, disconnect cleanup, a Linux dedicated server, Edgegap deployment, and dynamic address/port configuration on the client.

> Deep dive: see **[Documentation/NetworkArchitecture.md](Documentation/NetworkArchitecture.md)** for the short networking architecture explanation.

<!-- TOC start -->

- [Submission contents](#submission-contents)
- [Game rules](#game-rules)
- [Requirements](#requirements)
- [Project structure](#project-structure)
- [Build the dedicated server](#build-the-dedicated-server)
  - [Dedicated-server configuration and startup](#dedicated-server-configuration-and-startup)
- [Build the Android client](#build-the-android-client)
  - [Install and run the mobile build](#install-and-run-the-mobile-build)
- [Edgegap deployment](#edgegap-deployment)
  - [One-time setup](#one-time-setup)
  - [Deploy with the Edgegap Unity plugin (recommended)](#deploy-with-the-edgegap-unity-plugin-recommended)
  - [Deploy / recreate from the API (scriptable)](#deploy--recreate-from-the-api-scriptable)
- [Port and protocol](#port-and-protocol)
- [Connecting clients with the current deployment info](#connecting-clients-with-the-current-deployment-info)
- [Reproduce the 3-client multiplayer test](#reproduce-the-3-client-multiplayer-test)
- [Verify server startup, deployment status and logs](#verify-server-startup-deployment-status-and-logs)
- [Known limitations and troubleshooting](#known-limitations-and-troubleshooting)
- [Security note](#security-note)
- [Demonstration video](#demonstration-video)

<!-- TOC end -->

## Submission contents

| # | Deliverable | Location in this repo |
| --- | --- | --- |
| 1 | Unity project / source | this repository (Unity `6000.6.0f1`) |
| 2 | Mobile client build (Android) | `Builds/NGOSmallPrototype.apk` |
| 3 | Unity dedicated-server build (Linux headless) | `Builds/EdgegapServer/` (see [build steps](#build-the-dedicated-server)) |
| 4 | README | this file |
| 5 | Networking architecture explanation | `Documentation/NetworkArchitecture.md` |
| 6 | Demonstration video (Edgegap + physical mobile + 3 clients) | link in [Demonstration video](#demonstration-video) |

## Game rules

- Two teams (Red / Blue), up to 8 players per match.
- **3 Cores** spawn neutral. Carry one to your base, stand still and **hold interact for 1 second** to score. Moving cancels the interaction. Dying with a Core drops it back to neutral.
- A shared **Orb** is a temporary powerup: throw it in a straight line for up to 3 s. Hitting an enemy eliminates them (server-validated); hitting a teammate / wall just recovers it. The Orb carrier moves at 50 % speed.
- **First team to score 2 Cores wins**, and a team also wins by **eliminating every enemy**.

## Requirements

- **Unity 6000.6.0f1 (Unity 6 LTS)**, with the **Linux Build Support (Server) module** and **Android Build Support** (SDK/NDK/JDK) installed.
- Packages (already pinned in `Packages/manifest.json`):
  - Netcode for GameObjects **2.13.3**
  - Unity Transport **6.6.0**
  - Input System **1.20.1** (`Active Input Handling = Input System Package (New)`)
  - Universal Render Pipeline **17.6.0**
  - uGUI (Unity UI) **2.6.0**
  - Multiplayer Play Mode **3.0.0** (dev only) and Multiplayer Tools **2.2.12**
  - Edgegap Unity plugin **`com.edgegap.unity-servers-plugin`** (git)
- For the Edgegap pipeline: **Docker Desktop** + a free **[Edgegap](https://app.edgegap.com/auth/register)** account.
- For testing: an Android device, or 2+ editor clients for a local test.

## Project structure

```
Assets/
  _Project/
    Scenes/Arena.unity          # the single scene used by both builds
    Prefabs/Player.prefab       # NetworkObject + NetworkPlayer + PlayerMovement + AnticipatedNetworkTransform
    Prefabs/Core.prefab         # NetworkObject + NetworkTransform + ObjectiveCore
    Prefabs/Orb.prefab          # NetworkObject + NetworkTransform + SharedOrb + OrbProjectile
    Scripts/
      Network/  (ConnectionManager, ServerConnection, ClientConnection, Abstract/ConnectionBase)
      Game/     (GameManager, MatchState, Team)
      Player/   (NetworkPlayer, PlayerMovement, PlayerState)
      Objective/(ObjectiveCore, ObjectiveState)
      Orb/      (SharedOrb, OrbProjectile, OrbState)
      UI/       (ConnectionUI, GameHUD)
  EdgegapServerBootstrap/        # Edgegap runtime port-verification bootstrap (server-only)
  InputSystem_Actions.inputactions
  DefaultNetworkPrefabs.asset
Builds/
  EdgegapServer/                 # Linux dedicated-server build output (Docker build context input)
  NGOSmallPrototype.apk          # Android touch client
Documentation/
  NetworkArchitecture.md         
```

`Assets/_Project/Scenes/Arena.unity` is the only enabled scene in `ProjectSettings/EditorBuildSettings.asset`, so both the Linux server and Android client build this scene. The scene contains the NetworkManager (TickRate 60), GameManager, ConnectionManager, the Edgegap `EdgegapServerBootstrap`, and the Canvas UI (`ConnectionUI`, `GameHUD`).

## Build the dedicated server

1. In Unity: **File > Build Profiles > Linux**.
2. Set the **Server** subtarget (this defines `UNITY_SERVER`, so the build is headless).
3. Build to **`Builds/EdgegapServer/ServerBuild`** (the exact path the Edgegap Dockerfile copies).
4. The result is a folder containing `ServerBuild` plus its `_Data` and `UnityPlayer.so` (Linux has no separate `.exe`).

### Dedicated-server configuration and startup

On a server build the `ConnectionManager.Start()` path detects dedicated-server mode (`UNITY_SERVER` / batch mode / `--server`) and calls `ServerConnection.StartServer()` automatically — no UI and no player input is required. The scene is loaded, NGO starts as server, the match manager spawns the 3 Cores and the Orb, and the server binds to `0.0.0.0` on the resolved port.

**Port resolution order** (`ServerConnection.ReadServerPort` — no port is hardcoded in the scene):

1. `ARBITRIUM_PORTS_MAPPING` environment variable — Edgegap injects a JSON port mapping; the server reads the **`internal`** port (this is what it must bind).
2. `-port <n>` command line argument.
3. `PORT` environment variable.
4. Fallback `7777` (`ConnectionBase.DefaultPort`).

**Local smoke test (before containerizing):**

```bash
./Builds/EdgegapServer/ServerBuild -batchmode -nographics -port 7777
# or:  PORT=7777 ./Builds/EdgegapServer/ServerBuild -batchmode -nographics
# then point one client at 127.0.0.1:7777
```

`-batchmode -nographics` keep the server fully headless. The Edgegap container runs the same binary with those flags (see below).

## Build the Android client

1. In Unity: **File > Build Profiles > Android > Switch Platform** (install Android Build Support if prompted).
2. Player settings used for this submission: **package name `com.UnityTechnologies.Universal3D`**, **minimum API level 26 (Android 8.0)**, **target API = Automatic (highest installed)**, orientation follows OS auto-rotation (best played in landscape), IL2CPP.
3. Add `Assets/_Project/Scenes/Arena.unity` as the only scene and build the APK to `Builds/NGOSmallPrototype.apk`.

Touch controls (all bound through the `InputSystem_Actions` asset):
- **Left joystick** → `Player/Move`
- **INTERACT** button → `Player/Interact` — context-sensitive: picks up a nearby Core or Orb, or deposits a carried Core at your base (one action, resolved on the server).
- **THROW** button → `Player/Throw` — available only while carrying the Orb.
- Desktop equivalents: WASD / arrows, `E`, `Space`.

### Install and run the mobile build

- **Tested device:** **Oppo Reno 5** (ColorOS), **Android OS version: `13`**.
- **Tested platform:** Android (min API 26 / Android 8.0). Any Android 8.0+ device should work.
- **Install over USB (recommended, no store required):**

  ```bash
  adb install -r Builds/NGOSmallPrototype.apk
  # launch:
  adb shell monkey -p com.UnityTechnologies.Universal3D -c android.intent.category.LAUNCHER 1
  ```

  Or copy the APK to the device and tap it, allowing "Install unknown apps" for the file manager if needed.

- On first launch the app shows the connection screen (see below). Leave the USB cable connected if you want `adb logcat` in parallel.

## Edgegap deployment

### One-time setup

1. Create an Edgegap account and an application (this project used the app name **`ngosmallprototype`**; the name is arbitrary).
2. Obtain the container registry credentials (needed once for `docker login`):

   ```bash
   curl -H "Authorization: token $EDGEGAP_API_TOKEN" \
     https://api.edgegap.com/v1/wizard/registry-credentials
   # -> { registry_url, project, username, token }
   docker login $registry_url -u $username --password-stdin
   ```

3. The app version must expose a port mapping: **internal `7777`, protocol `UDP`** (external port is assigned by Edgegap). This is what lets clients join.

> The API token is only needed locally to build/push/deploy. **No token, credential, or secret is stored in this repository.**

### Deploy with the Edgegap Unity plugin (recommended)

The `com.edgegap.unity-servers-plugin` does the packaging and deploy for you:

1. Open **Tools > Edgegap Server Hosting**.
2. Paste your API token when prompted (stored locally on your machine, never in the project).
3. Follow the window sections in order:
   - **Linux / Docker / server-build checks** — confirm the Linux build support and Docker Desktop are detected.
   - **Build the server** — produces `Builds/EdgegapServer/ServerBuild`.
   - **Containerize** — builds the image using the plugin's `Editor/Dockerfile` (see below).
   - **Create app** — app name + image/tag + **port mapping 7777/UDP**.
   - **Deploy** — creates a deployment and polls until status is `READY`.
   - **Stop** — terminates the active deployment.
4. When it reports `READY`, the window shows the **public address (`fqdn` / `public_ip`)** and the **external port** to enter in the client.

The plugin's Dockerfile is the packaging contract:

```dockerfile
FROM ubuntu:22.04
ARG SERVER_BUILD_PATH=Builds/EdgegapServer
COPY ${SERVER_BUILD_PATH} /root/build/
WORKDIR /root/
RUN chmod +x /root/build/ServerBuild
RUN apt-get update && apt-get install -y ca-certificates && apt-get clean && update-ca-certificates
CMD ["/bin/bash", "-c", "env;/root/build/ServerBuild -batchmode -nographics $UNITY_COMMANDLINE_ARGS"]
```

To **recreate a deployment**, stop the current one (Edgegap free tier allows only one active deployment) and press Deploy again with the same app/version, or push a new version tag and deploy that.

### Deploy / recreate from the API (scriptable)

Everything the plugin does can be driven from the Edgegap REST API:

```bash
# 1) create/update app + version (docker image/repo/tag, private credentials, ports[7777 UDP])
#    POST /v1/app
#    POST /v1/app/<app_name>/version

# 2) build + push the container
docker build -f <edgegap-plugin>/Editor/Dockerfile \
             --build-arg SERVER_BUILD_PATH=Builds/EdgegapServer \
             -t registry.edgegap.com/<project>/ngosmallprototype:v1 .
docker push registry.edgegap.com/<project>/ngosmallprototype:v1

# 3) create a deployment
#    POST https://api.edgegap.com/v2/deployments
#    { "application": "ngosmallprototype", "version": "v1",
#      "users": [ { "user_type": "ip_address", "user_data": { "ip_address": "<your public ip>" } } ] }
#    -> returns { "request_id": "..." }

# 4) poll until READY and read the endpoint
#    GET https://api.edgegap.com/v1/status/<request_id>
#    -> current_status: "Status.READY", plus fqdn / public_ip and ports[external]

# 5) stop / recreate
#    DELETE https://api.edgegap.com/v1/stop/<request_id>
```

## Port and protocol

- **Game transport:** Unity Transport over **UDP**.
- **Internal (container) port:** **7777** — the server binds this (`ServerConnection` reads it from `ARBITRIUM_PORTS_MAPPING.internal`, else `-port`, else `PORT`, else 7777).
- **External port:** assigned by Edgegap per deployment and is **not** the same as 7777. Clients connect to the **external** port.
- **How to obtain the public address / external port:**
  - Edgegap console: **Deployment Management > Deployments**, open the running deployment.
  - REST: `GET https://api.edgegap.com/v1/status/<request_id>` → `fqdn`, `public_ip`, and `ports` (use the `external` value for `7777/UDP`).
  - Or the plugin window's deployment result, which prints `fqdn` + external port on `READY`.

## Connecting clients with the current deployment info

No address is hardcoded anywhere. Every client (Android APK or editor) shows a Canvas connection screen:

```
CONNECT
Status: Idle
Server Address: [ e08633476038.pr.edgegap.net ]   <- deployment fqdn (or public_ip)
Port:           [ 32327 ]                          <- deployment external UDP port

[ HOST GAME ] [ START DEDICATED SERVER ] [ JOIN GAME ]
```

1. Get the current `fqdn`/`public_ip` and external port from the deployment (section above).
2. Enter **Server Address** = `fqdn` (recommended; survives IP changes) or `public_ip`, and **Port** = the deployment's external UDP port.
3. Press **JOIN GAME** (`StartClient`). The transport is configured at runtime with `transport.SetConnectionData(host, port)`, then `NetworkManager.StartClient()`.
4. Once connected, the setup screen collapses to a small status chip (`Host • N player(s)`) with a **LEAVE** button and the HUD appears.

> **Config compatibility:** NGO clients only join a server whose `NetworkConfig` (including `TickRate`) hashes match their own. If you change TickRate or other network config, rebuild clients and the server together, and enter Play mode *after* the change so the client re-reads the fresh config.

For a fully local test press **HOST GAME** on one client (that client is also a player); for a local dedicated test press **START DEDICATED SERVER** on one client, then **JOIN GAME** from the others to `127.0.0.1`.

## Reproduce the 3-client multiplayer test

Prerequisite: an Edgegap deployment in `READY` state (or a local server), and its `fqdn` + external port.

1. **Start the server** (deployment already running, or run the server build locally).
2. **Client A — physical Android device:** install `Builds/NGOSmallPrototype.apk`, launch it, enter the deployment `fqdn` + external port, press **JOIN GAME**.
3. **Client B — editor:** open `Assets/_Project/Scenes/Arena.unity`, press Play, enter the same `fqdn` + port, press **JOIN GAME**.
4. **Client C — second editor instance:** either enable **Window > Multiplayer Play Mode** (start one simulated player) or launch a second Unity Editor instance on the same scene; enter the same `fqdn` + port and **JOIN GAME**.
5. Verify the HUD on all three shows the same match state and `CLIENTS 3`. The match starts automatically once 2+ players are connected (3-2-1 countdown → `Playing`).
6. Exercise gameplay and confirm each is server-validated: move from the phone (others see it), race two players for the same Core (only one gets it), carry/scoring with the 1-second hold, Orb pickup (slower), a throw eliminating an enemy, and a disconnect while carrying (the object returns to `Available`).

**Local alternative (no Edgegap):** run the Linux/desktop server, then connect the phone and editor clients to the host machine's LAN IP on port `7777`; or press **HOST GAME** on one editor client and **JOIN GAME** from the others to that machine's address.

## Verify server startup, deployment status and logs

**Server startup (local):** run the server with `-batchmode -nographics` and watch stdout. On success you should see Unity's engine banner followed by:

```
[GameManager] Server started, cores spawned, WaitingForPlayers
```

Once a second player connects:

```
[GameManager] 2+ players connected, match Starting
[GameManager] Match Playing
```

The Edgegap `EdgegapServerBootstrap` also logs a warning at startup if the Unity Transport port/protocol does not match the Edgegap port mapping (look for `WARNING: No Edgegap Port Mapping for UnityTransport ...`).

**Deployment status (Edgegap):**

- Console: **Deployment Management > Deployments** — expect a running deployment with a green/`READY` status.
- REST: `GET https://api.edgegap.com/v1/status/<request_id>` — check `current_status`, `running`, `public_ip`/`fqdn`, and `ports`.
- The container's `CMD` begins with `env;`, so `ARBITRIUM_PORTS_MAPPING`, `ARBITRIUM_DEPLOYMENT_LOCATION`, etc. are printed at the top of the deployment logs — use them to confirm the port mapping Edgegap injected.

**Deployment logs:** open the deployment in the Edgegap console and use its **Logs** view; the same `[GameManager]` / `[Movement]` lines above appear there (they are `Debug.Log`, which goes to stdout on a headless build).

**On-device/mobile logs:** `adb logcat -s Unity` while reproducing; `UNITY_SERVER` is not defined on the client so the client never auto-starts a server.

## Known limitations and troubleshooting

**Known limitations**

- UI is intentionally minimal (Canvas-based `ConnectionUI` / `GameHUD`); no lobbies, matchmaking, authentication, accounts, or reconnection.
- One match per server instance; the match is destroyed (not migrated) when a team wins.
- Edgegap **free tier allows only one active deployment** — stop the previous deployment and wait for it to fully terminate before deploying again.
- No lag compensation: Orb hits validate against current server positions, so a high-latency throw can resolve slightly behind what the thrower saw.
- No sound, animation, or effects; meshes are Unity primitives.
- Server/scene config values (`maxPlayers`, `coresToWin`, `startingDurationSeconds`) live on components in `Arena.unity`, not in remote config.

**Troubleshooting**

- **"Already running - shutdown first"** when connecting — a session is already active; press **LEAVE** (`Shutdown`) before starting another.
- **Client cannot connect after a redeploy** — the external port changes on each deployment. Re-read it from `GET /v1/status/<request_id>` and re-enter it.
- **Client rejected with "Game is full (8 players)"** — the server reached `maxPlayers` (8).
- **Connect fails with a config-hash mismatch** — TickRate/NetworkConfig differ between client and server. Rebuild both together.
- **Clients connect but the server never leaves `WaitingForPlayers`** — fewer than 2 clients are actually connected (the match needs 2+).
- **Movement stutters on remote players** — the headless server keeps its loop at 120 FPS (`ConnectionManager.ConfigureServerLoop`) so NetworkTransform updates stay frequent; if you change this, expect stepped remote movement.
- **Deployment stuck / not `READY`** — check the deployment logs for a container crash; confirm the port mapping is `internal 7777 / UDP` and that `Builds/EdgegapServer/ServerBuild` exists and is executable in the image.
- **Docker build fails to find the server** — the build arg `SERVER_BUILD_PATH` defaults to `Builds/EdgegapServer`; make sure the Linux **Server** build was written there.

## Security note

This submission intentionally contains **no Edgegap API tokens, credentials, passwords, or other secrets**. The Edgegap API token is entered locally in the editor (or supplied as an environment variable to `curl`) and is never committed to the repository or embedded in either build.

## Demonstration video

shows the Edgegap deployment/connectivity, the Android build running on the physical device (Oppo Reno 5), and 3-client multiplayer gameplay.

- **Link:** `<shared video link will be here>`
