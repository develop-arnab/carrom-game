# Requirements Document

## Introduction

Bot Brain 2.0 is a physics-aware, strategically adaptive upgrade to the existing `CarromAIBrain`. The current bot uses a pure-geometry force formula (`distance * multiplier`) that ignores Unity 2D physics realities — friction, mass, and momentum loss at the collision point — producing wildly inconsistent shots. This upgrade replaces the force model with a physics-derived calculation, introduces a three-state strategic playbook (Direct Cut, Cluster Break, Safety Nudge), and replaces the flat random error cone with a difficulty-tiered humanization system that simulates realistic human mistake patterns. All logic remains server-side. All tuning variables are exposed to the Unity Inspector.

## Glossary

- **CarromAIBrain**: The existing `MonoBehaviour` at `Scripts/Carrom/AI/CarromAIBrain.cs` that will be upgraded in-place.
- **PhysicsForceModel**: The new force calculation subsystem that accounts for total travel distance, cut-angle energy loss, and Unity linear drag.
- **TotalTravelDistance**: The sum of two segments: Striker → GhostCoin contact point, plus Coin → Pocket.
- **CutAngle**: The angle (in degrees) between the Striker→Coin direction and the Coin→Pocket direction. A 0° cut is a straight shot; a 90° cut is a full side-cut.
- **EnergyTransferCoefficient**: A scalar in [0, 1] derived from the CutAngle that models how much of the Striker's momentum is transferred to the coin. Approaches 1.0 at 0° and approaches 0 at 90°.
- **LinearDragCompensation**: A multiplier applied to the raw force to counteract Unity's `Rigidbody2D.linearDamping` over the expected travel distance.
- **ShotPlaybook**: The three-state strategic decision tree the bot evaluates each turn: DirectCutShot, ClusterBreak, SafetyNudge.
- **DirectCutShot**: A shot that aims to pocket a specific coin via the ghost-coin geometry.
- **ClusterBreak**: A high-power shot aimed at the geometric centroid of the densest coin cluster on the board, intended to scatter pieces when no direct shots are available.
- **SafetyNudge**: A low-power shot that gently displaces the nearest coin to create future opportunities, used as a last resort.
- **DifficultyProfile**: An enum (`Easy`, `Medium`, `Hard`) that governs the magnitude and type of aiming errors applied to the final shot.
- **AngleJitter**: A small angular perturbation applied to the computed CutAngle before the ghost-coin position is derived, simulating a human misjudging the contact point.
- **ForceJitter**: A multiplicative perturbation applied to the computed `forceMagnitude`, simulating a human under- or over-powering a shot.
- **ClusterCentroid**: The arithmetic mean position of all coins within a configurable radius of the densest coin on the board.
- **GhostCoin**: The contact point the Striker must reach to redirect a coin toward a pocket — one `coinRadius` behind the coin along the Coin→Pocket direction.
- **Striker**: The `StrikerController` component that exposes `ExecuteShot(direction, force)`.
- **PieceRegistry**: The existing registry that maps piece IDs to live GameObjects.
- **BoardScript**: The existing script that exposes `GetPocketPositions()` and `GetBaseline(seatIndex)`.

---

## Requirements

### Requirement 1: Physics-Aware Force Calculation

**User Story:** As a developer, I want the bot to calculate shot force based on total travel distance, cut-angle energy loss, and Unity linear drag, so that coins reliably reach pockets instead of stopping short or flying past them.

#### Acceptance Criteria

1. WHEN computing `forceMagnitude` for a `DirectCutShot`, THE `CarromAIBrain` SHALL calculate it using the formula:
   `forceMagnitude = (strikerToCoin + coinToPocket) * dragCompensation / energyTransfer`
   where `strikerToCoin` is `Distance(StrikerPosition, GhostCoinPos)`, `coinToPocket` is `Distance(CoinPos, PocketPos)`, `dragCompensation` is `LinearDragCompensation`, and `energyTransfer` is `EnergyTransferCoefficient`.

2. THE `CarromAIBrain` SHALL compute `EnergyTransferCoefficient` as `cos(CutAngle)`, where `CutAngle` is the angle in radians between the Striker→GhostCoin direction and the Coin→Pocket direction, clamped to the range [0.15, 1.0] to prevent division by near-zero values on extreme cuts.

3. THE `CarromAIBrain` SHALL compute `LinearDragCompensation` as `1 + (linearDrag * totalDistance * dragDistanceScale)`, where `linearDrag` is a `[SerializeField] float` matching the `Rigidbody2D.linearDamping` value set on the coin prefabs, `totalDistance` is `strikerToCoin + coinToPocket`, and `dragDistanceScale` is a `[SerializeField] float` tuning coefficient.

4. THE `CarromAIBrain` SHALL clamp the final `forceMagnitude` between `[SerializeField] float minForce` and `[SerializeField] float maxForce`.

5. THE `CarromAIBrain` SHALL expose `[SerializeField] float linearDrag`, `[SerializeField] float dragDistanceScale`, `[SerializeField] float minForce`, and `[SerializeField] float maxForce` to the Unity Inspector under a `[Header("Physics Force Model")]` group.

6. THE `CarromAIBrain` SHALL remove the legacy `forceMultiplier` field and all code paths that use the old `distance * multiplier` formula.

---

### Requirement 2: Shot Playbook — Strategic Decision Tree

**User Story:** As a player, I want the bot to evaluate the board and choose between different shot strategies, so that it behaves like a thinking opponent rather than always attempting the same type of shot.

#### Acceptance Criteria

1. WHEN `TriggerBotShot` is called, THE `CarromAIBrain` SHALL evaluate the `ShotPlaybook` in the following priority order: (1) `DirectCutShot`, (2) `ClusterBreak`, (3) `SafetyNudge`.

2. WHEN at least one valid `CandidateShot` exists after the existing raycast pipeline, THE `CarromAIBrain` SHALL select the `DirectCutShot` strategy and execute the highest-scored candidate.

3. WHEN no valid `CandidateShot` exists AND at least one coin cluster of two or more coins is detectable on the board, THE `CarromAIBrain` SHALL select the `ClusterBreak` strategy.

4. WHEN neither a `DirectCutShot` nor a `ClusterBreak` is available, THE `CarromAIBrain` SHALL select the `SafetyNudge` strategy.

5. THE `CarromAIBrain` SHALL log the selected strategy name at each turn using `Debug.Log` so the decision is visible in the Unity Console during testing.

---

### Requirement 3: Cluster Break Shot

**User Story:** As a player, I want the bot to break up coin clusters when no direct shots are available, so that the game progresses and doesn't stall in a deadlock.

#### Acceptance Criteria

1. WHEN the `ClusterBreak` strategy is selected, THE `CarromAIBrain` SHALL identify the `ClusterCentroid` by finding the coin with the highest neighbor count within `[SerializeField] float clusterDetectionRadius` and computing the mean position of that coin and all its neighbors.

2. WHEN the `ClusterCentroid` is identified, THE `CarromAIBrain` SHALL compute a striker position on the bot's baseline that aims directly at the `ClusterCentroid`, using the same baseline clamping logic as `DirectCutShot`.

3. THE `CarromAIBrain` SHALL compute `forceMagnitude` for a `ClusterBreak` as `clusterBreakForceBase + clusterSize * clusterBreakForcePerCoin`, where `clusterSize` is the number of coins in the cluster, clamped to `maxForce`.

4. THE `CarromAIBrain` SHALL expose `[SerializeField] float clusterDetectionRadius`, `[SerializeField] float clusterBreakForceBase`, and `[SerializeField] float clusterBreakForcePerCoin` to the Unity Inspector under a `[Header("Cluster Break")]` group.

5. IF the computed striker position for the `ClusterBreak` is obstructed by a raycast, THEN THE `CarromAIBrain` SHALL fall through to the `SafetyNudge` strategy.

---

### Requirement 4: Safety Nudge Shot

**User Story:** As a player, I want the bot to make a low-power disruptive shot when no better options exist, so that it never passes its turn or fires a meaningless shot at the board center.

#### Acceptance Criteria

1. WHEN the `SafetyNudge` strategy is selected, THE `CarromAIBrain` SHALL identify the nearest live coin to the bot's baseline midpoint as the nudge target.

2. THE `CarromAIBrain` SHALL compute a striker position aimed at the nudge target coin using the same ghost-coin geometry as `DirectCutShot`, without requiring a clear pocket path.

3. THE `CarromAIBrain` SHALL compute `forceMagnitude` for a `SafetyNudge` as a fixed `[SerializeField] float nudgeForce`, bypassing the physics force model.

4. THE `CarromAIBrain` SHALL expose `[SerializeField] float nudgeForce` to the Unity Inspector under a `[Header("Safety Nudge")]` group.

5. IF no live coins exist on the board when `SafetyNudge` is selected, THEN THE `CarromAIBrain` SHALL execute the existing fallback shot (aim at `Vector2.zero`, fixed force) and log a warning.

---

### Requirement 5: Difficulty-Tiered Humanization System

**User Story:** As a player, I want the bot's aiming errors to feel like realistic human mistakes rather than random spray, so that each difficulty level has a distinct and believable play style.

#### Acceptance Criteria

1. THE `CarromAIBrain` SHALL expose a `[SerializeField] BotDifficulty difficulty` enum field with values `Easy`, `Medium`, and `Hard` to the Unity Inspector under a `[Header("Difficulty & Humanization")]` group.

2. WHEN `difficulty` is `Easy`, THE `CarromAIBrain` SHALL apply both `AngleJitter` and `ForceJitter` at their maximum configured magnitudes, simulating a beginner who misjudges both angle and power.

3. WHEN `difficulty` is `Medium`, THE `CarromAIBrain` SHALL apply `AngleJitter` at half its maximum magnitude and `ForceJitter` at half its maximum magnitude.

4. WHEN `difficulty` is `Hard`, THE `CarromAIBrain` SHALL apply `AngleJitter` at one-quarter its maximum magnitude and no `ForceJitter`, simulating an experienced player who aims well but occasionally misjudges the cut angle.

5. THE `CarromAIBrain` SHALL apply `AngleJitter` by perturbing the `CutAngle` used to derive the `GhostCoin` position BEFORE computing the striker position, so the error propagates through the full geometry rather than being a post-hoc direction rotation.

6. THE `CarromAIBrain` SHALL sample `AngleJitter` from a zero-mean Gaussian distribution with standard deviation `angleJitterMaxDeg * difficultyScale`, where `difficultyScale` is 1.0 for Easy, 0.5 for Medium, and 0.25 for Hard.

7. THE `CarromAIBrain` SHALL apply `ForceJitter` as a multiplicative factor sampled from a Gaussian distribution with mean 1.0 and standard deviation `forceJitterMaxFraction * difficultyScale`, applied to the final `forceMagnitude` after physics model calculation.

8. THE `CarromAIBrain` SHALL remove the legacy flat `errorMarginCone` field and the post-hoc `Quaternion.Euler` direction rotation that implements it.

9. THE `CarromAIBrain` SHALL expose `[SerializeField] float angleJitterMaxDeg` and `[SerializeField] float forceJitterMaxFraction` to the Unity Inspector so the error envelope is tunable per difficulty without code changes.

---

### Requirement 6: Gaussian Sampling Utility

**User Story:** As a developer, I want a reusable Gaussian random number generator available within the AI module, so that all jitter systems produce normally-distributed errors rather than uniform distributions.

#### Acceptance Criteria

1. THE `CarromAIBrain` SHALL implement a private static method `SampleGaussian(float mean, float stdDev)` using the Box-Muller transform: `mean + stdDev * sqrt(-2 * ln(U1)) * cos(2π * U2)` where `U1` and `U2` are independent uniform random values in (0, 1].

2. WHEN `stdDev` is 0, THE `SampleGaussian` method SHALL return `mean` without computing the transform.

3. THE `AngleJitter` and `ForceJitter` systems SHALL both call `SampleGaussian` rather than `Random.Range`.

---

### Requirement 7: Inspector Tuning Surface

**User Story:** As a developer, I want all new behavioral parameters grouped and labeled in the Unity Inspector, so that I can tune the bot's behavior during playtesting without touching code.

#### Acceptance Criteria

1. THE `CarromAIBrain` SHALL organize all new `[SerializeField]` fields under the following `[Header]` groups: `"Physics Force Model"`, `"Shot Playbook"`, `"Cluster Break"`, `"Safety Nudge"`, and `"Difficulty & Humanization"`.

2. THE `CarromAIBrain` SHALL retain all existing `[Header("Bot Behavior")]` fields (`raycastLayerMask`, `coinRadius`, `minThinkTime`, `maxThinkTime`, `aimingSpeed`, `positionSnapThreshold`) unchanged.

3. THE `CarromAIBrain` SHALL NOT introduce any new `FindObjectOfType` calls; all new dependencies SHALL be resolved through existing `[SerializeField]` references or static accessors already present in `BoardScript`.

---

### Requirement 8: Backward Compatibility with Shot Execution Pipeline

**User Story:** As a developer, I want Bot Brain 2.0 to remain fully compatible with the existing `ExecuteShot` API and network turn loop, so that no changes are required to `StrikerController`, `CarromGameManager`, or any RPC pathway.

#### Acceptance Criteria

1. THE `CarromAIBrain` SHALL continue to call `striker.ExecuteShot(finalDirection, forceMagnitude)` as the sole shot-firing mechanism for all three playbook strategies.

2. THE `CarromAIBrain` SHALL continue to call `striker.ResetToBaseline(seatIndex)` and `striker.BroadcastPositionToClients()` during the striker slide animation for all strategies.

3. THE `CarromAIBrain` SHALL continue to run exclusively on the server (`if (!IsServer) return;` guard at the `TriggerBotShot` entry point).

4. THE `CarromGameManager` SHALL require no modifications as a result of this upgrade; the `aiBrain.TriggerBotShot(botSeat)` call site remains unchanged.
