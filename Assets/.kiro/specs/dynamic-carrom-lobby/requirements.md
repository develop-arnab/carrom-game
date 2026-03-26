# Requirements Document

## Introduction

This feature upgrades the Carrom game lobby from a simple auto-connecting 1v1 system to a dynamic, Host-controlled 1-to-4 player lobby. The new lobby supports public matchmaking, private friend invites via join code, and invisible Ghost Bots that silently fill empty seats. The system follows a strict Offline-First paradigm: no network connections are created until the player explicitly clicks an action button. Phase 1 covers UI scaffolding, offline configuration, and dropdown interlock logic in `StartGameLobbyManager.cs`.

## Glossary

- **StartGameLobbyManager**: The MonoBehaviour singleton (extending `SingletonNetwork<T>`) that owns all lobby UI state and orchestrates the offline-to-online transition.
- **LobbyManager**: The service wrapper around Unity Lobby SDK calls (create, quick-join, heartbeat, polling).
- **StartGameManager**: The service wrapper around Unity Relay SDK calls and `NetworkManager.StartHost()` / `StartClient()`.
- **NetworkManager**: Unity Netcode for GameObjects singleton that manages the multiplayer session.
- **Ghost Bot**: An invisible AI-controlled player profile injected server-side to fill an empty seat. Never shown in the UI as a selectable or addable entity.
- **Host**: The player who created the lobby and runs the server-authoritative NGO session.
- **Client**: Any player who joined an existing lobby session.
- **Slot Panel**: A UI panel representing one player seat in the lobby. Visible count matches the selected player count.
- **Offline-First**: The architectural rule that no network calls occur until the player explicitly triggers an action button.
- **3-Player Interlock**: The rule that Classic game mode is incompatible with a 3-player count and must be automatically overridden to Freestyle.
- **GameMode**: An enum with values `Freestyle` and `Classic` representing Carrom rule sets.
- **netCarromRuleset**: A `NetworkVariable<GameMode>` owned by the server, synced to all clients after spawn.
- **Ready State**: A per-client boolean indicating the client is prepared to start the game. Defaults to `true` on join.
- **IsSpawned Guard**: A code-level check (`IsSpawned == true`) that must precede any write to a `NetworkVariable` or any call that requires an active NGO session.

---

## Requirements

### Requirement 1: Offline-First Scene Entry

**User Story:** As a player, I want to land in the lobby scene without any network activity, so that I can configure my game before committing to a connection.

#### Acceptance Criteria

1. THE `StartGameLobbyManager` SHALL NOT call `NetworkManager.Singleton.StartHost()`, `StartClient()`, or any Unity Relay or Unity Lobby service method inside `Start()` or `Awake()`.
2. WHEN the lobby scene loads, THE `StartGameLobbyManager` SHALL initialize all UI controls (dropdowns, slot panels, buttons) using only local in-memory state.
3. WHEN the lobby scene loads, THE `StartGameLobbyManager` SHALL display the Game Mode dropdown defaulting to `Freestyle` and the Player Count dropdown defaulting to `2`.
4. WHEN the lobby scene loads, THE `StartGameLobbyManager` SHALL hide the `startGameButton` and the `readyButton` until `OnNetworkSpawn` is called.
5. IF `UnityServices` is not yet initialized when the scene loads, THEN THE `StartGameLobbyManager` SHALL defer all service initialization to the moment an action button is clicked.

---

### Requirement 2: Game Mode and Player Count Configuration

**User Story:** As a player, I want to select a game mode and player count before going online, so that the lobby is configured to my preference before any network session starts.

#### Acceptance Criteria

1. THE `StartGameLobbyManager` SHALL expose a `TMP_Dropdown` for Game Mode with options `Freestyle` (index 0) and `Classic` (index 1).
2. THE `StartGameLobbyManager` SHALL expose a `TMP_Dropdown` for Player Count with options `2` (index 0), `3` (index 1), and `4` (index 2).
3. WHEN the Game Mode dropdown value changes, THE `StartGameLobbyManager` SHALL update the `_localGameMode` field to the corresponding `GameMode` enum value.
4. WHEN the Player Count dropdown value changes, THE `StartGameLobbyManager` SHALL update the `_localPlayerCount` field to the integer value `(dropdownIndex + 2)`.
5. WHEN `_localPlayerCount` is set to any value, THE `StartGameLobbyManager` SHALL call `ApplyPlayerCountToUI` to activate the correct number of slot panels.
6. THE `StartGameLobbyManager` SHALL activate exactly `_localPlayerCount` slot panels and deactivate all remaining slot panels in the `slotPanels` array.

---

### Requirement 3: 3-Player Interlock

**User Story:** As a player, I want the system to automatically prevent an invalid Classic + 3-player configuration, so that I never accidentally start a game with an unsupported rule set.

#### Acceptance Criteria

1. WHEN the Player Count dropdown is changed to `3` AND `_localGameMode` is `Classic`, THE `StartGameLobbyManager` SHALL set `_localGameMode` to `Freestyle` and update the Game Mode dropdown value to `0` (Freestyle).
2. WHEN the Game Mode dropdown is changed to `Classic` AND `_localPlayerCount` is `3`, THE `StartGameLobbyManager` SHALL set `_localGameMode` to `Freestyle` and update the Game Mode dropdown value to `0` (Freestyle).
3. WHEN the interlock triggers, THE `StartGameLobbyManager` SHALL log a message indicating that Classic mode was overridden due to the 3-player constraint.
4. IF `IsSpawned` is `true` AND `IsServer` is `true` WHEN the interlock triggers, THEN THE `StartGameLobbyManager` SHALL update `netCarromRuleset.Value` to `GameMode.Freestyle` after correcting `_localGameMode`.

---

### Requirement 4: Slot Panel Visual Feedback

**User Story:** As a player, I want to see the correct number of player slots update in real time as I change the player count, so that I have a clear visual representation of the lobby I am configuring.

#### Acceptance Criteria

1. THE `StartGameLobbyManager` SHALL accept a serialized array of exactly 4 `GameObject` references named `slotPanels` in the Unity Inspector.
2. WHEN `ApplyPlayerCountToUI` is called with a count of `N`, THE `StartGameLobbyManager` SHALL call `SetActive(true)` on `slotPanels[0]` through `slotPanels[N-1]` and `SetActive(false)` on all remaining elements.
3. IF the `slotPanels` array is null or empty, THEN THE `StartGameLobbyManager` SHALL skip slot panel updates without throwing an exception.
4. IF a specific element in `slotPanels` is null, THEN THE `StartGameLobbyManager` SHALL skip that element without throwing an exception.

---

### Requirement 5: Action Button Triggers (Online Entry Points)

**User Story:** As a player, I want two distinct entry points — public matchmaking and private invite — so that I can choose how I want to find opponents.

#### Acceptance Criteria

1. WHEN the `[Play Online]` button is clicked, THE `StartGameLobbyManager` SHALL set `_isPrivateLobby` to `false`, call `InitializeNetworkServices()`, and then call `LobbyManager.Instance.QuickJoinOrCreatePublicLobby()`.
2. WHEN the `[Invite Friends]` button is clicked, THE `StartGameLobbyManager` SHALL set `_isPrivateLobby` to `true`, call `InitializeNetworkServices()`, and then call `LobbyManager.Instance.CreateLobby()` with `isPrivate: true` and `maxPlayers` equal to `_localPlayerCount`.
3. WHEN `[Invite Friends]` is clicked AND `LobbyManager.CreateLobby()` completes, THE `StartGameLobbyManager` SHALL remain in the lobby scene waiting for players to join rather than navigating away.
4. THE `StartGameLobbyManager` SHALL pass `_localPlayerCount` as the `maxPlayers` argument to `LobbyManager.Instance.CreateLobby()`.
5. IF `InitializeNetworkServices()` throws an exception, THEN THE `StartGameLobbyManager` SHALL log the error and SHALL NOT proceed to lobby creation or matchmaking.

---

### Requirement 6: Network Services Initialization Guard

**User Story:** As a developer, I want Unity Services initialization and anonymous sign-in to be idempotent and safe to call multiple times, so that repeated button presses or re-entry into the scene do not cause authentication errors.

#### Acceptance Criteria

1. WHEN `InitializeNetworkServices()` is called AND `UnityServices.State` is already `Initialized`, THE `StartGameLobbyManager` SHALL skip `UnityServices.InitializeAsync()` and proceed directly to the sign-in check.
2. WHEN `InitializeNetworkServices()` is called AND `AuthenticationService.Instance.IsSignedIn` is already `true`, THE `StartGameLobbyManager` SHALL skip `SignInAnonymouslyAsync()`.
3. THE `InitializeNetworkServices()` method SHALL be `async Task` and SHALL be awaited before any lobby or relay call is made.

---

### Requirement 7: LobbyManager Auto-Navigation Suppression

**User Story:** As a developer, I want `LobbyManager.CreateLobby()` to stop auto-navigating to the next scene immediately after lobby creation, so that the Host stays in the lobby waiting for players.

#### Acceptance Criteria

1. WHEN `LobbyManager.CreateLobby()` completes, THE `LobbyManager` SHALL NOT fire `OnLobbyStartGame` immediately after lobby creation.
2. WHEN `LobbyManager.CreateLobby()` completes, THE `LobbyManager` SHALL NOT set `alreadyStartedGame = true` or `IsHost = true` until the Host explicitly triggers game start.
3. THE `LobbyManager` SHALL expose a separate method or event path that the Host calls explicitly to signal game start, distinct from the lobby creation flow.
4. WHEN a private lobby is created, THE `LobbyManager` SHALL store the lobby join code in `LastLobbyCode` so the `StartGameLobbyManager` can display it in the `joinCodeText` UI element.

---

### Requirement 8: Authority UI Split (Host vs Client)

**User Story:** As a player, I want to see the correct action button for my role — Start Game for the Host and Ready for Clients — so that the UI reflects my authority in the session.

#### Acceptance Criteria

1. WHEN `OnNetworkSpawn` is called AND `IsServer` is `true`, THE `StartGameLobbyManager` SHALL activate `startGameButton` and deactivate `readyButton`.
2. WHEN `OnNetworkSpawn` is called AND `IsServer` is `false`, THE `StartGameLobbyManager` SHALL activate `readyButton` and deactivate `startGameButton`.
3. WHEN `OnNetworkSpawn` is called AND `IsServer` is `false`, THE `StartGameLobbyManager` SHALL set the Game Mode dropdown and Player Count dropdown to non-interactable.
4. WHEN `OnNetworkSpawn` is called AND `IsServer` is `true`, THE `StartGameLobbyManager` SHALL keep the Game Mode dropdown and Player Count dropdown interactable.
5. WHEN `OnNetworkSpawn` is called AND `IsServer` is `true` AND `LobbyManager.LastLobbyCode` is not empty, THE `StartGameLobbyManager` SHALL set `joinCodeText.text` to display the lobby code.

---

### Requirement 9: NetworkVariable Write Safety

**User Story:** As a developer, I want all writes to `netCarromRuleset` to be guarded by an `IsSpawned` check, so that NGO does not throw a write-before-spawn exception.

#### Acceptance Criteria

1. THE `StartGameLobbyManager` SHALL only write to `netCarromRuleset.Value` when `IsSpawned` is `true` AND `IsServer` is `true`.
2. WHEN `OnNetworkSpawn` is called AND `IsServer` is `true`, THE `StartGameLobbyManager` SHALL write `_localGameMode` to `netCarromRuleset.Value` as the first authoritative sync.
3. WHEN `OnGameModeChanged` is called AND `IsSpawned` is `false`, THE `StartGameLobbyManager` SHALL update only `_localGameMode` and SHALL NOT write to `netCarromRuleset`.
4. WHEN `netCarromRuleset.OnValueChanged` fires on a Client, THE `StartGameLobbyManager` SHALL update the Game Mode dropdown to reflect the new value.

---

### Requirement 10: Relay Allocation Size

**User Story:** As a developer, I want the Relay allocation to match the configured player count, so that the session supports the correct maximum number of connections.

#### Acceptance Criteria

1. WHEN `StartGameManager.CreateRelay()` is called, THE `StartGameManager` SHALL allocate a Relay with `maxConnections` equal to `(_localPlayerCount - 1)` rather than a hardcoded value.
2. THE `StartGameManager` SHALL receive the `_localPlayerCount` value from `StartGameLobbyManager` before calling `RelayService.Instance.CreateAllocationAsync()`.
3. IF `_localPlayerCount` is not set before `CreateRelay()` is called, THEN THE `StartGameManager` SHALL default to a `maxConnections` value of `1`.

---

### Requirement 11: Ready State Synchronization

**User Story:** As a Host, I want to see which clients are ready before I start the game, so that I can confirm all players are prepared.

#### Acceptance Criteria

1. THE `StartGameLobbyManager` SHALL track per-client ready state using a server-side data structure (not a local bool on the client) so that ready state is visible to the Host.
2. WHEN a Client connects to the session, THE `StartGameLobbyManager` SHALL default that client's ready state to `true`.
3. WHEN a Client clicks the `readyButton`, THE `StartGameLobbyManager` SHALL toggle that client's ready state between `true` and `false` via a ServerRpc.
4. WHEN all connected human clients have a ready state of `true`, THE `StartGameLobbyManager` SHALL enable the `startGameButton` on the Host.
5. WHEN any connected human client has a ready state of `false`, THE `StartGameLobbyManager` SHALL disable the `startGameButton` on the Host.

---

### Requirement 12: Ghost Bot Injection

**User Story:** As a Host, I want empty seats to be silently filled by Ghost Bots when I start the game, so that the game can proceed without waiting for additional human players.

#### Acceptance Criteria

1. WHEN the Host clicks `startGameButton` AND the number of connected human clients is less than `_localPlayerCount`, THE `StartGameLobbyManager` SHALL calculate `botCount = _localPlayerCount - ConnectedClientsIds.Count` and store it in `CarromGameManager.PendingBotCount`.
2. THE `StartGameLobbyManager` SHALL NOT display any bot-related UI elements (no "Add Bot" button, no bot avatar, no bot name in slot panels).
3. WHEN `InjectGhostBots()` is called, THE `StartGameLobbyManager` SHALL store `_localPlayerCount` in `CarromGameManager.PendingPlayerCount`.
4. WHEN `InjectGhostBots()` is called AND `botCount` is greater than `0`, THE `StartGameLobbyManager` SHALL log the number of bots being injected.
5. THE `StartGameLobbyManager` SHALL only call `InjectGhostBots()` from within `StartGame()`, which is only callable by the server after `IsSpawned` is confirmed.

---

### Requirement 13: Singleton and NetworkObject Lifecycle Safety

**User Story:** As a developer, I want the `StartGameLobbyManager` singleton to handle re-entry into the scene gracefully, so that stale NetworkManager sessions do not cause spawn conflicts.

#### Acceptance Criteria

1. WHEN the lobby scene is loaded AND a previous `NetworkManager` session is still active, THE `StartGameLobbyManager` SHALL detect the conflict and shut down the previous session before proceeding.
2. WHEN `OnDisable` is called AND `IsSpawned` is `true` AND `IsServer` is `true`, THE `StartGameLobbyManager` SHALL unsubscribe from `NetworkManager.Singleton.OnClientDisconnectCallback`.
3. IF `NetworkManager.Singleton` is null when `OnDisable` is called, THEN THE `StartGameLobbyManager` SHALL skip the unsubscribe without throwing a null reference exception.
