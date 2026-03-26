# Design Document: Dynamic Carrom Lobby

## Overview

This document describes the technical design for upgrading the Carrom lobby from a 1v1 auto-connecting system to a dynamic Host-controlled 1-to-4 player lobby. The design follows an Offline-First paradigm: no network calls occur until the player explicitly clicks an action button.

The three primary concerns are:

1. **LobbyManager surgery** — suppress the auto-navigation that fires `OnLobbyStartGame` immediately after `CreateLobby()`, so the Host stays in the lobby scene waiting for players.
2. **Ready state synchronization** — server-side tracking of per-client readiness with a ClientRpc push model to update the Host's `startGameButton` enabled state.
3. **Relay sizing + Ghost Bot injection** — pass `_localPlayerCount` from `StartGameLobbyManager` to `StartGameManager` so the Relay allocation matches the configured seat count, and silently fill empty seats with bots at game start.

---

## Architecture

The system is composed of three cooperating components:

```
┌─────────────────────────────────────────────────────────────────┐
│                     StartGameLobbyManager                       │
│  (SingletonNetwork<T> — owns all lobby UI + NGO lifecycle)      │
│                                                                 │
│  Offline state: _localGameMode, _localPlayerCount, _isPrivate   │
│  Network state: netCarromRuleset (NetworkVariable<GameMode>)    │
│  Ready state:   _readyStates (Dictionary<ulong,bool>) — server  │
└────────────────┬────────────────────────────┬───────────────────┘
                 │ calls                       │ reads
                 ▼                             ▼
┌───────────────────────────┐   ┌──────────────────────────────┐
│      LobbyManager         │   │      StartGameManager        │
│  CreateLobby() — patched  │   │  CreateRelay() — patched     │
│  NO auto OnLobbyStartGame │   │  reads LocalPlayerCount      │
│  LastLobbyCode persisted  │   │  from StartGameLobbyManager  │
└───────────────────────────┘   └──────────────────────────────┘
```

### Key Architectural Decisions

**Decision 1 — LobbyManager auto-navigation suppression**

`CreateLobby()` currently fires `OnLobbyStartGame` and sets `alreadyStartedGame = true` immediately after the lobby is created. This must be suppressed for the private lobby flow. The cleanest approach is to add an `isHostLobby` parameter (defaulting to `false`) to `CreateLobby()`. When `true`, the method skips the `OnLobbyStartGame` fire and the `alreadyStartedGame` flag. The Host explicitly triggers game start via `StartGameLobbyManager.OnStartGameClicked()` → `LobbyManager.StartGame()`.

**Decision 2 — Relay allocation size**

`StartGameManager.CreateRelay()` hardcodes `maxConnections = 3`. The fix is a static property `StartGameLobbyManager.LocalPlayerCount` that `StartGameManager` reads before calling `CreateAllocationAsync`. Static is appropriate here because `StartGameLobbyManager` is a singleton and the value is set before any network call is made.

**Decision 3 — Ready state data structure**

A server-side `Dictionary<ulong, bool> _readyStates` is the right choice over `NetworkList<ReadyState>`. Reasons:
- The Host is the only consumer of the aggregate ready check (to enable `startGameButton`).
- Clients only need to know their own ready state (which they already know locally).
- A ClientRpc `UpdateReadyStateClientRpc(ulong clientId, bool isReady)` can push targeted UI updates if slot panels ever need to show per-player ready indicators.
- `NetworkList` would require a custom struct, serialization boilerplate, and adds unnecessary sync overhead for data only the server needs to aggregate.

**Decision 4 — Singleton re-entry**

`SingletonNetwork<T>.Awake()` calls `Destroy(gameObject)` on the duplicate. The problem is that when the lobby scene reloads with a stale `NetworkManager` session still running, the *new* `StartGameLobbyManager` instance is destroyed before it can call `NetworkManager.Singleton.Shutdown()`. The fix is to override `Awake()` in `StartGameLobbyManager`: if `Instance != null && Instance != this`, check if `NetworkManager.Singleton.IsListening` and shut it down before destroying the old instance and registering the new one.

**Decision 5 — startGameButton enabled state**

The button is enabled only when `_readyStates.Count > 0` (at least one client connected) AND all values in `_readyStates` are `true`. This check runs in a helper `RefreshStartButtonState()` called from: `OnClientConnected`, `OnClientDisconnected`, and `ToggleReadyServerRpc`.

---

## Components and Interfaces

### StartGameLobbyManager (modified)

New members added to the existing class:

```csharp
// Ready state — server-side only
private Dictionary<ulong, bool> _readyStates = new Dictionary<ulong, bool>();

// Expose player count for StartGameManager to read
public static int LocalPlayerCount => Instance != null ? Instance._localPlayerCount : 1;

// ServerRpc — client calls this to toggle their ready state
[ServerRpc(RequireOwnership = false)]
public void ToggleReadyServerRpc(ServerRpcParams rpcParams = default);

// ClientRpc — server pushes ready state update to all clients (for slot panel UI)
[ClientRpc]
private void UpdateReadyStateClientRpc(ulong clientId, bool isReady);

// Recomputes and sets startGameButton.interactable
private void RefreshStartButtonState();

// Called from OnNetworkSpawn (server path) to subscribe to connect/disconnect
private void OnClientConnected(ulong clientId);
private void OnClientDisconnected(ulong clientId);  // already stubbed, needs ready state logic
```

### LobbyManager (modified)

```csharp
// Modified signature — isHostLobby suppresses auto-navigation
public async void CreateLobby(string lobbyName, int maxPlayers, bool isPrivate,
                               GameMode gameMode, bool isHostLobby = false);
```

When `isHostLobby == true`:
- Skip `OnLobbyStartGame?.Invoke(...)` 
- Skip `alreadyStartedGame = true`
- Skip `IsHost = true`
- Still set `LastLobbyCode = lobby.LobbyCode`

### StartGameManager (modified)

```csharp
private async void CreateRelay()
{
    // Read dynamic count instead of hardcoded 3
    int maxConnections = Mathf.Max(1, StartGameLobbyManager.LocalPlayerCount - 1);
    Allocation allocation = await RelayService.Instance.CreateAllocationAsync(maxConnections);
    // ... rest unchanged
}
```

### CarromGameManager (interface — no changes needed)

`StartGameLobbyManager` already writes to `CarromGameManager.PendingBotCount` and `CarromGameManager.PendingPlayerCount` as static fields. These are consumed by `CarromGameManager` on scene load.

---

## Data Models

### Ready State

```csharp
// Server-side only — not synced as a NetworkVariable
private Dictionary<ulong, bool> _readyStates = new Dictionary<ulong, bool>();
```

Lifecycle:
- **Client connects** → `_readyStates[clientId] = true` (default ready)
- **Client toggles** → `ToggleReadyServerRpc` flips `_readyStates[clientId]`
- **Client disconnects** → `_readyStates.Remove(clientId)` then `RefreshStartButtonState()`
- **Game starts** → dictionary is not cleared (session ends anyway)

The Host's own entry is never added to `_readyStates` — the Host is always implicitly ready (they control the Start button).

### NetworkVariable<GameMode> netCarromRuleset

Existing field. Write path:
1. `OnNetworkSpawn` (IsServer) → initial sync from `_localGameMode`
2. `OnGameModeChanged` (IsServer && IsSpawned) → live update
3. 3-Player Interlock (IsServer && IsSpawned) → forced Freestyle

Read path:
- `netCarromRuleset.OnValueChanged` on clients → updates `gameModeDropdown.value`

### Static Handoff Fields (CarromGameManager)

```csharp
public static int PendingBotCount    = 0;  // bots to inject
public static int PendingPlayerCount = 2;  // total seats (human + bot)
```

Set by `InjectGhostBots()` immediately before `LoadingSceneManager.Instance.LoadScene(nextScene)`.

### LobbyManager State

| Field | Type | Change |
|---|---|---|
| `alreadyStartedGame` | `bool` | Only set `true` when `isHostLobby == false` in `CreateLobby()` |
| `IsHost` | `static bool` | Only set `true` when `isHostLobby == false` in `CreateLobby()` |
| `LastLobbyCode` | `static string` | Always set on `CreateLobby()` completion |

---

## Correctness Properties

*A property is a characteristic or behavior that should hold true across all valid executions of a system — essentially, a formal statement about what the system should do. Properties serve as the bridge between human-readable specifications and machine-verifiable correctness guarantees.*

### Property 1: Game Mode Dropdown Mapping

*For any* dropdown index value in {0, 1}, setting `gameModeDropdown.value` to that index must result in `_localGameMode` equaling `(GameMode)index` — i.e., index 0 → Freestyle, index 1 → Classic.

**Validates: Requirements 2.3**

---

### Property 2: Player Count Dropdown Mapping

*For any* dropdown index value in {0, 1, 2}, setting `playerCountDropdown.value` to that index must result in `_localPlayerCount` equaling `index + 2` — i.e., index 0 → 2, index 1 → 3, index 2 → 4.

**Validates: Requirements 2.4**

---

### Property 3: Slot Panel Activation Invariant

*For any* player count N in {2, 3, 4}, after `ApplyPlayerCountToUI(N)` is called, exactly the first N elements of `slotPanels` must be active and all remaining elements must be inactive. This property must hold even when individual elements of `slotPanels` are null (null elements are skipped without exception) and when the array itself is null or empty (no exception thrown).

**Validates: Requirements 2.6, 4.2, 4.3, 4.4**

---

### Property 4: Network Services Initialization is Idempotent

*For any* sequence of calls to `InitializeNetworkServices()`, if `UnityServices` is already in the `Initialized` state, `InitializeAsync()` must not be called again; and if `AuthenticationService.Instance.IsSignedIn` is already `true`, `SignInAnonymouslyAsync()` must not be called again. Calling the method N times must produce the same observable outcome as calling it once.

**Validates: Requirements 6.1, 6.2**

---

### Property 5: Lobby Code Persistence Round-Trip

*For any* lobby created via `LobbyManager.CreateLobby()`, `LobbyManager.LastLobbyCode` must equal the `LobbyCode` property of the created lobby object immediately after the async call completes — regardless of whether `isHostLobby` is true or false.

**Validates: Requirements 7.4**

---

### Property 6: NetworkVariable-to-Dropdown Sync

*For any* `GameMode` value V, when `netCarromRuleset.OnValueChanged` fires with new value V on a client, `gameModeDropdown.value` must equal `(int)V` after the callback executes.

**Validates: Requirements 9.4**

---

### Property 7: Relay Allocation Size Matches Player Count

*For any* `_localPlayerCount` value N in {2, 3, 4}, `StartGameManager.CreateRelay()` must call `RelayService.Instance.CreateAllocationAsync(N - 1)`. When `_localPlayerCount` is not set (defaults to 0 or uninitialized), `CreateRelay()` must use `maxConnections = 1` as the fallback.

**Validates: Requirements 10.1, 10.3**

---

### Property 8: New Client Default Ready State

*For any* client ID that connects to the NGO session, the server-side `_readyStates` dictionary must contain an entry for that client ID with value `true` immediately after `OnClientConnected` fires.

**Validates: Requirements 11.2**

---

### Property 9: Ready Toggle is a Round-Trip

*For any* connected client, calling `ToggleReadyServerRpc` twice in sequence must return that client's ready state to its original value. The toggle operation is its own inverse.

**Validates: Requirements 11.3**

---

### Property 10: Start Button Enabled State Reflects All-Ready Invariant

*For any* state of the `_readyStates` dictionary, `startGameButton.interactable` must be `true` if and only if the dictionary is non-empty AND every value in the dictionary is `true`. If any client has ready state `false`, or if no clients are connected, the button must be disabled.

**Validates: Requirements 11.4, 11.5**

---

### Property 11: Ghost Bot Count Arithmetic

*For any* `_localPlayerCount` N and any number of connected human clients H where H ≤ N, `InjectGhostBots()` must set `CarromGameManager.PendingBotCount` to exactly `N - H` and `CarromGameManager.PendingPlayerCount` to exactly `N`.

**Validates: Requirements 12.1, 12.3**

---

## Error Handling

### InitializeNetworkServices Failures

If `UnityServices.InitializeAsync()` or `SignInAnonymouslyAsync()` throws, the exception is caught in `OnPlayOnlineClicked` / `OnInviteFriendsClicked` and logged. No lobby or relay call proceeds. The UI remains in its pre-click state so the player can retry.

```csharp
public async void OnPlayOnlineClicked()
{
    try
    {
        _isPrivateLobby = false;
        await InitializeNetworkServices();
        LobbyManager.Instance.QuickJoinOrCreatePublicLobby();
    }
    catch (Exception e)
    {
        Debug.LogError($"[StartGameLobbyManager] Network init failed: {e.Message}");
        // TODO: surface error to player via ErrorMenu
    }
}
```

### Relay Allocation Failures

`StartGameManager.CreateRelay()` already wraps the relay call in a try/catch for `RelayServiceException`. No change needed here beyond the dynamic `maxConnections` value.

### Null Safety in ApplyPlayerCountToUI

```csharp
private void ApplyPlayerCountToUI(int count)
{
    if (slotPanels == null) return;          // Req 4.3
    for (int i = 0; i < slotPanels.Length; i++)
    {
        if (slotPanels[i] != null)           // Req 4.4
            slotPanels[i].SetActive(i < count);
    }
}
```

### NetworkManager Null Guard in OnDisable

```csharp
private void OnDisable()
{
    if (IsSpawned && IsServer && NetworkManager.Singleton != null)  // Req 13.3
        NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
}
```

### Singleton Re-Entry (Stale NGO Session)

Override `Awake()` in `StartGameLobbyManager` to detect and shut down a stale session before the base class destroys the duplicate:

```csharp
public override void Awake()
{
    if (Instance != null && Instance != this)
    {
        // Stale session check — shut down before destroying old instance
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
        {
            Debug.Log("[StartGameLobbyManager] Stale NGO session detected — shutting down.");
            NetworkManager.Singleton.Shutdown();
        }
        Destroy(Instance.gameObject);
        Instance = null;
    }
    base.Awake();  // registers this as the new Instance
}
```

### IsSpawned Guard on netCarromRuleset Writes

All writes to `netCarromRuleset.Value` are gated:

```csharp
if (IsSpawned && IsServer)
    netCarromRuleset.Value = _localGameMode;
```

This prevents the NGO "write before spawn" exception when `OnGameModeChanged` fires during offline configuration.

---

## Testing Strategy

### Dual Testing Approach

Both unit tests and property-based tests are required. They are complementary:
- Unit tests cover specific examples, integration points, and error conditions.
- Property-based tests verify universal invariants across randomized inputs.

### Unit Tests (specific examples and integration)

Focus areas:
- **3-Player Interlock examples**: Classic→3P forces Freestyle; 3P→Classic forces Freestyle.
- **OnNetworkSpawn UI split**: Host sees startGameButton; Client sees readyButton; Client dropdowns non-interactable.
- **LobbyManager auto-navigation suppression**: `CreateLobby(isHostLobby: true)` does not fire `OnLobbyStartGame` and does not set `IsHost = true`.
- **NetworkVariable write guard**: `OnGameModeChanged` when `IsSpawned == false` does not write to `netCarromRuleset`.
- **Singleton re-entry**: Re-entering the lobby scene with a live `NetworkManager` session shuts down the old session.
- **OnDisable null guard**: `OnDisable` with `NetworkManager.Singleton == null` does not throw.

### Property-Based Tests

Library: **FsCheck** (for C# / Unity test runner) or **fast-check** if tests are run in a JS harness. Each test runs a minimum of **100 iterations**.

Each test must be tagged with a comment in the format:
`// Feature: dynamic-carrom-lobby, Property {N}: {property_text}`

| Property | Test Description |
|---|---|
| P1 | For random index in {0,1}: `OnGameModeChanged(index)` → `_localGameMode == (GameMode)index` |
| P2 | For random index in {0,1,2}: `OnPlayerCountChanged(index)` → `_localPlayerCount == index + 2` |
| P3 | For random N in {2,3,4} and random null pattern in slotPanels: `ApplyPlayerCountToUI(N)` activates exactly N non-null panels |
| P4 | For any call sequence: `InitializeNetworkServices()` called twice does not call `InitializeAsync` or `SignInAnonymouslyAsync` a second time |
| P5 | For any lobby creation: `LastLobbyCode == lobby.LobbyCode` after `CreateLobby()` |
| P6 | For any `GameMode` value: `netCarromRuleset` change fires → `gameModeDropdown.value == (int)newMode` |
| P7 | For random N in {2,3,4}: `CreateRelay()` calls `CreateAllocationAsync(N-1)` |
| P8 | For any client ID: after `OnClientConnected(id)`, `_readyStates[id] == true` |
| P9 | For any client ID: `ToggleReady` twice → ready state unchanged |
| P10 | For any `_readyStates` dictionary: `startGameButton.interactable == (_readyStates.Count > 0 && _readyStates.Values.All(v => v))` |
| P11 | For random N in {2,3,4} and random H in {1..N}: `InjectGhostBots()` → `PendingBotCount == N-H`, `PendingPlayerCount == N` |

### Test Configuration

- Minimum 100 iterations per property test.
- Property tests must mock `NetworkManager`, `LobbyService`, and `RelayService` to avoid live service calls.
- Use Unity Test Runner (Edit Mode) for unit and property tests.
- Integration tests (full lobby flow) are manual / playtest only due to Unity Services dependency.
