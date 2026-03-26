# Tasks — 4-Seat Roster & Command Pattern

## Phase 1: Data Structures & State

- [x] 1.1 Define `ControllerType` and `Team` enums in `CarromGameManager.cs`
  - Add `public enum ControllerType { Human_Local, Human_Network, AI_Bot, Closed }`
  - Add `public enum Team { White, Black }`

- [x] 1.2 Define `SeatData` struct with `INetworkSerializable`
  - Fields: `byte SeatIndex`, `ControllerType ControllerType`, `Team Team`, `ulong OwnerClientId`
  - Implement `NetworkSerialize<T>(BufferSerializer<T>)` serializing all four fields

- [x] 1.3 Define `RosterState` struct with `INetworkSerializable`
  - Four named fields: `SeatData Seat0, Seat1, Seat2, Seat3`
  - Implement indexer `this[int i]` returning the correct field
  - Implement `NetworkSerialize<T>` delegating to each `SeatData` field

- [x] 1.4 Add `NetworkVariable<RosterState> rosterState` to `CarromGameManager`
  - `NetworkVariableReadPermission.Everyone`, `NetworkVariableWritePermission.Server`

- [x] 1.5 Add `NetworkVariable<int> activeSeatIndex` to `CarromGameManager`
  - Initial value `0`, server-write, everyone-read

- [x] 1.6 Add four per-seat score `NetworkVariable<int>`: `score0`, `score1`, `score2`, `score3`
  - All server-write, everyone-read, initial value `0`

- [x] 1.7 Add server-side `SeatData[] Roster` array (4 elements) as a private field

- [x] 1.8 Remove `networkPlayerTurn`, `networkScorePlayer`, `networkScoreEnemy` declarations
  - Confirm no remaining references before deletion

---

## Phase 2: Turn Logic

- [x] 2.1 Implement `PopulateRoster()` — human seat assignment
  - Iterate `SeatPriority [0,2,1,3]`; assign `ConnectedClientsIds` entries to first `(PendingPlayerCount - PendingBotCount)` slots
  - Set `ControllerType.Human_Local` for host seat, `Human_Network` for remote seats

- [x] 2.2 Implement `PopulateRoster()` — bot seat assignment
  - Fill next `PendingBotCount` priority slots with `ControllerType.AI_Bot`, `OwnerClientId = 0`

- [x] 2.3 Implement `PopulateRoster()` — close remaining seats
  - Set all unfilled seats to `ControllerType.Closed`

- [x] 2.4 Implement `PopulateRoster()` — team assignment
  - If `PendingPlayerCount == 2`: seat 0 = `Team.White`, seat 2 = `Team.Black`
  - If `PendingPlayerCount >= 3`: seats 0,2 = `Team.White`; seats 1,3 = `Team.Black`

- [x] 2.5 Implement `PopulateRoster()` — validation and write to `rosterState`
  - Log error and default to 2-seat human layout if `PendingPlayerCount < 1 || > 4`
  - Write completed `Roster` into `rosterState.Value` at end of method

- [x] 2.6 Call `PopulateRoster()` from `OnNetworkSpawn` on the Server

- [x] 2.7 Implement `AdvanceTurn()` — server-only
  - Clockwise increment: `(activeSeatIndex.Value + 1) % 4`
  - Skip `Closed` seats; guard against all-closed infinite loop (log error, return)
  - Write result to `activeSeatIndex.Value`
  - Call `UpdateTurnDisplayClientRpc(newSeat.Team, newSeat.ControllerType)`

- [x] 2.8 Update `UpdateTurnDisplayClientRpc` signature to `(Team team, ControllerType ct)`

- [x] 2.9 Implement `RetainCurrentSeat()` — replaces retain-turn branch in `TransferAuthority`
  - Call `striker.RetainTurnResetClientRpc()` (existing RPC, no change needed)

- [x] 2.10 Update `TransferAuthority()` to call `AdvanceTurn()` on turn loss and `RetainCurrentSeat()` on retention
  - Remove `FlipTurn()` call
  - Replace `GetCurrentActivePlayerId()` with `GetActiveSeatOwnerClientId()`

- [x] 2.11 Implement `GetActiveSeatOwnerClientId()` — returns `Roster[activeSeatIndex.Value].OwnerClientId`
  - Return `ulong.MaxValue` if not spawned or roster not yet populated

- [x] 2.12 Update `EvaluateFreestyleMode()` to use `activeSeatIndex` instead of `networkPlayerTurn`
  - Replace `isHostTurn` boolean with `int activeIdx = activeSeatIndex.Value`
  - Credit points to `score[activeIdx]` instead of `networkScorePlayer`/`networkScoreEnemy`
  - Derive `queenSecured` from per-seat queen flags (or keep existing `hostSecuredQueen`/`clientSecuredQueen` for now with a TODO)

- [x] 2.13 Update `EvaluateClassicMode()` to use `activeSeatIndex` and `Roster[activeIdx].Team`
  - Replace `isHostTurn` with `activeIdx`; derive `myCoin` from `Roster[activeIdx].Team`
  - White team score = `score0.Value + score2.Value`; Black = `score1.Value + score3.Value`

- [x] 2.14 Update `Update()` win-condition check to use `score0`–`score3` aggregates

- [x] 2.15 Update `LateUpdate()` score display to read from `score0`–`score3`
  - Map seat scores to the correct UI text elements

- [x] 2.16 Update `GetActivePlayerClientId()` to delegate to `GetActiveSeatOwnerClientId()`

---

## Phase 3: Command Pattern & Bot Execution

- [x] 3.1 Add `public void ExecuteShot(Vector2 direction, float forceMagnitude)` to `StrikerController`
  - Guard: early return if `!IsOwner && !IsServer`
  - Disable `strikerForceField` GameObject
  - Call `CarromGameManager.Instance.OnShotStart()`
  - Apply `rb.AddForce(direction.normalized * forceMagnitude, ForceMode2D.Impulse)`
  - Start the post-shot wait coroutine (extracted from `FireShot`)

- [x] 3.2 Extract post-shot wait logic from `FireShot` into a private coroutine `WaitForShotComplete()`
  - Wait 0.1s, wait until `rb.linearVelocity < 0.1f`, wait until `AreAllObjectsStopped()`, call `OnShotComplete()`

- [x] 3.3 Refactor `FireShot` coroutine to calculate `direction` and `forceMagnitude` then call `ExecuteShot(direction, forceMagnitude)`
  - Remove inline `rb.AddForce` and `OnShotStart` calls from `FireShot`

- [x] 3.4 Update `ResetToBaseline` to accept `int seatIndex` parameter
  - Seat 0: `position.y = -4.57f`, X-axis slider movement
  - Seat 2: `position.y = 3.45f`, X-axis slider movement
  - Seat 1: `position.x = eastRailValue`, Y-axis slider movement
  - Seat 3: `position.x = westRailValue`, Y-axis slider movement
  - Keep parameterless overload calling `ResetToBaseline(activeSeatIndex)` for backward compat

- [x] 3.5 Update `OnGainedOwnership` and `OnLostOwnership` to use seat-aware `ResetToBaseline`

- [x] 3.6 Update `SetSliderX` to be axis-aware (X-axis for seats 0/2, Y-axis for seats 1/3)

- [x] 3.7 Implement `TriggerBotShot(SeatData botSeat)` in `CarromGameManager`
  - Guard: `if (!IsServer) return`
  - Call `ResetToBaseline(botSeat.SeatIndex)` on the striker
  - Calculate placeholder direction (toward board center `Vector2.zero`) and fixed force (e.g., `15f`)
  - Call `strikerController.ExecuteShot(direction, force)` directly (no ClientRpc)

- [x] 3.8 Hook `TriggerBotShot` into `AdvanceTurn()` — after writing `activeSeatIndex`, check if new seat is `AI_Bot` and invoke `TriggerBotShot`

- [x] 3.9 Update `activeSeatIndex.OnValueChanged` callback on clients to evaluate `GetActiveSeatOwnerClientId()` and enable/disable striker slider and turn indicator UI

- [x] 3.10 Gate `StrikerController.Update()` input processing behind `LocalClientId == CarromGameManager.Instance.GetActiveSeatOwnerClientId()`
  - Replace existing `if (!IsOwner && IsSpawned) return` with the seat-aware check

- [x] 3.11 Add `[ClientRpc] ResetToBaselineClientRpc(int seatIndex)` to `StrikerController`
  - Only executes on the client whose `LocalClientId` matches the new active seat's `OwnerClientId`
  - Calls `ResetToBaseline(seatIndex)`

- [x] 3.12 Call `ResetToBaselineClientRpc` from `AdvanceTurn()` after advancing to a human seat
