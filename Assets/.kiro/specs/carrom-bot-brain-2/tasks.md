# Tasks — carrom-bot-brain-2

## Task List

- [x] 1. Write complete CarromAIBrain.cs (Phase 2 upgrade)
  - [x] 1.1 Add BotDifficulty enum and new SerializeField groups (Physics Force Model, Cluster Break, Safety Nudge, Difficulty & Humanization)
  - [x] 1.2 Remove forceMultiplier and errorMarginCone fields and all usages
  - [x] 1.3 Implement SampleGaussian(float mean, float stdDev) using Box-Muller transform
  - [x] 1.4 Implement ComputePhysicsForce — EnergyTransferCoefficient, LinearDragCompensation, clamp
  - [x] 1.5 Implement GetDifficultyScale() returning 1.0 / 0.5 / 0.25 per BotDifficulty
  - [x] 1.6 Apply AngleJitter in BuildCandidateShots before ComputeGhostCoinPos
  - [x] 1.7 Replace distance * forceMultiplier with ComputePhysicsForce in BuildCandidateShots
  - [x] 1.8 Implement FindDensestCluster(out Vector2 centroid, out int clusterSize)
  - [x] 1.9 Implement ExecuteClusterBreak coroutine (baseline aim, cluster force formula, LOS check)
  - [x] 1.10 Implement ExecuteSafetyNudge coroutine (nearest coin, ghost-coin geometry, nudgeForce)
  - [x] 1.11 Refactor BotShotRoutine to implement three-branch ShotPlaybook with Debug.Log strategy selection
  - [x] 1.12 Apply ForceJitter (multiplicative Gaussian) to final forceMagnitude in all non-nudge branches
  - [x] 1.13 Remove Quaternion.Euler errorMarginCone rotation from BotShotRoutine
