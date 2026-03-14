# Telemetry System Scene Setup Guide

## Critical Issue
Your telemetry system code is complete and correct, but it's not wired up in the Unity scene. The client never receives batches because the components don't have their references set.

## Step-by-Step Setup Instructions

### 1. Create TelemetrySystem GameObject

1. In the Carrom scene, create a new empty GameObject
2. Name it: `TelemetrySystem`
3. Add a `NetworkObject` component to it
4. **CRITICAL**: Check "Don't Destroy With Owner" on the NetworkObject
5. Add the following components to TelemetrySystem:
   - `TelemetryRecorder`
   - `BatchTransmitter`
   - `JitterBuffer`
   - `PlaybackEngine`
   - `PieceRegistry`

### 2. Wire Up Component References

#### A. BatchTransmitter Component
In the Inspector for BatchTransmitter on TelemetrySystem:
- **Batch Interval**: 0.225 (default)
- **Telemetry Recorder**: Drag TelemetrySystem (to reference its TelemetryRecorder component)
- **Jitter Buffer**: Drag TelemetrySystem (to reference its JitterBuffer component)
- **Playback Engine**: Drag TelemetrySystem (to reference its PlaybackEngine component)
- **Carrom Game Manager**: Drag the CarromGameManager GameObject from the scene

#### B. TelemetryRecorder Component
In the Inspector for TelemetryRecorder on TelemetrySystem:
- **Velocity Threshold**: 0.1 (default)
- **Piece Registry**: Drag TelemetrySystem (to reference its PieceRegistry component)

#### C. JitterBuffer Component
In the Inspector for JitterBuffer on TelemetrySystem:
- **Buffer Duration**: 1.0 (default, can adjust between 0.5-2.0)

#### D. PlaybackEngine Component
In the Inspector for PlaybackEngine on TelemetrySystem:
- **Jitter Buffer**: Drag TelemetrySystem (to reference its JitterBuffer component)
- **Piece Registry**: Drag TelemetrySystem (to reference its PieceRegistry component)
- **Audio Source**: Create an AudioSource component on TelemetrySystem, then drag it here
- **Collision Audio Clip**: Drag your Carrom collision sound effect (from Audio/SFX folder)

#### E. CarromGameManager Component
In the Inspector for CarromGameManager:
- **Telemetry Recorder**: Drag TelemetrySystem (to reference its TelemetryRecorder component)
- **Batch Transmitter**: Drag TelemetrySystem (to reference its BatchTransmitter component)
- **Jitter Buffer**: Drag TelemetrySystem (to reference its JitterBuffer component)
- **Playback Engine**: Drag TelemetrySystem (to reference its PlaybackEngine component)
- **Piece Registry**: Drag TelemetrySystem (to reference its PieceRegistry component)
- **Velocity Threshold**: 0.1 (default)

### 3. Register All Game Pieces in PieceRegistry

You need to add a Start() method to PieceRegistry.cs to auto-register all pieces. Here's the code:

```csharp
private void Start()
{
    // Auto-register all game pieces by tag
    RegisterPiecesByTag("Striker", 0, 1);      // Striker: ID 0
    RegisterPiecesByTag("White", 1, 9);        // White coins: IDs 1-9
    RegisterPiecesByTag("Black", 10, 9);       // Black coins: IDs 10-18
    RegisterPiecesByTag("Queen", 19, 1);       // Queen: ID 19
    
    Debug.Log($"[PieceRegistry] Registered {Count} pieces");
}

private void RegisterPiecesByTag(string tag, byte startId, int count)
{
    GameObject[] pieces = GameObject.FindGameObjectsWithTag(tag);
    
    if (pieces.Length != count)
    {
        Debug.LogWarning($"[PieceRegistry] Expected {count} pieces with tag '{tag}', found {pieces.Length}");
    }
    
    for (int i = 0; i < Mathf.Min(pieces.Length, count); i++)
    {
        byte pieceId = (byte)(startId + i);
        RegisterPiece(pieceId, pieces[i]);
        Debug.Log($"[PieceRegistry] Registered {pieces[i].name} with ID {pieceId}");
    }
}
```

### 4. Verify Game Piece Setup

For EVERY game piece (striker, all coins, queen):
1. Ensure it has a `NetworkObject` component
2. Ensure it has a `NetworkPhysicsObject` component
3. Ensure it has a `Rigidbody2D` component
4. Ensure it has the correct tag:
   - Striker: "Striker"
   - White coins: "White"
   - Black coins: "Black"
   - Queen: "Queen"

### 5. Add TelemetrySystem to Network Prefabs

1. In Unity, go to your NetworkManager GameObject
2. Find the "Network Prefabs List"
3. Add TelemetrySystem to the list (or ensure it's spawned at scene start)

**ALTERNATIVE**: If TelemetrySystem should exist in the scene from the start:
1. Place TelemetrySystem in the Carrom scene hierarchy
2. Make sure it's NOT a prefab instance
3. The NetworkObject will auto-spawn when the scene loads

### 6. Test and Verify

After setup, run the game and check the Console for these logs:

#### On Host (when Host shoots):
```
[CarromGameManager] ===== SHOT START =====
[CarromGameManager] Telemetry recording started
[BatchTransmitter] ===== NOTIFYING SHOT START =====
[BatchTransmitter] Spectator client ID: 1
[BatchTransmitter] Shot start RPC sent to client 1
[BatchTransmitter] ===== TRANSMITTED BATCH: X frames to client 1 =====
```

#### On Client (when Host shoots):
```
[BatchTransmitter] ===== CLIENT RECEIVED SHOT START RPC =====
[BatchTransmitter] JitterBuffer initialized with 1.0-second countdown
[Telemetry] Client received batch with X frames.
[BatchTransmitter] Enqueued X frames to JitterBuffer
[Playback] Starting visual interpolation...
```

### 7. Common Issues and Fixes

#### Issue: "TelemetryRecorder reference is NULL"
**Fix**: Wire up the TelemetryRecorder reference in BatchTransmitter Inspector

#### Issue: "JitterBuffer reference is NULL"
**Fix**: Wire up the JitterBuffer reference in BatchTransmitter Inspector

#### Issue: "No spectator found to send batch to"
**Fix**: Ensure both players are connected and the game is in multiplayer mode

#### Issue: Client never receives batches
**Fix**: 
- Verify TelemetrySystem has a NetworkObject component
- Verify NetworkObject is spawned on both clients
- Check that BatchTransmitter is a NetworkBehaviour on a spawned NetworkObject

#### Issue: "No piece found with ID X"
**Fix**: 
- Add the Start() method to PieceRegistry.cs (code provided above)
- Verify all game pieces have the correct tags

## Quick Checklist

- [ ] TelemetrySystem GameObject created
- [ ] NetworkObject added to TelemetrySystem
- [ ] All 5 telemetry components added to TelemetrySystem
- [ ] BatchTransmitter references wired up (4 references)
- [ ] TelemetryRecorder references wired up (2 references)
- [ ] PlaybackEngine references wired up (4 references)
- [ ] CarromGameManager references wired up (5 references)
- [ ] PieceRegistry Start() method added
- [ ] All game pieces have NetworkObject + NetworkPhysicsObject
- [ ] All game pieces have correct tags
- [ ] TelemetrySystem spawned on network (in prefabs list or in scene)

## Next Steps

After completing this setup:
1. Run the game with 2 players
2. Check Console logs on both Host and Client
3. If you see NULL errors, check which component reference is missing
4. If you see "No spectator found", verify both players are connected
5. If client still doesn't receive batches, verify NetworkObject is spawned

The code is correct - this is purely a Unity scene configuration issue.
