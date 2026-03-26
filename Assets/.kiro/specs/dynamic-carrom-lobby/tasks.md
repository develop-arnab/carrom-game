# Implementation Plan: Dynamic Carrom Lobby

## Overview

Upgrade the Carrom lobby from a 1v1 auto-connecting system to a dynamic Host-controlled 1-to-4 player lobby. Three files are modified: `StartGameLobbyManager.cs`, `LobbyManager.cs`, and `StartGameManager.cs`. Implementation follows an Offline-First paradigm — no network calls until an action button is clicked.

## Tasks

- [x] 1. Patch LobbyManager.CreateLobby() to suppress auto-navigation
  - [x] 1.1 Add `isHostLobby = false` parameter to `CreateLobby()` signature
    - When `isHostLobby == true`, skip `OnLobbyStartGame?.Invoke(...)`, skip setting `alreadyStartedGame = true`, and skip setting `IsHost = true`
    - Always set `LastLobbyCode = lobby.LobbyCode` regardless of `isHostLobby`
    - _Requirements: 7.1, 7.2, 7.4_

  - [ ]* 1.2 Write property test for lobby code persistence round-trip (Property 5)
    - **Property 5: Lobby Code Persistence Round-Trip**
    - For any lobby created via `CreateLobby()`, `LastLobbyCode` must equal `lobby.LobbyCode` after the async call completes — regardless of `isHostLobby` value
    - Mock `LobbyService` to return a fake lobby with a known code; assert `LastLobbyCode` matches
    - **Validates: Requirements 7.4**
    - `// Feature: dynamic-carrom-lobby, Property 5: LastLobbyCode == lobby.LobbyCode after CreateLobby()`

  - [ ]* 1.3 Write unit tests for LobbyManager auto-navigation suppression
    - Test: `CreateLobby(isHostLobby: true)` does not fire `OnLobbyStartGame`
    - Test: `CreateLobby(isHostLobby: true)` does not set `IsHost = true`
    - Test: `CreateLobby(isHostLobby: false)` still fires `OnLobbyStartGame` (existing behavior preserved)
    - _Requirements: 7.1, 7.2_

- [x] 2. Add offline UI configuration to StartGameLobbyManager
  - [x] 2.1 Implement `OnGameModeChanged(int index)` and `OnPlayerCountChanged(int index)` dropdown callbacks
    - `OnGameModeChanged`: set `_localGameMode = (GameMode)index`
    - `OnPlayerCountChanged`: set `_localPlayerCount = index + 2`, then call `ApplyPlayerCountToUI(_localPlayerCount)`
    - Wire both callbacks to their respective `TMP_Dropdown.onValueChanged` listeners in `Start()`
    - Default `gameModeDropdown.value = 0` (Freestyle) and `playerCountDropdown.value = 0` (2 players) on scene load
    - _Requirements: 1.2, 1.3, 2.3, 2.4, 2.5_

  - [ ]* 2.2 Write property test for game mode dropdown mapping (Property 1)
    - **Property 1: Game Mode Dropdown Mapping**
    - For any index in {0, 1}: `OnGameModeChanged(index)` → `_localGameMode == (GameMode)index`
    - Run minimum 100 iterations with FsCheck generating values from {0, 1}
    - **Validates: Requirements 2.3**
    - `// Feature: dynamic-carrom-lobby, Property 1: OnGameModeChanged(index) => _localGameMode == (GameMode)index`

  - [ ]* 2.3 Write property test for player count dropdown mapping (Property 2)
    - **Property 2: Player Count Dropdown Mapping**
    - For any index in {0, 1, 2}: `OnPlayerCountChanged(index)` → `_localPlayerCount == index + 2`
    - Run minimum 100 iterations with FsCheck generating values from {0, 1, 2}
    - **Validates: Requirements 2.4**
    - `// Feature: dynamic-carrom-lobby, Property 2: OnPlayerCountChanged(index) => _localPlayerCount == index + 2`

  - [x] 2.4 Implement `ApplyPlayerCountToUI(int count)` with null safety
    - Iterate `slotPanels`; call `SetActive(i < count)` on each non-null element
    - Return early without exception if `slotPanels` is null or empty
    - Skip null elements without exception
    - _Requirements: 2.6, 4.1, 4.2, 4.3, 4.4_

  - [ ]* 2.5 Write property test for slot panel activation invariant (Property 3)
    - **Property 3: Slot Panel Activation Invariant**
    - For any N in {2, 3, 4} and any null pattern in `slotPanels`: `ApplyPlayerCountToUI(N)` activates exactly the first N non-null panels and deactivates the rest; no exception thrown for null elements or null array
    - **Validates: Requirements 2.6, 4.2, 4.3, 4.4**
    - `// Feature: dynamic-carrom-lobby, Property 3: ApplyPlayerCountToUI(N) activates exactly N panels`

- [x] 3. Implement 3-Player Interlock
  - [x] 3.1 Add interlock logic inside `OnPlayerCountChanged` and `OnGameModeChanged`
    - In `OnPlayerCountChanged`: if new count is 3 and `_localGameMode == Classic`, force `_localGameMode = Freestyle`, set `gameModeDropdown.value = 0`, log override message
    - In `OnGameModeChanged`: if new mode is Classic and `_localPlayerCount == 3`, force `_localGameMode = Freestyle`, set `gameModeDropdown.value = 0`, log override message
    - After correcting `_localGameMode`, if `IsSpawned && IsServer`, write `netCarromRuleset.Value = GameMode.Freestyle`
    - _Requirements: 3.1, 3.2, 3.3, 3.4_

  - [ ]* 3.2 Write unit tests for 3-Player Interlock examples
    - Test: Classic → 3P forces Freestyle and logs message
    - Test: 3P → Classic forces Freestyle and logs message
    - Test: Classic → 2P does not trigger interlock
    - Test: Classic → 4P does not trigger interlock
    - _Requirements: 3.1, 3.2, 3.3_

- [x] 4. Checkpoint — Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

- [x] 5. Implement NetworkVariable write safety and OnNetworkSpawn UI split
  - [x] 5.1 Add `IsSpawned && IsServer` guard to all `netCarromRuleset.Value` writes
    - In `OnNetworkSpawn` (server path): write `_localGameMode` to `netCarromRuleset.Value` as initial sync
    - In `OnGameModeChanged`: only write to `netCarromRuleset` when `IsSpawned && IsServer`
    - In interlock correction path: only write to `netCarromRuleset` when `IsSpawned && IsServer`
    - _Requirements: 9.1, 9.2, 9.3_

  - [x] 5.2 Implement `OnNetworkSpawn` authority UI split
    - Server path: activate `startGameButton`, deactivate `readyButton`, keep dropdowns interactable, display `LobbyManager.LastLobbyCode` in `joinCodeText` if non-empty
    - Client path: activate `readyButton`, deactivate `startGameButton`, set dropdowns non-interactable
    - Both paths: hide `startGameButton` and `readyButton` before `OnNetworkSpawn` fires (set inactive in `Start()`)
    - Subscribe `netCarromRuleset.OnValueChanged` to update `gameModeDropdown.value` on clients
    - _Requirements: 1.4, 8.1, 8.2, 8.3, 8.4, 8.5_

  - [ ]* 5.3 Write property test for NetworkVariable-to-dropdown sync (Property 6)
    - **Property 6: NetworkVariable-to-Dropdown Sync**
    - For any `GameMode` value V: when `netCarromRuleset.OnValueChanged` fires with new value V on a client, `gameModeDropdown.value` must equal `(int)V` after the callback executes
    - Mock the `NetworkVariable` change callback; assert dropdown value matches
    - **Validates: Requirements 9.4**
    - `// Feature: dynamic-carrom-lobby, Property 6: netCarromRuleset change => gameModeDropdown.value == (int)newMode`

  - [ ]* 5.4 Write unit tests for NetworkVariable write guard
    - Test: `OnGameModeChanged` when `IsSpawned == false` does not write to `netCarromRuleset`
    - Test: `OnNetworkSpawn` (IsServer) writes `_localGameMode` to `netCarromRuleset.Value`
    - _Requirements: 9.1, 9.2, 9.3_

- [x] 6. Implement network services initialization guard
  - [x] 6.1 Implement `InitializeNetworkServices()` as `async Task` with idempotency checks
    - Skip `UnityServices.InitializeAsync()` if `UnityServices.State == ServicesInitializationState.Initialized`
    - Skip `SignInAnonymouslyAsync()` if `AuthenticationService.Instance.IsSignedIn == true`
    - _Requirements: 6.1, 6.2, 6.3_

  - [x] 6.2 Implement `OnPlayOnlineClicked()` and `OnInviteFriendsClicked()` action buttons
    - `OnPlayOnlineClicked`: set `_isPrivateLobby = false`, await `InitializeNetworkServices()`, call `LobbyManager.Instance.QuickJoinOrCreatePublicLobby()`
    - `OnInviteFriendsClicked`: set `_isPrivateLobby = true`, await `InitializeNetworkServices()`, call `LobbyManager.Instance.CreateLobby(lobbyName, _localPlayerCount, isPrivate: true, _localGameMode, isHostLobby: true)`
    - Wrap both in try/catch; on exception log error and do not proceed to lobby/relay calls
    - _Requirements: 5.1, 5.2, 5.3, 5.4, 5.5_

  - [ ]* 6.3 Write property test for network services initialization idempotency (Property 4)
    - **Property 4: Network Services Initialization is Idempotent**
    - For any call sequence: calling `InitializeNetworkServices()` twice when already initialized must not call `InitializeAsync` or `SignInAnonymouslyAsync` a second time
    - Mock `UnityServices` and `AuthenticationService`; assert each service method called at most once
    - **Validates: Requirements 6.1, 6.2**
    - `// Feature: dynamic-carrom-lobby, Property 4: InitializeNetworkServices() is idempotent`

- [x] 7. Patch StartGameManager.CreateRelay() for dynamic allocation size
  - [x] 7.1 Replace hardcoded `maxConnections = 3` with `StartGameLobbyManager.LocalPlayerCount - 1`
    - Add `public static int LocalPlayerCount => Instance != null ? Instance._localPlayerCount : 1;` to `StartGameLobbyManager`
    - In `StartGameManager.CreateRelay()`: `int maxConnections = Mathf.Max(1, StartGameLobbyManager.LocalPlayerCount - 1);`
    - _Requirements: 10.1, 10.2, 10.3_

  - [ ]* 7.2 Write property test for relay allocation size (Property 7)
    - **Property 7: Relay Allocation Size Matches Player Count**
    - For any N in {2, 3, 4}: `CreateRelay()` calls `CreateAllocationAsync(N - 1)`; when `LocalPlayerCount` is unset (defaults to 1 via fallback), `maxConnections == 1`
    - Mock `RelayService`; capture the argument passed to `CreateAllocationAsync`; assert it equals `N - 1`
    - **Validates: Requirements 10.1, 10.3**
    - `// Feature: dynamic-carrom-lobby, Property 7: CreateRelay() calls CreateAllocationAsync(LocalPlayerCount - 1)`

- [x] 8. Implement ready state synchronization
  - [x] 8.1 Add `_readyStates` dictionary and `OnClientConnected` subscription
    - Declare `private Dictionary<ulong, bool> _readyStates = new Dictionary<ulong, bool>();`
    - In `OnNetworkSpawn` (server path): subscribe `NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected`
    - In `OnClientConnected(ulong clientId)`: set `_readyStates[clientId] = true`, call `RefreshStartButtonState()`
    - In `OnClientDisconnected(ulong clientId)`: call `_readyStates.Remove(clientId)`, call `RefreshStartButtonState()`
    - _Requirements: 11.1, 11.2_

  - [ ]* 8.2 Write property test for new client default ready state (Property 8)
    - **Property 8: New Client Default Ready State**
    - For any client ID: after `OnClientConnected(id)` fires, `_readyStates[id] == true`
    - Generate arbitrary `ulong` client IDs; assert dictionary entry is `true` immediately after callback
    - **Validates: Requirements 11.2**
    - `// Feature: dynamic-carrom-lobby, Property 8: OnClientConnected(id) => _readyStates[id] == true`

  - [x] 8.3 Implement `ToggleReadyServerRpc` and `UpdateReadyStateClientRpc`
    - `ToggleReadyServerRpc`: read `rpcParams.Receive.SenderClientId`, flip `_readyStates[clientId]`, call `UpdateReadyStateClientRpc(clientId, _readyStates[clientId])`, call `RefreshStartButtonState()`
    - `UpdateReadyStateClientRpc(ulong clientId, bool isReady)`: update slot panel UI for the given client if applicable
    - Wire `readyButton.onClick` to call `ToggleReadyServerRpc()`
    - _Requirements: 11.3_

  - [ ]* 8.4 Write property test for ready toggle round-trip (Property 9)
    - **Property 9: Ready Toggle is a Round-Trip**
    - For any connected client: calling `ToggleReadyServerRpc` twice returns the client's ready state to its original value
    - Generate arbitrary initial ready states; toggle twice; assert state is unchanged
    - **Validates: Requirements 11.3**
    - `// Feature: dynamic-carrom-lobby, Property 9: ToggleReady twice => state unchanged`

  - [x] 8.5 Implement `RefreshStartButtonState()`
    - Enable `startGameButton.interactable` if and only if `_readyStates.Count > 0 && _readyStates.Values.All(v => v)`
    - Call from `OnClientConnected`, `OnClientDisconnected`, and `ToggleReadyServerRpc`
    - _Requirements: 11.4, 11.5_

  - [ ]* 8.6 Write property test for start button enabled state invariant (Property 10)
    - **Property 10: Start Button Enabled State Reflects All-Ready Invariant**
    - For any `_readyStates` dictionary: `startGameButton.interactable == (_readyStates.Count > 0 && _readyStates.Values.All(v => v))`
    - Generate arbitrary dictionaries of `ulong → bool`; assert button state matches the invariant
    - **Validates: Requirements 11.4, 11.5**
    - `// Feature: dynamic-carrom-lobby, Property 10: startGameButton.interactable iff all clients ready and count > 0`

- [x] 9. Checkpoint — Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

- [x] 10. Implement Ghost Bot injection
  - [x] 10.1 Implement `InjectGhostBots()` and wire it into `StartGame()`
    - Calculate `botCount = _localPlayerCount - (int)NetworkManager.Singleton.ConnectedClientsIds.Count`
    - Set `CarromGameManager.PendingBotCount = botCount`
    - Set `CarromGameManager.PendingPlayerCount = _localPlayerCount`
    - If `botCount > 0`, log the number of bots being injected
    - Call `InjectGhostBots()` from within `StartGame()` only; guard `StartGame()` with `IsSpawned && IsServer`
    - _Requirements: 12.1, 12.2, 12.3, 12.4, 12.5_

  - [ ]* 10.2 Write property test for ghost bot count arithmetic (Property 11)
    - **Property 11: Ghost Bot Count Arithmetic**
    - For any N in {2, 3, 4} and any H in {1..N}: `InjectGhostBots()` sets `PendingBotCount == N - H` and `PendingPlayerCount == N`
    - Generate random (N, H) pairs satisfying H ≤ N; mock `ConnectedClientsIds.Count` to return H; assert both static fields
    - **Validates: Requirements 12.1, 12.3**
    - `// Feature: dynamic-carrom-lobby, Property 11: InjectGhostBots() => PendingBotCount == N-H, PendingPlayerCount == N`

- [x] 11. Implement singleton lifecycle safety and OnDisable null guard
  - [x] 11.1 Override `Awake()` in `StartGameLobbyManager` for stale NGO session shutdown
    - If `Instance != null && Instance != this`: check `NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening`; if so, call `NetworkManager.Singleton.Shutdown()` and log the stale session message; then `Destroy(Instance.gameObject)` and set `Instance = null`
    - Call `base.Awake()` to register the new instance
    - _Requirements: 13.1_

  - [x] 11.2 Add null guard to `OnDisable` for `NetworkManager.Singleton`
    - Wrap `OnClientDisconnectCallback` unsubscribe in `if (NetworkManager.Singleton != null)`
    - Only unsubscribe when `IsSpawned && IsServer`
    - _Requirements: 13.2, 13.3_

  - [ ]* 11.3 Write unit tests for singleton lifecycle
    - Test: re-entering lobby scene with live `NetworkManager` session calls `Shutdown()` and destroys old instance
    - Test: `OnDisable` with `NetworkManager.Singleton == null` does not throw
    - _Requirements: 13.1, 13.2, 13.3_

- [x] 12. Final checkpoint — Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

## Notes

- Tasks marked with `*` are optional and can be skipped for a faster MVP
- Property tests use FsCheck in Unity Test Runner (Edit Mode) with a minimum of 100 iterations each
- All property tests must mock `NetworkManager`, `LobbyService`, and `RelayService` to avoid live service calls
- Each property test is tagged with `// Feature: dynamic-carrom-lobby, Property N: ...` for traceability
- The Host's own client ID is never added to `_readyStates` — the Host is implicitly always ready
