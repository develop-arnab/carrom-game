using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;

/// <summary>
/// Difficulty profile for the AI bot.
/// Easy   — full AngleJitter + ForceJitter (difficultyScale = 1.0)
/// Medium — half magnitude on both         (difficultyScale = 0.5)
/// Hard   — quarter AngleJitter, no ForceJitter (difficultyScale = 0.25)
/// </summary>
public enum BotDifficulty { Easy, Medium, Hard }

/// <summary>
/// Server-side AI Bot Brain for Carrom — Phase 2.
/// Evaluates the live board state via a three-state ShotPlaybook
/// (DirectCutShot → ClusterBreak → SafetyNudge), computes physics-aware
/// force, and fires through the existing StrikerController.ExecuteShot API.
/// All logic is server-only. No changes required to StrikerController or
/// CarromGameManager.
/// </summary>
public class CarromAIBrain : NetworkBehaviour
{
    // -------------------------------------------------------------------------
    // DEPENDENCY FIELDS
    // -------------------------------------------------------------------------

    [SerializeField] StrikerController striker;
    [SerializeField] PieceRegistry     pieceRegistry;
    [SerializeField] BoardScript       boardScript;

    // -------------------------------------------------------------------------
    // TUNING FIELDS
    // -------------------------------------------------------------------------

    [Header("Bot Behavior")]
    [SerializeField] LayerMask raycastLayerMask;
    [SerializeField] float coinRadius            = 0.238f;
    [SerializeField] float cutAngleWeight        = 0.5f;
    [SerializeField] float distanceWeight        = 0.3f;
    [SerializeField] float clusteringWeight      = 0.2f;
    [SerializeField] float minThinkTime          = 2.0f;
    [SerializeField] float maxThinkTime          = 3.0f;
    [SerializeField] float aimingSpeed           = 4f;
    [SerializeField] float positionSnapThreshold = 0.05f;

    [Header("Physics Stabilization")]
    // Problem A: striker radius + micro-gap so the collision is purely velocity-driven
    [SerializeField] float strikerRadius         = 0.388f;
    [SerializeField] float depenetrationGap      = 0.01f;
    // Problem B: widen the pocket-path highway beyond the coin's physical radius
    [SerializeField] float pathClearanceMultiplier = 1.4f;
    // Problem C: "Gimme" shot thresholds — short, straight shots get priority + guaranteed force
    [SerializeField] float gimmeMaxCoinToPocket  = 3.0f;
    [SerializeField] float gimmeMaxCutAngleDeg   = 15f;
    [SerializeField] float gimmeForce            = 30f;

    [Header("Physics Force Model")]
    [SerializeField] float linearDrag            = 1.5f;
    [SerializeField] float dragDistanceScale     = 0.1f;
    [SerializeField] float minForce              = 5f;
    [SerializeField] float maxForce              = 100f;

    [Header("Cluster Break")]
    [SerializeField] float clusterDetectionRadius    = 1.5f;
    [SerializeField] float clusterBreakForceBase     = 12f;
    [SerializeField] float clusterBreakForcePerCoin  = 2f;

    [Header("Safety Nudge")]
    [SerializeField] float nudgeForce = 7f;

    [Header("Difficulty & Humanization")]
    [SerializeField] BotDifficulty difficulty         = BotDifficulty.Medium;
    [SerializeField] float         angleJitterMaxDeg  = 8f;
    [SerializeField] float         forceJitterMaxFraction = 0.15f;

    [Header("Puppeteer Animation")]
    // Step B: smooth slide duration (seconds)
    [SerializeField] float slideDuration             = 0.75f;
    // Step C: micro-adjustment — overshoot amount and correction pause
    [SerializeField] float microAdjustOvershoot      = 0.05f;
    [SerializeField] float microAdjustPauseMin       = 0.2f;
    [SerializeField] float microAdjustPauseMax       = 0.4f;
    [SerializeField] float microAdjustCorrectPause   = 0.1f;
    // Step D: charge-up aiming visuals duration
    [SerializeField] float chargeUpDuration          = 0.75f;
    [SerializeField] float chargeUpMaxVisualScale    = 1.2f;

    // -------------------------------------------------------------------------
    // DATA MODELS
    // -------------------------------------------------------------------------

    private struct CandidateShot
    {
        public Vector2 strikerPosition;
        public Vector2 aimDirection;
        public float   forceMagnitude;
        public float   score;
        public float   cutAngleDeg;
        public float   distanceToCoin;
        public float   clusterDensity;
        public float   coinToPocketDist; // used by gimme filter
        public bool    isGimme;          // Problem C: short + straight = guaranteed force
    }

    // -------------------------------------------------------------------------
    // PUBLIC API
    // -------------------------------------------------------------------------

    /// <summary>
    /// Entry point called by CarromGameManager when it is the bot's turn.
    /// Server-only: returns immediately if not running on the server.
    /// </summary>
    public void TriggerBotShot(SeatData botSeat)
    {
        if (!IsServer) return;
        StartCoroutine(BotShotRoutine(botSeat));
    }

    // -------------------------------------------------------------------------
    // BOARD VISION
    // -------------------------------------------------------------------------

    /// <summary>
    /// Returns the list of legal target coins for the bot's current turn.
    /// Filters out graveyard pieces (position.y >= 500) and applies mode/team rules.
    /// Returns null if no valid targets exist (triggers fallback).
    /// </summary>
    private List<GameObject> CollectTargetCoins(SeatData botSeat)
    {
        var targets = new List<GameObject>();
        CarromGameManager mgr = CarromGameManager.Instance;

        for (byte id = 1; id <= 19; id++)
        {
            GameObject piece = pieceRegistry.GetPiece(id);
            if (piece == null) continue;
            if (piece.transform.position.y >= 500f) continue;

            string tag = piece.tag;

            if (CarromGameManager.ActiveRuleset == GameMode.Classic)
            {
                string teamTag = botSeat.Team.ToString();
                if (tag == teamTag)
                {
                    targets.Add(piece);
                }
                else if (tag == "Queen" &&
                         !mgr.hostSecuredQueen.Value &&
                         !mgr.clientSecuredQueen.Value)
                {
                    targets.Add(piece);
                }
            }
            else
            {
                if (tag == "White" || tag == "Black" || tag == "Queen")
                    targets.Add(piece);
            }
        }

        if (targets.Count == 0)
        {
            Debug.LogWarning("[CarromAIBrain] CollectTargetCoins: no legal target coins found — triggering fallback shot");
            return null;
        }

        return targets;
    }

    // -------------------------------------------------------------------------
    // PATHFINDING — POCKET CLEARANCE
    // -------------------------------------------------------------------------

    /// <summary>
    /// Returns the subset of pocket positions with an unobstructed path from
    /// the given coin's center.
    /// Problem B fix: CircleCast radius = coinRadius * pathClearanceMultiplier so the
    /// bot only attempts shots with a wide-open "highway" — a 1mm gap between blockers
    /// is no longer considered clear, preventing grazing misses.
    /// </summary>
    private List<Vector2> GetValidPocketPaths(GameObject coin)
    {
        var validPockets = new List<Vector2>();
        Vector2[]  pocketPositions = BoardScript.GetPocketPositions();
        Vector2    coinPos         = coin.transform.position;
        Collider2D coinCollider    = coin.GetComponent<Collider2D>();
        float      castRadius      = coinRadius * pathClearanceMultiplier; // widened highway

        foreach (Vector2 pocketPos in pocketPositions)
        {
            Vector2 direction = pocketPos - coinPos;
            float   distance  = direction.magnitude;
            if (distance < Mathf.Epsilon) continue;

            Vector2 dir = direction / distance;

            bool wasEnabled = false;
            if (coinCollider != null) { wasEnabled = coinCollider.enabled; coinCollider.enabled = false; }

            RaycastHit2D hit = Physics2D.CircleCast(coinPos, castRadius, dir, distance, raycastLayerMask);

            if (coinCollider != null) coinCollider.enabled = wasEnabled;

            bool pathClear = hit.collider == null ||
                             hit.collider.GetComponent<BoardScript>() != null;

            if (pathClear)
                validPockets.Add(pocketPos);
            else
                Debug.Log($"[AIBrain] {coin.name} -> Pocket {pocketPos}: pocket path blocked by '{hit.collider.name}' (castRadius={castRadius:F3})");
        }

        return validPockets;
    }

    // -------------------------------------------------------------------------
    // PATHFINDING — GHOST COIN & STRIKER BASELINE
    // -------------------------------------------------------------------------

    /// <summary>
    /// Computes the ghost coin position: the point the Striker's CENTER must reach
    /// to transfer momentum toward the pocket.
    /// Offset = coinRadius + strikerRadius + depenetrationGap so the striker's
    /// physical volume never overlaps the coin, preventing Unity depenetration explosions.
    /// An optional angleOffsetDeg rotates the coin→pocket direction before derivation
    /// to simulate AngleJitter upstream of the geometry.
    /// </summary>
    private Vector2 ComputeGhostCoinPos(Vector2 coinPos, Vector2 pocketPos, float angleOffsetDeg = 0f)
    {
        Vector2 coinToPocket = (pocketPos - coinPos).normalized;
        if (angleOffsetDeg != 0f)
            coinToPocket = RotateVector(coinToPocket, angleOffsetDeg);
        float contactOffset = coinRadius + strikerRadius + depenetrationGap;
        return coinPos - coinToPocket * contactOffset;
    }

    /// <summary>
    /// Rotates a 2D vector by angleDeg degrees (counter-clockwise).
    /// </summary>
    private static Vector2 RotateVector(Vector2 v, float angleDeg)
    {
        float rad = angleDeg * Mathf.Deg2Rad;
        float cos = Mathf.Cos(rad);
        float sin = Mathf.Sin(rad);
        return new Vector2(v.x * cos - v.y * sin, v.x * sin + v.y * cos);
    }

    /// <summary>
    /// Projects the ghost coin position onto the bot's baseline, clamps it to
    /// the valid slider range, then validates the path via CircleCast.
    /// Returns null if obstructed.
    /// </summary>
    private Vector2? TryGetStrikerPosition(Vector2 ghostCoinPos, BaselineData baseline, Collider2D coinCollider)
    {
        Vector2 strikerPos = ComputeStrikerPosition(ghostCoinPos, baseline);
        Vector2 toGhost    = ghostCoinPos - strikerPos;
        float   distance   = toGhost.magnitude;

        if (distance < Mathf.Epsilon) return strikerPos;

        bool wasEnabled = false;
        if (coinCollider != null) { wasEnabled = coinCollider.enabled; coinCollider.enabled = false; }

        RaycastHit2D hit = Physics2D.CircleCast(
            strikerPos, coinRadius, toGhost / distance, distance, raycastLayerMask);

        if (coinCollider != null) coinCollider.enabled = wasEnabled;

        return hit.collider != null ? (Vector2?)null : strikerPos;
    }

    /// <summary>
    /// Computes and clamps the striker position on the baseline so it aligns
    /// with the ghost coin contact point.
    /// </summary>
    private Vector2 ComputeStrikerPosition(Vector2 ghostCoinPos, BaselineData baseline)
    {
        if (baseline.isHorizontal)
        {
            float clampedX = Mathf.Clamp(ghostCoinPos.x, baseline.rangeMin, baseline.rangeMax);
            return new Vector2(clampedX, baseline.fixedAxis);
        }
        else
        {
            float clampedY = Mathf.Clamp(ghostCoinPos.y, baseline.rangeMin, baseline.rangeMax);
            return new Vector2(baseline.fixedAxis, clampedY);
        }
    }

    // -------------------------------------------------------------------------
    // PHYSICS FORCE MODEL
    // -------------------------------------------------------------------------

    /// <summary>
    /// Computes physics-aware force for a DirectCutShot.
    /// Formula: totalDistance * dragCompensation / energyTransfer, clamped to [minForce, maxForce].
    /// </summary>
    private float ComputePhysicsForce(Vector2 strikerPos, Vector2 ghostCoinPos, Vector2 coinPos, Vector2 pocketPos, float cutAngleDeg)
    {
        float strikerToCoin = Vector2.Distance(strikerPos, ghostCoinPos);
        float coinToPocket  = Vector2.Distance(coinPos, pocketPos);
        float totalDistance = strikerToCoin + coinToPocket;

        float cutAngleRad      = cutAngleDeg * Mathf.Deg2Rad;
        float energyTransfer   = Mathf.Clamp(Mathf.Cos(cutAngleRad), 0.15f, 1.0f);
        float dragCompensation = 1f + (linearDrag * totalDistance * dragDistanceScale);

        float force = totalDistance * dragCompensation / energyTransfer;
        return Mathf.Clamp(force, minForce, maxForce);
    }

    // -------------------------------------------------------------------------
    // DIFFICULTY & HUMANIZATION
    // -------------------------------------------------------------------------

    /// <summary>
    /// Returns the difficulty scale factor: Easy=1.0, Medium=0.5, Hard=0.25.
    /// </summary>
    private float GetDifficultyScale()
    {
        switch (difficulty)
        {
            case BotDifficulty.Easy:   return 1.0f;
            case BotDifficulty.Medium: return 0.5f;
            case BotDifficulty.Hard:   return 0.25f;
            default:                   return 0.5f;
        }
    }

    /// <summary>
    /// Samples a value from a Gaussian distribution using the Box-Muller transform.
    /// Returns mean directly when stdDev is 0 to avoid log(0).
    /// </summary>
    private static float SampleGaussian(float mean, float stdDev)
    {
        if (stdDev == 0f) return mean;

        float u1 = Mathf.Max(Random.value, 1e-6f); // clamp away from 0 for log safety
        float u2 = Mathf.Max(Random.value, 1e-6f);
        float z  = Mathf.Sqrt(-2f * Mathf.Log(u1)) * Mathf.Cos(2f * Mathf.PI * u2);
        return mean + stdDev * z;
    }

    // -------------------------------------------------------------------------
    // CANDIDATE SHOT BUILDING — DirectCutShot pipeline
    // -------------------------------------------------------------------------

    /// <summary>
    /// Iterates every target coin × every valid pocket, applies AngleJitter
    /// before ghost-coin geometry, computes physics force, and returns all
    /// scored candidates. Deep telemetry logs explain every rejection.
    /// </summary>
    private List<CandidateShot> BuildCandidateShots(List<GameObject> targetCoins, SeatData botSeat)
    {
        var candidates   = new List<CandidateShot>();
        BaselineData baseline = BoardScript.GetBaseline(botSeat.SeatIndex);
        float diffScale  = GetDifficultyScale();
        float jitterStdDev = angleJitterMaxDeg * diffScale;

        Debug.Log($"[AIBrain] BuildCandidateShots — seat {botSeat.SeatIndex}, {targetCoins.Count} coins, angleJitterStdDev={jitterStdDev:F2}°");

        foreach (GameObject coin in targetCoins)
        {
            Vector2    coinPos      = coin.transform.position;
            Collider2D coinCollider = coin.GetComponent<Collider2D>();

            List<Vector2> validPockets = GetValidPocketPaths(coin);
            if (validPockets.Count == 0)
            {
                Debug.Log($"[AIBrain] {coin.name} @ {coinPos}: SKIPPED — all 4 pocket paths blocked");
                continue;
            }

            Debug.Log($"[AIBrain] {coin.name} @ {coinPos}: {validPockets.Count} pocket(s) reachable");

            foreach (Vector2 pocketPos in validPockets)
            {
                // Apply AngleJitter BEFORE ghost-coin geometry (Req 5.5)
                float angleJitter    = SampleGaussian(0f, jitterStdDev);
                Vector2 ghostCoinPos = ComputeGhostCoinPos(coinPos, pocketPos, angleJitter);

                Vector2? strikerPosNullable = TryGetStrikerPosition(ghostCoinPos, baseline, coinCollider);
                if (strikerPosNullable == null)
                {
                    // Diagnostic re-run to name the blocker
                    Vector2 strikerPos2 = ComputeStrikerPosition(ghostCoinPos, baseline);
                    Vector2 toGhost2    = ghostCoinPos - strikerPos2;
                    float   dist2       = toGhost2.magnitude;

                    bool wasEnabled = false;
                    if (coinCollider != null) { wasEnabled = coinCollider.enabled; coinCollider.enabled = false; }
                    RaycastHit2D diagHit = dist2 > Mathf.Epsilon
                        ? Physics2D.CircleCast(strikerPos2, coinRadius, toGhost2 / dist2, dist2, raycastLayerMask)
                        : default;
                    if (coinCollider != null) coinCollider.enabled = wasEnabled;

                    string blocker = diagHit.collider != null ? diagHit.collider.name : "unknown";
                    Debug.Log($"[AIBrain] {coin.name} -> Pocket {pocketPos}: REJECTED — striker LOS blocked by '{blocker}' (strikerPos={strikerPos2}, ghost={ghostCoinPos})");
                    continue;
                }

                Vector2 strikerPos   = strikerPosNullable.Value;
                Vector2 aimDirection = (ghostCoinPos - strikerPos).normalized;
                Vector2 coinToPocket = (pocketPos - coinPos).normalized;
                float   dot          = Vector2.Dot(aimDirection, coinToPocket);

                if (dot <= 0f)
                {
                    Debug.Log($"[AIBrain] {coin.name} -> Pocket {pocketPos}: REJECTED — impossible cut angle (dot={dot:F3})");
                    continue;
                }

                float cutAngleDeg    = Vector2.Angle(aimDirection, coinToPocket);
                float forceMagnitude = ComputePhysicsForce(strikerPos, ghostCoinPos, coinPos, pocketPos, cutAngleDeg);
                float clusterDensity = ComputeClusterDensity(coinPos);
                float coinToPocketDist = Vector2.Distance(coinPos, pocketPos);

                // Problem C: flag gimme shots — short distance + straight angle
                bool isGimme = coinToPocketDist <= gimmeMaxCoinToPocket &&
                               cutAngleDeg      <= gimmeMaxCutAngleDeg;

                Debug.Log($"[AIBrain] {coin.name} -> Pocket {pocketPos}: ACCEPTED — cutAngle={cutAngleDeg:F1}°, force={forceMagnitude:F1}, cluster={clusterDensity}, jitter={angleJitter:F2}°, gimme={isGimme}");

                candidates.Add(new CandidateShot
                {
                    strikerPosition  = strikerPos,
                    aimDirection     = aimDirection,
                    forceMagnitude   = forceMagnitude,
                    cutAngleDeg      = cutAngleDeg,
                    distanceToCoin   = Vector2.Distance(strikerPos, coinPos),
                    clusterDensity   = clusterDensity,
                    coinToPocketDist = coinToPocketDist,
                    isGimme          = isGimme,
                    score            = 0f
                });
            }
        }

        for (int i = 0; i < candidates.Count; i++)
        {
            CandidateShot c = candidates[i];
            ScoreCandidate(ref c);
            candidates[i] = c;
        }

        Debug.Log($"[AIBrain] BuildCandidateShots complete — {candidates.Count} valid candidate(s)");
        return candidates;
    }

    // -------------------------------------------------------------------------
    // SCORING
    // -------------------------------------------------------------------------

    private static readonly Collider2D[] _clusterBuffer = new Collider2D[20];

    private float ComputeClusterDensity(Vector2 coinPos)
    {
        int count = Physics2D.OverlapCircleNonAlloc(coinPos, 1.5f, _clusterBuffer);
        return Mathf.Max(0, count - 1);
    }

    private void ScoreCandidate(ref CandidateShot c)
    {
        c.score = (1f - c.cutAngleDeg / 90f)                   * cutAngleWeight
                + (1f - Mathf.Clamp01(c.distanceToCoin / 10f)) * distanceWeight
                + (1f - Mathf.Clamp01(c.clusterDensity / 5f))  * clusteringWeight;
    }

    private CandidateShot? SelectBestCandidate(List<CandidateShot> candidates)
    {
        if (candidates == null || candidates.Count == 0)
        {
            Debug.LogWarning("[CarromAIBrain] SelectBestCandidate: no valid candidates");
            return null;
        }

        // Problem C: gimme priority — find the shortest-distance gimme and take it immediately.
        // Override its force with gimmeForce to guarantee the coin reaches the pocket.
        CandidateShot? bestGimme = null;
        float          bestGimmeDist = float.MaxValue;
        for (int i = 0; i < candidates.Count; i++)
        {
            if (candidates[i].isGimme && candidates[i].coinToPocketDist < bestGimmeDist)
            {
                bestGimmeDist = candidates[i].coinToPocketDist;
                CandidateShot g = candidates[i];
                g.forceMagnitude = gimmeForce;
                bestGimme = g;
            }
        }
        if (bestGimme != null)
        {
            Debug.Log($"[CarromAIBrain] SelectBestCandidate: GIMME shot selected — coinToPocket={bestGimmeDist:F2}, force overridden to {gimmeForce:F1}");
            return bestGimme;
        }

        if (candidates.Count == 1) return candidates[0];

        CandidateShot best = candidates[0];
        for (int i = 1; i < candidates.Count; i++)
            if (candidates[i].score > best.score) best = candidates[i];
        return best;
    }

    // -------------------------------------------------------------------------
    // CLUSTER BREAK
    // -------------------------------------------------------------------------

    /// <summary>
    /// Finds the coin with the most neighbors within clusterDetectionRadius.
    /// Returns true if a cluster of 2+ coins is found; outputs centroid and size.
    /// </summary>
    private bool FindDensestCluster(out Vector2 centroid, out int clusterSize)
    {
        centroid    = Vector2.zero;
        clusterSize = 0;

        int      bestCount  = 0;
        Vector2  bestCenter = Vector2.zero;
        var      bestNeighbors = new List<Vector2>();

        for (byte id = 1; id <= 19; id++)
        {
            GameObject piece = pieceRegistry.GetPiece(id);
            if (piece == null || piece.transform.position.y >= 500f) continue;

            Vector2 pos      = piece.transform.position;
            var     neighbors = new List<Vector2> { pos };

            for (byte nid = 1; nid <= 19; nid++)
            {
                if (nid == id) continue;
                GameObject neighbor = pieceRegistry.GetPiece(nid);
                if (neighbor == null || neighbor.transform.position.y >= 500f) continue;

                Vector2 npos = neighbor.transform.position;
                if (Vector2.Distance(pos, npos) <= clusterDetectionRadius)
                    neighbors.Add(npos);
            }

            if (neighbors.Count > bestCount)
            {
                bestCount     = neighbors.Count;
                bestNeighbors = neighbors;
                bestCenter    = pos;
            }
        }

        if (bestCount < 2) return false; // no cluster worth breaking

        // Compute centroid as mean of all coins in the cluster
        Vector2 sum = Vector2.zero;
        foreach (Vector2 p in bestNeighbors) sum += p;
        centroid    = sum / bestNeighbors.Count;
        clusterSize = bestNeighbors.Count;
        return true;
    }

    // -------------------------------------------------------------------------
    // COROUTINE — FULL BOT SHOT SEQUENCE (ShotPlaybook)
    // -------------------------------------------------------------------------

    private IEnumerator BotShotRoutine(SeatData botSeat)
    {
        // Think delay
        yield return new WaitForSeconds(Random.Range(minThinkTime, maxThinkTime));

        // --- Phase 1: attempt DirectCutShot ---
        List<GameObject> targetCoins = CollectTargetCoins(botSeat);
        CandidateShot?   bestShot    = null;

        if (targetCoins != null && targetCoins.Count > 0)
        {
            List<CandidateShot> candidates = BuildCandidateShots(targetCoins, botSeat);
            bestShot = SelectBestCandidate(candidates);
        }

        if (bestShot != null)
        {
            Debug.Log("[CarromAIBrain] ShotPlaybook: selected strategy = DirectCutShot");
            yield return StartCoroutine(ExecuteDirectCutShot(botSeat, bestShot.Value));
            yield break;
        }

        // --- Phase 2: attempt ClusterBreak ---
        if (FindDensestCluster(out Vector2 centroid, out int clusterSize))
        {
            BaselineData baseline    = BoardScript.GetBaseline(botSeat.SeatIndex);
            Vector2      strikerPos  = ComputeStrikerPosition(centroid, baseline);
            Vector2      toCluster   = centroid - strikerPos;
            float        dist        = toCluster.magnitude;
            bool         losBlocked  = false;

            if (dist > Mathf.Epsilon)
            {
                RaycastHit2D hit = Physics2D.CircleCast(strikerPos, coinRadius, toCluster / dist, dist, raycastLayerMask);
                losBlocked = hit.collider != null;
            }

            if (!losBlocked)
            {
                Debug.Log($"[CarromAIBrain] ShotPlaybook: selected strategy = ClusterBreak (clusterSize={clusterSize}, centroid={centroid})");
                yield return StartCoroutine(ExecuteClusterBreak(botSeat, strikerPos, centroid, clusterSize));
                yield break;
            }

            Debug.Log("[CarromAIBrain] ClusterBreak: LOS blocked — falling through to SafetyNudge");
        }

        // --- Phase 3: SafetyNudge ---
        Debug.Log("[CarromAIBrain] ShotPlaybook: selected strategy = SafetyNudge");
        yield return StartCoroutine(ExecuteSafetyNudge(botSeat));
    }

    // -------------------------------------------------------------------------
    // SHOT EXECUTION — DirectCutShot
    // -------------------------------------------------------------------------

    private IEnumerator ExecuteDirectCutShot(SeatData botSeat, CandidateShot shot)
    {
        // Apply ForceJitter (Hard difficulty: stdDev=0, factor=1.0 exactly)
        float diffScale   = GetDifficultyScale();
        float jitterStdDev = (difficulty == BotDifficulty.Hard) ? 0f : forceJitterMaxFraction * diffScale;
        float jitterFactor = SampleGaussian(1.0f, jitterStdDev);
        float finalForce   = Mathf.Clamp(shot.forceMagnitude * jitterFactor, minForce, maxForce);

        Debug.Log($"[AIBrain] DirectCutShot — force={shot.forceMagnitude:F1} * jitter={jitterFactor:F3} = {finalForce:F1}");

        yield return StartCoroutine(SlideAndFire(botSeat, shot.strikerPosition, shot.aimDirection, finalForce));
    }

    // -------------------------------------------------------------------------
    // SHOT EXECUTION — ClusterBreak
    // -------------------------------------------------------------------------

    private IEnumerator ExecuteClusterBreak(SeatData botSeat, Vector2 strikerPos, Vector2 centroid, int clusterSize)
    {
        float force = Mathf.Clamp(
            clusterBreakForceBase + clusterSize * clusterBreakForcePerCoin,
            minForce, maxForce);

        // Apply ForceJitter (same as DirectCutShot)
        float diffScale    = GetDifficultyScale();
        float jitterStdDev = (difficulty == BotDifficulty.Hard) ? 0f : forceJitterMaxFraction * diffScale;
        float jitterFactor = SampleGaussian(1.0f, jitterStdDev);
        float finalForce   = Mathf.Clamp(force * jitterFactor, minForce, maxForce);

        Vector2 aimDir = (centroid - strikerPos).normalized;
        Debug.Log($"[AIBrain] ClusterBreak — clusterSize={clusterSize}, force={finalForce:F1}, aim={aimDir}");

        yield return StartCoroutine(SlideAndFire(botSeat, strikerPos, aimDir, finalForce));
    }

    // -------------------------------------------------------------------------
    // SHOT EXECUTION — SafetyNudge
    // -------------------------------------------------------------------------

    private IEnumerator ExecuteSafetyNudge(SeatData botSeat)
    {
        BaselineData baseline = BoardScript.GetBaseline(botSeat.SeatIndex);
        Vector2 baselineMid   = baseline.isHorizontal
            ? new Vector2((baseline.rangeMin + baseline.rangeMax) * 0.5f, baseline.fixedAxis)
            : new Vector2(baseline.fixedAxis, (baseline.rangeMin + baseline.rangeMax) * 0.5f);

        // Find nearest live coin to baseline midpoint
        GameObject nearestCoin  = null;
        float      nearestDist  = float.MaxValue;

        for (byte id = 1; id <= 19; id++)
        {
            GameObject piece = pieceRegistry.GetPiece(id);
            if (piece == null || piece.transform.position.y >= 500f) continue;

            float d = Vector2.Distance(piece.transform.position, baselineMid);
            if (d < nearestDist) { nearestDist = d; nearestCoin = piece; }
        }

        if (nearestCoin == null)
        {
            Debug.LogWarning("[CarromAIBrain] SafetyNudge: no live coins found — executing fallback shot");
            ExecuteFallbackShot(botSeat);
            yield break;
        }

        Vector2    coinPos      = nearestCoin.transform.position;
        Collider2D coinCollider = nearestCoin.GetComponent<Collider2D>();

        // Use ghost-coin geometry aimed at board center (no pocket path required)
        Vector2 ghostCoinPos = ComputeGhostCoinPos(coinPos, Vector2.zero);
        Vector2 strikerPos   = ComputeStrikerPosition(ghostCoinPos, baseline);
        Vector2 aimDir       = (ghostCoinPos - strikerPos).normalized;

        Debug.Log($"[AIBrain] SafetyNudge — target={nearestCoin.name}, force={nudgeForce:F1}");

        yield return StartCoroutine(SlideAndFire(botSeat, strikerPos, aimDir, nudgeForce));
    }

    // -------------------------------------------------------------------------
    // SHARED SLIDE-AND-FIRE COROUTINE  (Puppeteer — Steps B, C, D)
    // -------------------------------------------------------------------------

    /// <summary>
    /// The full "Grand Illusion" sequence used by all three playbook branches:
    ///   Step B — SmoothStep ease-in/out slide to target position
    ///   Step C — Micro-adjustment: overshoot → pause → correct to true target
    ///   Step D — Charge-up: animate the force-field aiming arrow over chargeUpDuration
    ///            then fire via ExecuteShot
    /// </summary>
    private IEnumerator SlideAndFire(SeatData botSeat, Vector2 targetPos2D, Vector2 aimDirection, float forceMagnitude)
    {
        striker.ResetToBaseline(botSeat.SeatIndex);
        striker.BroadcastPositionToClients(); // RPC #1: announce baseline start

        Vector3 startPos  = striker.transform.position;
        Vector3 targetPos = new Vector3(targetPos2D.x, targetPos2D.y, 0f);

        // ── Step B: SmoothStep ease-in / ease-out slide ──────────────────────
        float elapsed = 0f;
        while (elapsed < slideDuration)
        {
            elapsed += Time.deltaTime;
            float t  = Mathf.Clamp01(elapsed / slideDuration);
            // SmoothStep: 3t²-2t³  — fast in the middle, slow at both ends
            float smooth = t * t * (3f - 2f * t);
            striker.transform.position = Vector3.Lerp(startPos, targetPos, smooth);

            // Broadcast every frame so remote clients see the smooth glide
            striker.BroadcastPositionToClients();
            yield return null;
        }
        striker.transform.position = targetPos;

        // ── Step C: Micro-adjustment (overshoot → pause → correct) ───────────
        // Compute overshoot: push slightly past the target along the slide direction
        Vector3 slideDir   = (targetPos - startPos);
        Vector3 overshootPos = targetPos;
        if (slideDir.sqrMagnitude > Mathf.Epsilon)
            overshootPos = targetPos + slideDir.normalized * microAdjustOvershoot;

        // Glide to overshoot in ~0.08 s
        elapsed = 0f;
        float overshootDur = 0.08f;
        Vector3 preOvershoot = striker.transform.position;
        while (elapsed < overshootDur)
        {
            elapsed += Time.deltaTime;
            float t  = Mathf.Clamp01(elapsed / overshootDur);
            striker.transform.position = Vector3.Lerp(preOvershoot, overshootPos, t);
            striker.BroadcastPositionToClients();
            yield return null;
        }

        // Pause — the "human is settling"
        yield return new WaitForSeconds(microAdjustCorrectPause);

        // Correct back to the true target
        elapsed = 0f;
        Vector3 postOvershoot = striker.transform.position;
        while (elapsed < overshootDur)
        {
            elapsed += Time.deltaTime;
            float t  = Mathf.Clamp01(elapsed / overshootDur);
            striker.transform.position = Vector3.Lerp(postOvershoot, targetPos, t);
            striker.BroadcastPositionToClients();
            yield return null;
        }
        striker.transform.position = targetPos;

        // Pause — the "human is locked in"
        float settlePause = Random.Range(microAdjustPauseMin, microAdjustPauseMax);
        yield return new WaitForSeconds(settlePause);

        striker.BroadcastPositionToClients(); // RPC #2: confirm locked position

        // ── Step D: Charge-up with Aim Wiggle ────────────────────────────────
        // Start from a "fake" aim that is offset 3–5° from the true direction,
        // then Slerp it toward the true aimDirection over chargeUpDuration.
        // This makes the force-field arrow visibly sweep into position — exactly
        // how a human "perfects" their angle before releasing.
        Vector3 strikerWorld   = striker.transform.position;
        Vector3 trueAim3       = new Vector3(aimDirection.x, aimDirection.y, 0f);
        float   wiggleOffsetDeg = Random.Range(3f, 5f) * (Random.value < 0.5f ? 1f : -1f);
        Vector3 fakeAim3       = RotateVector(aimDirection, wiggleOffsetDeg);
        fakeAim3.z = 0f;

        elapsed = 0f;
        while (elapsed < chargeUpDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / chargeUpDuration);

            // Slerp the aim direction from fake → true (linear t feels natural here)
            Vector3 currentAim = Vector3.Slerp(fakeAim3, trueAim3, t);
            Vector3 lookTarget = strikerWorld + currentAim.normalized;

            // Scale ease-in: ramps from 0 → chargeUpMaxVisualScale
            float scale = Mathf.Lerp(0f, chargeUpMaxVisualScale, t * t);
            striker.SimulateAimingVisuals(lookTarget, new Vector3(scale, scale, scale));
            yield return null;
        }

        // Lock onto the true aim for one final frame at full scale
        striker.SimulateAimingVisuals(
            strikerWorld + trueAim3.normalized,
            Vector3.one * chargeUpMaxVisualScale);

        // Hold at full charge for a brief beat before release
        yield return new WaitForSeconds(0.05f);

        // ── Fire ─────────────────────────────────────────────────────────────
        striker.ExecuteShot(aimDirection, forceMagnitude);
    }

    // -------------------------------------------------------------------------
    // FALLBACK SHOT
    // -------------------------------------------------------------------------

    private void ExecuteFallbackShot(SeatData botSeat)
    {
        striker.ResetToBaseline(botSeat.SeatIndex);
        Vector2 strikerPos2D = striker.transform.position;
        Vector2 direction    = (Vector2.zero - strikerPos2D).normalized;
        striker.ExecuteShot(direction, 15f);
    }
}
