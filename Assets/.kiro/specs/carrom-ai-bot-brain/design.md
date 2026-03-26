# Design Document: Carrom AI Bot Brain

## Overview

The Carrom AI Bot Brain replaces the placeholder `TriggerBotShot` in `CarromGameManager.cs` with a self-contained `CarromAIBrain` MonoBehaviour. The brain runs entirely server-side, evaluates the live board state, computes geometrically valid shots using 2D raycasting and vector math, scores candidates, and fires the best shot through the existing `striker.ExecuteShot(direction, forceMagnitude)` API.

The design is intentionally modular: `CarromGameManager` is reduced to a single delegation call, and all AI logic lives in `Scripts/Carrom/AI/CarromAIBrain.cs`. All tuning parameters are `[SerializeField]`-exposed for live Inspector tweaking.

## Architecture

```mermaid
graph TD
    CGM[CarromGameManager] -->|aiBrain.TriggerBotShot(botSeat)| AIB[CarromAIBrain]
    AIB -->|GetAllPieces| PR[PieceRegistry]
    AIB -->|GetPocketPositions / GetBaseline| BS[BoardScript]
    AIB -->|ResetToBaseline / ExecuteShot| SC[StrikerController]
    AIB -->|Physics2D.Raycast| PHY[Unity Physics 2D]
```

**Execution flow (server-only):**

```
TriggerBotShot(botSeat)
  └─ StartCoroutine(BotShotRoutine(botSeat))
       ├─ Wait ThinkTime
       ├─ CollectTargetCoins()
       ├─ BuildCandidateShots()
       │    ├─ foreach TargetCoin
       │    │    └─ foreach Pocket
       │    │         ├─ RaycastCoinToPocket  → skip if blocked
       │    │         ├─ ComputeStrikerPosition on Baseline
       │    │         ├─ RaycastStrikerToCoin  → skip if blocked
       │    │         ├─ ValidateCutAngle (dot product)
       │    │         └─ Build CandidateShot
       ├─ ScoreCandidates()
       ├─ SelectBestCandidate()
       ├─ LerpStrikerToPosition() (SyncAimClientRpc)
       ├─ ApplyErrorCone()
       └─ striker.ExecuteShot(finalDir, force)
```

## Components and Interfaces

### CarromAIBrain (new)

`Scripts/Carrom/AI/CarromAIBrain.cs` — `MonoBehaviour`

**Public API:**
```csharp
public void TriggerBotShot(SeatData botSeat);
```

**Inspector-serialized dependencies:**
```csharp
[SerializeField] StrikerController striker;
[SerializeField] PieceRegistry     pieceRegistry;
[SerializeField] BoardScript       boardScript;
```

**Inspector-serialized tuning (all under `[Header("Bot Behavior")]`):**
```csharp
[Header("Bot Behavior")]
[SerializeField] LayerMask raycastLayerMask;
[SerializeField] float forceMultiplier   = 3f;
[SerializeField] float minForce          = 5f;
[SerializeField] float maxForce          = 25f;
[SerializeField] float cutAngleWeight    = 0.5f;
[SerializeField] float distanceWeight    = 0.3f;
[SerializeField] float clusteringWeight  = 0.2f;
[SerializeField] float minThinkTime      = 0.8f;
[SerializeField] float maxThinkTime      = 2.2f;
[SerializeField] float aimingSpeed       = 4f;
[SerializeField] float positionSnapThreshold = 0.05f;
[SerializeField] float errorMarginCone   = 5f;
```

### CarromGameManager (modified)

`TriggerBotShot` body becomes:
```csharp
private void TriggerBotShot(SeatData botSeat)
{
    if (!IsServer) return;
    aiBrain.TriggerBotShot(botSeat);
}
```

New field:
```csharp
[SerializeField] CarromAIBrain aiBrain;
```

### BoardScript (read-only extension)

Two new public accessor methods are added to expose data the AI needs:

```csharp
// Returns the four pocket world positions
public Vector2[] GetPocketPositions();

// Returns the baseline range for a given seat:
// Seats 0/2: returns (minX, maxX, fixedY)
// Seats 1/3: returns (fixedX, minY, maxY)
public BaselineData GetBaseline(int seatIndex);
```

`BaselineData` is a simple struct:
```csharp
public struct BaselineData
{
    public bool  isHorizontal; // true = seats 0/2 (Y fixed), false = seats 1/3 (X fixed)
    public float fixedAxis;    // the fixed coordinate value
    public float rangeMin;     // slider min
    public float rangeMax;     // slider max
}
```

## Data Models

### CandidateShot

```csharp
private struct CandidateShot
{
    public Vector2 strikerPosition;   // clamped position on baseline
    public Vector2 aimDirection;      // normalized, striker → coin
    public float   forceMagnitude;    // clamped [minForce, maxForce]
    public float   score;             // higher = better
    public float   cutAngleDeg;       // degrees, lower = easier
    public float   distanceToCoin;    // world units
    public float   clusterDensity;    // nearby coin count within radius
}
```

### BaselineData

```csharp
public struct BaselineData
{
    public bool  isHorizontal;
    public float fixedAxis;
    public float rangeMin;
    public float rangeMax;
}
```

### Graveyard threshold

Coins with `position.y >= 500f` are considered pocketed (in the Graveyard) and excluded from targeting. This matches the existing `SendToGraveyard` encoding in `BoardScript` (`1000 + pocketCenter.y`).

## Correctness Properties

*A property is a characteristic or behavior that should hold true across all valid executions of a system — essentially, a formal statement about what the system should do. Properties serve as the bridge between human-readable specifications and machine-verifiable correctness guarantees.*

> Note: Per the requirements document, no automated tests are written for this feature. All validation is visual and manual in the Unity Editor. The properties below are stated for specification clarity and future reference only.

### Property 1: Legal target filtering respects game mode

*For any* board state and game mode, every coin in the `TargetCoin` candidate list must satisfy the mode's legality rule: in Classic mode, only coins whose tag matches the bot's team (plus unsecured Queen); in Freestyle mode, any non-Striker coin with `position.y < 500`.

**Validates: Requirements 1.1, 1.2, 1.3**

### Property 2: Fallback on empty candidate list

*For any* board state where no legal `TargetCoin` candidates exist, the system must fall back to the dummy shot and log a warning rather than throwing an exception or silently doing nothing.

**Validates: Requirements 1.4, 5.5**

### Property 3: Pocket raycast excludes obstructed paths

*For any* `(TargetCoin, Pocket)` pair, if a `Physics2D.Raycast` from the coin toward the pocket is blocked by another piece, that pair must not appear in the valid pocket path set.

**Validates: Requirements 2.1, 2.2, 2.3**

### Property 4: Striker position is clamped to baseline range

*For any* computed ideal striker position, the final position used for raycasting and movement must lie within `[rangeMin, rangeMax]` of the seat's baseline.

**Validates: Requirements 3.1, 3.4**

### Property 5: Line-of-sight check discards blocked shots

*For any* candidate where the raycast from the computed striker position to the target coin is obstructed, that candidate must be discarded and not appear in the final candidate list.

**Validates: Requirements 3.2, 3.3**

### Property 6: Force magnitude is clamped

*For any* computed `forceMagnitude = distance * forceMultiplier`, the value passed to `ExecuteShot` must satisfy `minForce <= forceMagnitude <= maxForce`.

**Validates: Requirements 4.2**

### Property 7: Impossible cut angles are discarded

*For any* candidate shot, if the dot product between the aim direction and the vector from coin to pocket is non-positive (coin would need to move away from the pocket), that candidate must be discarded.

**Validates: Requirements 4.4**

### Property 8: Best shot selection is deterministic given a fixed candidate list

*For any* non-empty candidate list, the selected shot must be the one with the highest composite score (weighted sum of cut angle, distance, and clustering components). If only one candidate exists, it is selected without scoring.

**Validates: Requirements 5.1, 5.2, 5.4**

### Property 9: Think time delay precedes all shot logic

*For any* bot turn, the striker must not move and `ExecuteShot` must not be called until at least `minThinkTime` seconds have elapsed since `TriggerBotShot` was invoked.

**Validates: Requirements 6.1, 6.3**

### Property 10: Error cone is bounded

*For any* final aim direction, the angular offset applied before `ExecuteShot` must lie within `[-errorMarginCone/2, +errorMarginCone/2]` degrees.

**Validates: Requirements 8.1**

### Property 11: ExecuteShot is the sole shot mechanism

*For any* bot shot execution, the only physics-affecting call must be `striker.ExecuteShot(finalDirection, forceMagnitude)`. No direct `Rigidbody2D` force application, no `TriggerAuthorityTransfer`, no `OnShotComplete` call from within `CarromAIBrain`.

**Validates: Requirements 9.1, 9.2, 9.3**

### Property 12: Server-only guard

*For any* call to `TriggerBotShot`, if `IsServer` is false the method must return immediately without executing any logic.

**Validates: Requirements 9.4**

## Error Handling

| Scenario | Handling |
|---|---|
| `striker` reference is null | `Debug.LogError` + early return |
| `pieceRegistry` reference is null | `Debug.LogError` + early return |
| `boardScript` reference is null | `Debug.LogError` + early return |
| No legal target coins | `Debug.LogWarning` + fallback dummy shot (aim at board center, force = 15f) |
| No valid candidate shots | `Debug.LogWarning` + fallback dummy shot |
| All pocket raycasts blocked for a coin | Coin silently skipped (not an error) |
| Striker baseline raycast blocked | Candidate silently discarded |
| `IsServer == false` | Silent early return (`if (!IsServer) return;`) |

The fallback dummy shot mirrors the existing placeholder: `striker.ResetToBaseline(botSeat.SeatIndex)` followed by `striker.ExecuteShot((Vector2.zero - strikerPos).normalized, 15f)`.

## Testing Strategy

All validation for this feature is **visual and manual in the Unity Editor**, as specified in the requirements. No automated unit or property-based tests are written.

**Manual validation checklist:**

- Place bot in seat 0 (South) in a Freestyle game. Verify it waits `ThinkTime`, slides the striker, and pockets a coin.
- Place bot in seat 1 (East). Verify baseline is the east rail and the striker slides along Y.
- Set `errorMarginCone = 0`. Verify shots are geometrically precise.
- Set `errorMarginCone = 45`. Verify shots are visibly scattered.
- Block all pocket paths with coins. Verify fallback dummy shot fires and a warning is logged.
- Set `currentGameMode = Classic`. Verify bot only targets its own team's coins (plus Queen).
- Set `currentGameMode = Freestyle`. Verify bot targets any coin.
- Verify no direct `Rigidbody2D` force calls appear in `CarromAIBrain` (code review).
- Verify `TriggerAuthorityTransfer` and `OnShotComplete` are not called from `CarromAIBrain` (code review).
- In a 2-player + 1 bot game, verify the bot takes its turn correctly in the rotation.
