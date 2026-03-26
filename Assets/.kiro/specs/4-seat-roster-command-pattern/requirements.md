# Requirements Document

## Introduction

This feature upgrades CarromGameManager and StrikerController from a binary 2-player turn model to a server-authoritative 4-seat roster supporting Human (local/network) and AI Bot players. The binary `networkPlayerTurn` boolean is replaced by a structured `SeatData` roster and an `activeSeatIndex` NetworkVariable. Shot physics are decoupled from the mouse-release event into a universal `ExecuteShot` command method callable by both human UI and AI algorithms identically.

## Glossary

- **CarromGameManager**: The server-authoritative NetworkBehaviour managing game state, scoring, and turn flow.
- **StrikerController**: The NetworkBehaviour handling striker input, physics, and shot execution.
- **SeatData**: A struct representing one of four player seats, containing seat index, controller type, team assignment, and owner client ID.
- **ControllerType**: An enum with values `Human_Local`, `Human_Network`, `AI_Bot`, and `Closed`.
- **Team**: An enum with values `White` and `Black`.
- **Roster**: The server-managed array of four `SeatData` entries representing all seats in a match.
- **RosterState**: An `INetworkSerializable` struct wrapping the 4-element `SeatData` array for transmission as a single `NetworkVariable`.
- **ActiveSeatIndex**: A `NetworkVariable<int>` (0–3) identifying which seat currently holds the turn.
- **AdvanceTurn**: The server-side method that increments `ActiveSeatIndex` clockwise by 1 modulo 4 (`(activeSeatIndex.Value + 1) % 4`), skipping `Closed` seats. Does not use `SeatPriority`.
- **ExecuteShot**: A public method on `StrikerController` that accepts a direction vector and force magnitude and applies the shot physics.
- **ShotCommand**: The data pair `(Vector2 direction, float forceMagnitude)` passed to `ExecuteShot`.
- **PendingPlayerCount**: Static int on `CarromGameManager` set by `StartGameLobbyManager` before scene load; total active seats (human + bot).
- **PendingBotCount**: Static int on `CarromGameManager` set by `StartGameLobbyManager` before scene load; number of AI bot seats.
- **SeatPriority**: The fixed array `[0, 2, 1, 3]` defining the order in which seats are filled by `PopulateRoster()`. Not used by `AdvanceTurn()`.
- **NGO**: Unity Netcode for GameObjects.
- **Server**: The NGO host/server authority.
- **TransferAuthority**: The existing method in `CarromGameManager` that ends a shot cycle and advances the turn.

---

## Requirements

### Requirement 1: SeatData Roster Definition

**User Story:** As a developer, I want a structured 4-seat roster to replace the binary turn boolean, so that the game can support 2, 3, or 4 players and AI bots with a single unified turn model.

#### Acceptance Criteria

1. THE `CarromGameManager` SHALL define a `SeatData` struct with fields: `SeatIndex` (byte, 0–3), `ControllerType` (enum: `Human_Local`, `Human_Network`, `AI_Bot`, `Closed`), `Team` (enum: `White`, `Black`), and `OwnerClientId` (ulong).
2. THE `CarromGameManager` SHALL implement `INetworkSerializable` on `SeatData` so it can be transmitted via NGO.
3. THE `CarromGameManager` SHALL maintain a server-side `SeatData[]` array of exactly 4 elements representing the Roster.
4. THE `CarromGameManager` SHALL expose a `NetworkVariable<int> activeSeatIndex` with `NetworkVariableWritePermission.Server` to broadcast the active seat to all clients.
5. WHEN `PopulateRoster()` assigns teams, THE `CarromGameManager` SHALL apply team assignment based on `PendingPlayerCount`: if `PendingPlayerCount == 2`, seat 0 SHALL be `Team.White` and seat 2 SHALL be `Team.Black`; if `PendingPlayerCount >= 3`, seats 0 and 2 SHALL be `Team.White` and seats 1 and 3 SHALL be `Team.Black`.

---

### Requirement 2: Server Roster Population

**User Story:** As the server, I want to populate the roster automatically on spawn using the pending player and bot counts, so that every match starts with a correctly configured seat layout without manual setup.

#### Acceptance Criteria

1. WHEN `OnNetworkSpawn` executes on the Server, THE `CarromGameManager` SHALL call a `PopulateRoster()` method using `PendingPlayerCount` and `PendingBotCount`.
2. WHEN `PopulateRoster()` runs, THE `CarromGameManager` SHALL iterate through `SeatPriority` (`[0, 2, 1, 3]`) and assign connected human `ClientId` values from `NetworkManager.Singleton.ConnectedClientsIds` to the first `(PendingPlayerCount - PendingBotCount)` seats in priority order.
3. WHEN `PopulateRoster()` runs, THE `CarromGameManager` SHALL fill the next seats in `SeatPriority` order (up to `PendingPlayerCount`) with `ControllerType.AI_Bot` and set their `OwnerClientId` to `0` (Server authority).
4. WHEN `PopulateRoster()` runs, THE `CarromGameManager` SHALL set all seats not assigned by steps 2 or 3 to `ControllerType.Closed`.
5. IF `PendingPlayerCount` is less than 1 or greater than 4, THEN THE `CarromGameManager` SHALL log an error and default to a 2-seat human layout.
6. WHEN `PopulateRoster()` assigns teams, THE `CarromGameManager` SHALL apply team assignment based on `PendingPlayerCount`: if `PendingPlayerCount == 2`, seat 0 SHALL be `Team.White` and seat 2 SHALL be `Team.Black`; if `PendingPlayerCount >= 3`, seats 0 and 2 SHALL be `Team.White` and seats 1 and 3 SHALL be `Team.Black`.
7. WHEN `PopulateRoster()` completes, THE `CarromGameManager` SHALL write the populated roster into `NetworkVariable<RosterState> rosterState` so all clients — including late joiners — receive the current roster automatically via NGO synchronization.

---

### Requirement 3: Smart Turn Iterator

**User Story:** As the server, I want a turn iterator that advances through active seats and skips closed ones, so that 2-player, 3-player, and 4-player matches all use the same turn advancement logic.

#### Acceptance Criteria

1. THE `CarromGameManager` SHALL replace `networkPlayerTurn` with `NetworkVariable<int> activeSeatIndex` initialized to `0`.
2. THE `CarromGameManager` SHALL implement `AdvanceTurn()` as a server-only method that advances `activeSeatIndex` using strictly clockwise iteration: `(activeSeatIndex.Value + 1) % 4`.
3. WHEN `AdvanceTurn()` advances the index, THE `CarromGameManager` SHALL skip any seat whose `ControllerType` is `Closed` by continuing to increment by 1 modulo 4 until a non-`Closed` seat is found.
4. IF all seats are `Closed`, THEN THE `CarromGameManager` SHALL log an error and leave `activeSeatIndex` unchanged to prevent an infinite loop.
5. WHEN `activeSeatIndex` changes, THE `CarromGameManager` SHALL call `UpdateTurnDisplayClientRpc` with the new active seat's `Team` and `ControllerType` so all clients can update their UI.
6. WHEN `EvaluateFreestyleMode()` or `EvaluateClassicMode()` checks the active player's team, THE `CarromGameManager` SHALL read `Roster[activeSeatIndex.Value].Team` instead of `networkPlayerTurn.Value`.
7. WHEN `TransferAuthority()` determines whether to retain or advance the turn, THE `CarromGameManager` SHALL call `AdvanceTurn()` on turn loss and `RetainCurrentSeat()` on turn retention.

---

### Requirement 4: ExecuteShot Command Method

**User Story:** As a developer, I want shot physics extracted into a standalone command method, so that both human UI input and AI algorithms can trigger shots through an identical interface.

#### Acceptance Criteria

1. THE `StrikerController` SHALL expose a `public void ExecuteShot(Vector2 direction, float forceMagnitude)` method.
2. WHEN `ExecuteShot` is called, THE `StrikerController` SHALL disable the `strikerForceField` GameObject, apply `rb.AddForce(direction.normalized * forceMagnitude, ForceMode2D.Impulse)`, and call `CarromGameManager.OnShotStart()`.
3. WHEN the mouse-release event in `StrikerController` fires with a valid drag vector, THE `StrikerController` SHALL calculate `direction` and `forceMagnitude` from the drag and call `ExecuteShot(direction, forceMagnitude)` instead of executing physics inline.
4. THE `StrikerController` SHALL ensure `ExecuteShot` is only callable when `IsOwner` is true or when the caller is the Server acting on behalf of an `AI_Bot` seat.
5. WHEN `ExecuteShot` is called, THE `StrikerController` SHALL start the existing post-shot wait coroutine (wait for all objects to stop, then call `OnShotComplete`) identically to the current `FireShot` behavior.

---

### Requirement 5: AI Bot Turn Execution

**User Story:** As the server, I want AI bot turns to be triggered automatically using the same ExecuteShot interface as human players, so that bot behavior is architecturally identical to human input.

#### Acceptance Criteria

1. WHEN `activeSeatIndex` advances to a seat with `ControllerType.AI_Bot`, THE `CarromGameManager` SHALL invoke a `TriggerBotShot(SeatData botSeat)` method on the Server.
2. WHEN `TriggerBotShot` runs, THE `CarromGameManager` SHALL verify `IsServer` is true, calculate a shot direction and force magnitude (placeholder: aim toward board center with a fixed force), and call `strikerController.ExecuteShot(direction, force)` directly as a local call — no ClientRpc is used.
3. WHEN `TriggerBotShot` executes the shot, THE `StrikerController` SHALL process it through `ExecuteShot` identically to a human shot, including `OnShotStart`, physics application, and `OnShotComplete`.

---

### Requirement 6: Per-Seat Scoring

**User Story:** As the server, I want each seat to maintain its own independent score, so that Freestyle and Classic scoring modes can both derive correct results from the same underlying data.

#### Acceptance Criteria

1. THE `CarromGameManager` SHALL maintain four independent score NetworkVariables — `score0`, `score1`, `score2`, `score3` (each `NetworkVariable<int>`) — one per seat index, replacing the previous `networkScoreWhite` and `networkScoreBlack` variables.
2. WHEN a coin is pocketed in Freestyle mode, THE `CarromGameManager` SHALL credit the coin to `score[activeSeatIndex.Value]` (the active seat's own score variable).
3. WHEN `EvaluateFreestyleMode()` determines which player to credit points to, THE `CarromGameManager` SHALL increment the NetworkVariable corresponding to `activeSeatIndex.Value`.
4. WHEN `EvaluateClassicMode()` calculates team scores for win evaluation, THE `CarromGameManager` SHALL compute White team score as `score0.Value + score2.Value` and Black team score as `score1.Value + score3.Value`.
5. WHEN `EvaluateClassicMode()` determines `myCoin` (the active player's assigned coin color), THE `CarromGameManager` SHALL derive it from `Roster[activeSeatIndex.Value].Team` where `Team.White` maps to `CoinType.White` and `Team.Black` maps to `CoinType.Black`.

---

### Requirement 7: Active Player Authority Resolution

**User Story:** As a client, I want to know whether it is my turn based on the active seat's OwnerClientId, so that the striker UI and input are correctly enabled or disabled for all seat configurations.

#### Acceptance Criteria

1. WHEN `activeSeatIndex` changes on any client, THE `CarromGameManager` SHALL evaluate whether `NetworkManager.Singleton.LocalClientId` matches `Roster[activeSeatIndex.Value].OwnerClientId`.
2. WHEN the local client's ID matches the active seat's `OwnerClientId`, THE `CarromGameManager` SHALL activate the striker slider and turn indicator UI.
3. WHEN the local client's ID does not match the active seat's `OwnerClientId`, THE `CarromGameManager` SHALL deactivate the striker slider and turn indicator UI.
4. THE `StrikerController` SHALL gate all input processing behind a check that `NetworkManager.Singleton.LocalClientId == CarromGameManager.Instance.GetActiveSeatOwnerClientId()`.

---

### Requirement 8: Backward Compatibility — 2-Player Match

**User Story:** As a player in a standard 2-player match, I want the new roster system to behave identically to the old binary turn system, so that existing 2-player gameplay is unaffected.

#### Acceptance Criteria

1. WHEN `PendingPlayerCount` is 2 and `PendingBotCount` is 0, THE `CarromGameManager` SHALL populate seat 0 with the Host's `ClientId` (South, `Team.White`) and seat 2 with the Client's `ClientId` (North, `Team.Black`), following `SeatPriority` order, and mark seats 1 and 3 as `Closed`.
2. WHEN `AdvanceTurn()` runs in a 2-player match, THE `CarromGameManager` SHALL alternate between seat 0 and seat 2 by clockwise increment (`0→1→2`, skipping closed seat 1, then `2→3→0`, skipping closed seat 3), producing the same alternating behavior as the old `networkPlayerTurn` flip.
3. WHEN the game is in a 2-player match, THE `CarromGameManager` SHALL NOT require any code path changes in `BoardScript` or `TimerScript`.

---

### Requirement 9: NGO Roster Sync via NetworkVariable

**User Story:** As a developer, I want the roster sync strategy to use a NetworkVariable so that late-joining clients receive the current roster automatically without manual re-send logic.

#### Acceptance Criteria

1. THE `CarromGameManager` SHALL define a `RosterState` struct implementing `INetworkSerializable` that wraps the 4-element `SeatData` array using `BufferSerializer<T>` for deterministic byte layout across platforms.
2. THE `CarromGameManager` SHALL expose a `NetworkVariable<RosterState> rosterState` with `NetworkVariableWritePermission.Server` to broadcast the full roster to all clients.
3. WHEN `PopulateRoster()` completes, THE `CarromGameManager` SHALL assign the populated `RosterState` to `rosterState.Value` so NGO automatically delivers the current state to all connected and future clients.
4. WHEN a client spawns after the roster has been populated, THE `CarromGameManager` SHALL rely on NGO's built-in `NetworkVariable` late-joiner synchronization — no manual `OnClientConnectedCallback` re-send is required.
5. THE `CarromGameManager` SHALL NOT use `NetworkVariable<SeatData[]>` directly because NGO does not natively support array-typed NetworkVariables without a serializable wrapper struct.

---

### Requirement 10: Striker Reset on Seat Transition

**User Story:** As a player, I want the striker to reset to the correct baseline position when the active seat changes, so that each player's turn starts with the striker in the right location for their side of the board.

#### Acceptance Criteria

1. WHEN `activeSeatIndex` advances to a new seat, THE `CarromGameManager` SHALL call a `[ClientRpc]` that triggers `StrikerController.ResetToBaseline(int seatIndex)` on the client whose `LocalClientId` matches the new active seat's `OwnerClientId`.
2. WHEN the active seat is an `AI_Bot`, THE `CarromGameManager` SHALL reset the striker to the bot's designated baseline position on the Server before calling `TriggerBotShot`.
3. WHEN `ResetToBaseline(int seatIndex)` executes, THE `StrikerController` SHALL use the following axis-aware positioning rules based on seat index:
   - Seat 0 (South): set position Y = -4.57f; striker moves along the X-axis.
   - Seat 2 (North): set position Y = 3.45f; striker moves along the X-axis.
   - Seat 1 (East): set position X = [east rail value]; striker moves along the Y-axis (camera rotated 90°).
   - Seat 3 (West): set position X = [west rail value]; striker moves along the Y-axis (camera rotated 270°).
4. THE `StrikerController` SHALL NOT use a Y-only baseline formula applied uniformly to all seats, as seats 1 and 3 are positioned on the east and west rails and require X-axis positioning instead.
