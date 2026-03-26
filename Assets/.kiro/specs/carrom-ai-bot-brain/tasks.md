# Tasks: Carrom AI Bot Brain

## Task List

- [x] 1. Add BoardScript accessor methods
  - [x] 1.1 Add `BaselineData` struct to `BoardScript.cs` with fields: `isHorizontal`, `fixedAxis`, `rangeMin`, `rangeMax`
  - [x] 1.2 Add `GetPocketPositions()` method returning the four pocket `Vector2` world positions (read from existing pocket trigger `Transform` references or hardcoded constants matching the board layout)
  - [x] 1.3 Add `GetBaseline(int seatIndex)` method returning a `BaselineData` for the given seat, using the same Y/X constants already present in `StrikerController` (`-4.57f`, `3.45f`, `eastRailX`, `westRailX`) and the slider range `[-3f, 3f]`

- [x] 2. Create CarromAIBrain script skeleton
  - [x] 2.1 Create `Scripts/Carrom/AI/CarromAIBrain.cs` as a `MonoBehaviour`
  - [x] 2.2 Declare all `[SerializeField]` dependency fields: `StrikerController striker`, `PieceRegistry pieceRegistry`, `BoardScript boardScript`
  - [x] 2.3 Declare all `[Header("Bot Behavior")]` `[SerializeField]` tuning fields: `raycastLayerMask`, `coinRadius`, `forceMultiplier`, `minForce`, `maxForce`, `cutAngleWeight`, `distanceWeight`, `clusteringWeight`, `minThinkTime`, `maxThinkTime`, `aimingSpeed`, `positionSnapThreshold`, `errorMarginCone`
  - [x] 2.4 Declare the `CandidateShot` private struct with fields: `strikerPosition`, `aimDirection`, `forceMagnitude`, `score`, `cutAngleDeg`, `distanceToCoin`, `clusterDensity`
  - [x] 2.5 Add public `void TriggerBotShot(SeatData botSeat)` entry point with `if (!IsServer) return;` guard that starts `BotShotRoutine` coroutine

- [x] 3. Implement board vision — legal target collection
  - [x] 3.1 Implement `CollectTargetCoins(SeatData botSeat)` returning `List<GameObject>` by iterating all registered pieces in `PieceRegistry` (IDs 1–19) and filtering out pieces with `position.y >= 500f`
  - [x] 3.2 In Classic mode, further filter to coins whose `tag` matches `botSeat.Team.ToString()` plus the Queen (`tag == "Queen"`) if `hostSecuredQueen` and `clientSecuredQueen` are both false (read from `CarromGameManager.Instance`)
  - [x] 3.3 In Freestyle mode, include all non-Striker live coins (tags "White", "Black", "Queen")
  - [x] 3.4 If the resulting list is empty, log `Debug.LogWarning` and return null to trigger fallback

- [x] 4. Implement pathfinding — pocket clearance circle casts
  - [x] 4.1 Implement `GetValidPocketPaths(GameObject coin)` returning `List<Vector2>` of unobstructed pocket positions
  - [x] 4.2 For each of the four pocket positions from `boardScript.GetPocketPositions()`, cast `Physics2D.CircleCast` (radius = `coinRadius`, a new `[SerializeField] float coinRadius = 0.27f` field) from `coin.transform.position` toward the pocket using `raycastLayerMask`, ignoring the coin's own collider; add pocket to valid list only if the cast is unobstructed (hits nothing, or first hit is the pocket trigger itself). Using `CircleCast` instead of `Raycast` ensures the bot never attempts to send a coin through a gap that is physically too narrow for the coin's collider.
  - [x] 4.3 If all four circle casts are blocked, return an empty list (coin is skipped silently)

- [x] 5. Implement pathfinding — ghost coin position and striker baseline line-of-sight
  - [x] 5.1 Implement `ComputeGhostCoinPos(Vector2 coinPos, Vector2 pocketPos)` returning `Vector2`: `ghostCoinPos = coinPos - (pocketPos - coinPos).normalized * coinRadius`. This is the contact point the Striker must hit — the position the Striker's center must reach to transfer momentum in the correct direction. All subsequent striker alignment and aim direction calculations use `ghostCoinPos`, not `coinPos`.
  - [x] 5.2 Implement `ComputeStrikerPosition(Vector2 ghostCoinPos, BaselineData baseline)` returning a `Vector2`: compute the ideal striker X (or Y) on the baseline such that the striker aligns with `ghostCoinPos` along the required approach vector
  - [x] 5.3 Clamp the computed position to `[baseline.rangeMin, baseline.rangeMax]`
  - [x] 5.4 Cast `Physics2D.CircleCast` (radius = `coinRadius`) from the clamped striker position toward `ghostCoinPos` using `raycastLayerMask`; return null if obstructed. Using `CircleCast` here ensures the striker's physical body has a clear path to the contact point.

- [x] 6. Implement kinematics — shot calculation and cut angle validation
  - [x] 6.1 Compute `aimDirection` as `(ghostCoinPos - strikerPos).normalized` — aim at the ghost coin contact point, not the coin center, so the momentum transfer produces the correct cut angle
  - [x] 6.2 Compute `forceMagnitude` as `Vector2.Distance(strikerPos, ghostCoinPos) * forceMultiplier`, clamped to `[minForce, maxForce]`
  - [x] 6.3 Validate cut angle: compute `Vector2.Dot(aimDirection, (pocketPos - coinPos).normalized)`; discard candidate if dot product <= 0
  - [x] 6.4 Compute `cutAngleDeg` as `Vector2.Angle(aimDirection, (pocketPos - coinPos).normalized)` for scoring

- [x] 7. Implement candidate scoring and selection
  - [x] 7.1 Implement `ScoreCandidate(ref CandidateShot c)`: score = `(1f - cutAngleDeg/90f) * cutAngleWeight + (1f - Mathf.Clamp01(distanceToCoin/10f)) * distanceWeight + (1f - Mathf.Clamp01(clusterDensity/5f)) * clusteringWeight`
  - [x] 7.2 Implement `clusterDensity` as count of live coins within radius 1.5f of the target coin (use `Physics2D.OverlapCircleNonAlloc`)
  - [x] 7.3 Implement `SelectBestCandidate(List<CandidateShot> candidates)`: if one candidate, return it directly; otherwise return the one with highest `score`
  - [x] 7.4 If candidate list is empty after all filtering, log `Debug.LogWarning` and execute fallback dummy shot

- [x] 8. Implement the BotShotRoutine coroutine
  - [x] 8.1 `yield return new WaitForSeconds(Random.Range(minThinkTime, maxThinkTime))` at the start
  - [x] 8.2 Call `CollectTargetCoins` → `BuildCandidateShots` → `SelectBestCandidate`; on null/empty result, call `ExecuteFallbackShot` and yield break
  - [x] 8.3 Call `striker.ResetToBaseline(botSeat.SeatIndex)` to place striker at baseline origin
  - [x] 8.4 Move the striker from its current position to `bestShot.strikerPosition` using `Vector3.MoveTowards` each frame at `aimingSpeed` units/second, updating `striker.transform.position` directly on the server. Since `StrikerController` has no `NetworkTransform`, send ONE single `SyncAimClientRpc(x, y)` call at the start of the slide (to set the initial position on all clients) and ONE more when the slide completes (to snap to the exact final position). Do NOT call any RPC per-frame — clients will see smooth interpolation via the `Rigidbody2D.interpolation = Interpolate` already set in `StrikerController.Start()`.
  - [x] 8.5 Once within `positionSnapThreshold`, snap to exact position
  - [x] 8.6 Apply error cone: `float offset = Random.Range(-errorMarginCone/2f, errorMarginCone/2f); Vector2 finalDir = Quaternion.Euler(0,0,offset) * bestShot.aimDirection`
  - [x] 8.7 Call `striker.ExecuteShot(finalDir, bestShot.forceMagnitude)`

- [x] 9. Implement fallback dummy shot
  - [x] 9.1 Implement `ExecuteFallbackShot(SeatData botSeat)`: call `striker.ResetToBaseline(botSeat.SeatIndex)`, compute direction toward `Vector2.zero`, call `striker.ExecuteShot(direction, 15f)`

- [x] 10. Wire CarromAIBrain into CarromGameManager
  - [x] 10.1 Add `[SerializeField] CarromAIBrain aiBrain;` field to `CarromGameManager`
  - [x] 10.2 Replace the body of `TriggerBotShot(SeatData botSeat)` with `aiBrain.TriggerBotShot(botSeat);` (keep the `if (!IsServer) return;` guard and null check)

- [ ] 11. Unity Editor wiring and validation
  - [ ] 11.1 In the Unity Editor, assign the `CarromAIBrain` component to the `aiBrain` field on `CarromGameManager`
  - [ ] 11.2 Assign `StrikerController`, `PieceRegistry`, and `BoardScript` references on the `CarromAIBrain` component
  - [ ] 11.3 Verify all `[SerializeField]` tuning fields appear under the "Bot Behavior" Inspector header with sensible defaults
  - [ ] 11.4 Run a Freestyle bot game and visually confirm: think delay, striker slide, shot execution, and turn handoff
  - [ ] 11.5 Run a Classic bot game and visually confirm the bot only targets its own team's coins
