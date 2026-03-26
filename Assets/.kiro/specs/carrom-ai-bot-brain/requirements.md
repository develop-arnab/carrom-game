# Requirements Document

## Introduction

This feature replaces the dummy `TriggerBotShot` placeholder in `CarromGameManager.cs` with a modular, intelligent AI Bot Brain for the Carrom multiplayer game. The Bot Brain evaluates the live board state, calculates valid shots using 2D physics/geometry, scores candidate shots by difficulty, and executes the best shot through the existing `striker.ExecuteShot(direction, forceMagnitude)` API. All AI logic runs server-side only. All behavioral tuning variables are exposed to the Unity Inspector via `[SerializeField]`. No automated tests are written; all validation is visual and manual in the Unity Editor.

## Glossary

- **CarromAIBrain**: The new MonoBehaviour script that encapsulates all bot AI logic, attached to the same GameObject as `CarromGameManager` or as a separate prefab component.
- **BotSeat**: A `SeatData` struct whose `ControllerType` is `AI_Bot`.
- **Baseline**: The fixed rail line from which the Striker is launched. Seats 0 and 2 use a horizontal baseline (Y-axis fixed); Seats 1 and 3 use a vertical baseline (X-axis fixed).
- **Striker**: The `StrikerController` component on the active striker GameObject.
- **PieceRegistry**: The existing `PieceRegistry` MonoBehaviour that maps piece IDs to GameObjects.
- **TargetCoin**: A coin GameObject that is a legal target for the current bot's team and game mode.
- **CandidateShot**: A fully computed shot — striker position on the baseline, aim direction, and force magnitude — that has been validated as geometrically clear.
- **CutAngle**: The angle between the line from Striker to TargetCoin and the line from TargetCoin to the chosen Pocket, measured in degrees.
- **ErrorCone**: An Inspector-configurable angular spread (in degrees) applied to the final aim direction to simulate human imprecision.
- **Pocket**: One of the four corner pocket positions defined by `BoardScript`.
- **ThinkTime**: A random delay sampled between `minThinkTime` and `maxThinkTime` before the bot commits to a shot.

## Requirements

### Requirement 1: Board Vision — Legal Target Identification

**User Story:** As the server, I want the Bot Brain to identify which coins are legal targets for the active bot seat, so that the AI only attempts valid shots according to the current game mode and team rules.

#### Acceptance Criteria

1. WHEN `TriggerBotShot` is called with a `BotSeat`, THE `CarromAIBrain` SHALL query `PieceRegistry` to retrieve all coin GameObjects currently on the board (position Y < 500, i.e., not in the Graveyard).
2. WHEN the current `GameMode` is `Classic`, THE `CarromAIBrain` SHALL restrict `TargetCoin` candidates to coins whose tag matches the `BotSeat.Team` (White team targets "White" coins; Black team targets "Black" coins), plus the Queen if it has not yet been secured by either team.
3. WHEN the current `GameMode` is `Freestyle`, THE `CarromAIBrain` SHALL treat all non-Striker coins ("White", "Black", "Queen") as valid `TargetCoin` candidates.
4. IF no legal `TargetCoin` candidates exist on the board, THEN THE `CarromAIBrain` SHALL fall back to the existing dummy shot behavior (aim at `Vector2.zero` with a fixed force) and log a warning.

---

### Requirement 2: Pathfinding — Pocket Clearance Raycast

**User Story:** As the server, I want the Bot Brain to verify that a target coin has a clear path to at least one pocket, so that the AI does not attempt shots that are physically blocked.

#### Acceptance Criteria

1. WHEN evaluating a `TargetCoin`, THE `CarromAIBrain` SHALL perform a 2D Physics raycast from the `TargetCoin`'s center toward each of the four `Pocket` positions.
2. WHEN a raycast from `TargetCoin` to a `Pocket` is unobstructed (hits only the pocket trigger or nothing), THE `CarromAIBrain` SHALL mark that `(TargetCoin, Pocket)` pair as a valid pocket path.
3. IF all four raycasts for a `TargetCoin` are obstructed, THEN THE `CarromAIBrain` SHALL exclude that coin from `CandidateShot` generation for this turn.
4. THE `CarromAIBrain` SHALL use a configurable `[SerializeField] LayerMask raycastLayerMask` to control which colliders are considered obstructions during raycasting.

---

### Requirement 3: Pathfinding — Baseline Striker Position Raycast

**User Story:** As the server, I want the Bot Brain to find a valid Striker position on the bot's baseline that has a clear line-of-sight to the target coin, so that the shot is physically executable.

#### Acceptance Criteria

1. WHEN a valid `(TargetCoin, Pocket)` pair exists, THE `CarromAIBrain` SHALL compute the geometrically ideal Striker X (or Y) position on the `BotSeat`'s `Baseline` that produces the correct `CutAngle` to direct the coin toward the chosen `Pocket`.
2. THE `CarromAIBrain` SHALL perform a 2D Physics raycast from the computed Striker position to the `TargetCoin` center to verify the line-of-sight is clear.
3. IF the line-of-sight raycast is obstructed, THEN THE `CarromAIBrain` SHALL discard that `CandidateShot` and continue evaluating other `(TargetCoin, Pocket)` pairs.
4. THE `CarromAIBrain` SHALL clamp the computed Striker position to the valid range of the `Baseline` (matching the slider range used by human players) before raycasting.

---

### Requirement 4: Kinematics — Shot Calculation

**User Story:** As the server, I want the Bot Brain to calculate the correct aim direction and force magnitude for each candidate shot, so that the physics simulation produces the intended coin trajectory.

#### Acceptance Criteria

1. WHEN a `CandidateShot` passes both raycasts, THE `CarromAIBrain` SHALL calculate the aim direction as the normalized vector from the computed Striker position to the `TargetCoin` center.
2. THE `CarromAIBrain` SHALL calculate `forceMagnitude` as `distance(StrikerPosition, TargetCoin) * [SerializeField] float forceMultiplier`, clamped between `[SerializeField] float minForce` and `[SerializeField] float maxForce`.
3. THE `CarromAIBrain` SHALL expose `[SerializeField] float forceMultiplier`, `[SerializeField] float minForce`, and `[SerializeField] float maxForce` to the Unity Inspector.
4. BEFORE finalizing a `CandidateShot`, THE `CarromAIBrain` SHALL mathematically verify that the aim direction (from the clamped Striker position to the `TargetCoin` center) physically results in the `TargetCoin` moving toward the chosen `Pocket`. This is validated by confirming that the dot product of `(TargetCoin → Pocket)` and the reflected coin velocity vector is positive. IF the geometry requires the Striker to contact the "wrong side" of the coin (i.e., the required cut angle is geometrically impossible from the Striker's fixed axis), THEN the `CandidateShot` SHALL be discarded.

---

### Requirement 5: Scoring — Best Shot Selection

**User Story:** As the server, I want the Bot Brain to score all valid candidate shots and select the best one, so that the AI prefers easier, higher-probability shots over difficult ones.

#### Acceptance Criteria

1. WHEN multiple `CandidateShot` entries exist, THE `CarromAIBrain` SHALL assign each a score based on: `CutAngle` (lower is better), distance from Striker to `TargetCoin` (shorter is better), and local coin clustering density around the `TargetCoin` (less crowded is better).
2. THE `CarromAIBrain` SHALL select the `CandidateShot` with the highest score as the shot to execute.
3. THE `CarromAIBrain` SHALL expose `[SerializeField] float cutAngleWeight`, `[SerializeField] float distanceWeight`, and `[SerializeField] float clusteringWeight` to the Unity Inspector so the scoring formula is tunable without code changes.
4. IF only one `CandidateShot` exists, THE `CarromAIBrain` SHALL execute it without scoring.
5. IF no `CandidateShot` exists after all evaluations, THE `CarromAIBrain` SHALL fall back to the dummy shot and log a warning.

---

### Requirement 6: Humanization — Think Time Delay

**User Story:** As a player, I want the bot to pause before shooting, so that the game feels natural and not robotic.

#### Acceptance Criteria

1. WHEN `TriggerBotShot` is called, THE `CarromAIBrain` SHALL start a coroutine that waits for a random duration sampled uniformly between `[SerializeField] float minThinkTime` and `[SerializeField] float maxThinkTime` before executing any shot logic.
2. THE `CarromAIBrain` SHALL expose `[SerializeField] float minThinkTime` and `[SerializeField] float maxThinkTime` to the Unity Inspector.
3. WHILE the think-time coroutine is waiting, THE `CarromAIBrain` SHALL NOT move the Striker or call `ExecuteShot`.

---

### Requirement 7: Humanization — Striker Slide Animation

**User Story:** As a player, I want to see the bot's Striker smoothly slide to its chosen position on the baseline, so that the bot's intent is visually readable.

#### Acceptance Criteria

1. WHEN the think-time delay completes, THE `CarromAIBrain` SHALL move the `Striker` from its current baseline position to the computed target position by calling `Striker.ResetToBaseline(seatIndex)` followed by a smooth lerp over `[SerializeField] float aimingSpeed` units per second.
2. THE `CarromAIBrain` SHALL broadcast the Striker's intermediate positions during the slide using the existing `SyncAimClientRpc` pathway so all clients see the animation.
3. THE `CarromAIBrain` SHALL expose `[SerializeField] float aimingSpeed` to the Unity Inspector.
4. WHEN the Striker reaches the target position (within `[SerializeField] float positionSnapThreshold` units), THE `CarromAIBrain` SHALL proceed to apply the error cone and fire the shot.

---

### Requirement 8: Humanization — Cone of Error

**User Story:** As a player, I want the bot to miss occasionally, so that the AI difficulty feels fair and beatable.

#### Acceptance Criteria

1. WHEN the Striker has reached its target position, THE `CarromAIBrain` SHALL apply a random angular offset to the final aim direction, sampled uniformly within `[-errorMarginCone/2, +errorMarginCone/2]` degrees.
2. THE `CarromAIBrain` SHALL expose `[SerializeField] float errorMarginCone` to the Unity Inspector.
3. THE `CarromAIBrain` SHALL apply the error offset by rotating the aim direction `Vector2` using `Quaternion.Euler(0, 0, randomOffset)` before passing it to `ExecuteShot`.

---

### Requirement 9: Shot Execution — Decoupled API Compliance

**User Story:** As the server, I want the Bot Brain to fire shots exclusively through the existing `ExecuteShot` API, so that the network turn loop and telemetry pipeline are not disrupted.

#### Acceptance Criteria

1. THE `CarromAIBrain` SHALL call `striker.ExecuteShot(finalDirection, forceMagnitude)` as the sole mechanism for firing a bot shot.
2. THE `CarromAIBrain` SHALL NOT directly modify `Rigidbody2D` velocity or apply forces to any GameObject.
3. THE `CarromAIBrain` SHALL NOT call `TriggerAuthorityTransfer`, `OnShotComplete`, or any turn-loop method directly; those are triggered by `WaitForShotComplete` inside `StrikerController` as they are for human shots.
4. THE `CarromAIBrain` SHALL run exclusively on the server (`if (!IsServer) return;` guard at entry).

---

### Requirement 10: Architecture — Script Modularity

**User Story:** As a developer, I want the AI logic isolated in its own script, so that `CarromGameManager` stays clean and the Bot Brain can be iterated independently.

#### Acceptance Criteria

1. THE `CarromAIBrain` SHALL be implemented as a new `MonoBehaviour` in a new file `Scripts/Carrom/AI/CarromAIBrain.cs`.
2. THE `CarromGameManager` SHALL obtain a reference to `CarromAIBrain` via `[SerializeField] CarromAIBrain aiBrain` and replace the body of `TriggerBotShot` with a single call: `aiBrain.TriggerBotShot(botSeat)`.
3. THE `CarromAIBrain` SHALL receive references to `StrikerController`, `PieceRegistry`, and `BoardScript` via `[SerializeField]` fields, not via `FindObjectOfType` at runtime.
4. THE `CarromAIBrain` SHALL expose all behavioral tuning variables under a `[Header("Bot Behavior")]` Inspector group for discoverability.
