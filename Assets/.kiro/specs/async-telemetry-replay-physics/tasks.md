# Implementation Plan: Asynchronous Telemetry Replay Physics

## Overview

This implementation plan breaks down the async telemetry replay architecture into lean, rapid implementation tasks. The system replaces real-time physics synchronization with a recording-and-replay model where the active player experiences zero-latency local physics while the spectating player views smooth delayed playback.

The implementation follows this sequence:
1. Core data structures using NGO's INetworkSerializable
2. Recording system with collision audio capture
3. Network transmission via NGO RPCs (no custom serialization)
4. Jitter buffer and playback with audio replay
5. Integration with existing game systems using Rigidbody2D.simulated = false

## Key Implementation Principles

- **No Test Scripts**: Validation through actual gameplay, not TDD
- **NGO Native Serialization**: Use INetworkSerializable, FastBufferWriter/Reader
- **No Compression**: 13-14 bytes per piece is too small to benefit from compression
- **No Custom Retries**: Rely on NGO's Reliable Sequenced channel for packet handling
- **Physics Disable**: Use Rigidbody2D.simulated = false, not kinematic mode
- **Audio Game Feel**: Capture and replay collision velocities for authentic sound

## Tasks

- [x] 1. Create core data structures with NGO serialization and audio support
  - Create `Scripts/Carrom/Telemetry/PieceState.cs` struct implementing INetworkSerializable
  - Add fields: byte pieceId, float xPosition, float yPosition, float zRotation, float collisionVelocity
  - Implement NetworkSerialize method using FastBufferWriter/FastBufferReader
  - Create `Scripts/Carrom/Telemetry/PhysicsFrame.cs` struct implementing INetworkSerializable
  - Add fields: float timestamp, int pieceCount, PieceState[] pieces (fixed size 20)
  - Implement NetworkSerialize method for PhysicsFrame
  - Create `Scripts/Carrom/Telemetry/TelemetryBatch.cs` struct implementing INetworkSerializable
  - Add fields: int frameCount, PhysicsFrame[] frames
  - Implement NetworkSerialize method for TelemetryBatch
  - Create `Scripts/Carrom/Telemetry/EndStatePayload.cs` struct implementing INetworkSerializable
  - Add fields: int pieceCount, PieceState[] finalStates
  - Implement NetworkSerialize method for EndStatePayload
  - Create `Scripts/Carrom/Telemetry/PieceRegistry.cs` class with bidirectional Dictionary mappings
  - Implement RegisterPiece, GetPiece, GetId methods
  - Assign piece IDs: Striker=0, White coins=1-9, Black coins=10-18, Queen=19
  - _Requirements: 4.3, 10.2, 11.1, 13.2, 16.3_

- [x] 2. Implement allocation-free telemetry buffer
  - Create `Scripts/Carrom/Telemetry/TelemetryBuffer.cs` class with ring buffer implementation
  - Use pre-allocated PhysicsFrame array with fixed capacity (300 frames = 6 seconds at 50 FPS)
  - Implement writeIndex, readIndex, count tracking for ring buffer
  - Implement TryAddFrame method that adds frames without heap allocation
  - Implement TryGetFrames method that retrieves frame ranges for batch transmission
  - Implement Clear method to reset buffer state
  - Handle buffer overflow by triggering immediate batch transmission signal
  - _Requirements: 4.4, 4.5, 5.1, 5.2, 5.3, 5.4_

- [x] 3. Implement telemetry recorder with collision audio capture
  - Create `Scripts/Carrom/Telemetry/TelemetryRecorder.cs` MonoBehaviour class
  - Add TelemetryBuffer field and velocity threshold field (default 0.1f)
  - Add Dictionary to track collision velocities per piece ID for current frame
  - Implement StartRecording method to initialize recording state
  - Implement OnCollisionEnter2D hook to capture collision impacts and store relative velocity
  - Implement CaptureFrame method called every FixedUpdate to record moving piece states
  - Filter pieces by velocity magnitude > velocityThreshold before recording
  - Record Piece_ID, X_Position, Y_Position, Z_Rotation, and collisionVelocity for each moving piece
  - Clear collision velocity dictionary after each frame capture
  - Implement StopRecording method to halt recording when all pieces are stationary
  - Implement GetRecordedFrames method to retrieve frames for batch transmission
  - Implement ResetBuffer method to clear buffer after transmission
  - _Requirements: 4.1, 4.2, 13.1, 13.3, 15.1, 15.2, 15.3, 15.4_

- [x] 4. Implement batch transmitter with NGO RPCs
  - Create `Scripts/Carrom/Telemetry/BatchTransmitter.cs` NetworkBehaviour class
  - Add TelemetryRecorder reference and batch interval field (default 0.225f = 225ms)
  - Implement TransmitBatch method to send accumulated frames via ClientRpc
  - Aggregate frames spanning 200-250ms of simulation time per batch
  - Use NGO's native serialization (no custom byte arrays, no compression, no object pooling)
  - Implement ReceiveTelemetryBatchClientRpc with TelemetryBatch parameter
  - Implement TransmitEndState method to send final authoritative positions
  - Implement ReceiveEndStateClientRpc with EndStatePayload parameter
  - Use ClientRpcParams to target only the spectating player
  - Rely on NGO's Reliable Sequenced delivery (no custom retries or timeouts)
  - _Requirements: 6.1, 6.2, 6.3, 6.4, 10.3_

- [x] 5. Implement shot start RPC signaling
  - Add NotifyShotStartClientRpc method to BatchTransmitter
  - Send shot_start RPC when active player initiates shot
  - Use ClientRpcParams to target only the spectating player
  - _Requirements: 7.1_

- [x] 6. Implement jitter buffer
  - Create `Scripts/Carrom/Telemetry/JitterBuffer.cs` MonoBehaviour class
  - Use Queue<PhysicsFrame[]> for batch storage
  - Add configurable buffer duration field (default 1.0f, range 0.5-2.0 seconds, inspector-exposed)
  - Implement Initialize method to start buffer timer when shot_start RPC received
  - Implement EnqueueBatch method to store incoming batches in arrival order
  - Implement IsBufferReady method that returns true when timer expires
  - Implement GetNextFrame method to provide frames to PlaybackEngine in sequence
  - Implement Clear method to reset buffer state
  - Handle buffer overflow by discarding oldest batches (max 100 batches)
  - _Requirements: 7.3, 7.4, 8.1, 8.2, 8.3, 8.4, 17.1, 17.2, 17.3, 17.4_

- [x] 7. Implement playback engine with interpolation and audio replay
  - Create `Scripts/Carrom/Telemetry/PlaybackEngine.cs` MonoBehaviour class
  - Add JitterBuffer reference, PieceRegistry reference, and AudioSource reference
  - Add AudioClip field for Carrom collision sound
  - Track currentFrame, nextFrame, and frameProgress for interpolation
  - Implement StartPlayback method to begin rendering when jitter buffer is ready
  - Implement UpdatePlayback method called every frame to interpolate positions/rotations
  - Use Vector2.Lerp for position interpolation between consecutive frames
  - Use Mathf.LerpAngle for rotation interpolation between consecutive frames
  - Read collisionVelocity from PieceState during interpolation
  - If collisionVelocity > 0, trigger local Carrom collision audio at volume scaled by velocity
  - Directly modify Transform components, not Rigidbody2D components
  - Implement ApplyEndState method to snap all pieces to authoritative final positions
  - Calculate frameProgress based on elapsed time and frame timestamps
  - _Requirements: 9.1, 9.2, 9.3, 9.4, 9.5, 10.4, 10.5, 14.2, 14.3, 14.4_

- [x] 8. Modify NetworkPhysicsObject to disable physics simulation on spectator
  - Add SetAuthority method to switch between active (simulated) and spectating (not simulated) modes
  - Add SetPhysicsSimulation method to control Rigidbody2D.simulated property
  - Add SetVisualTransform method to update Transform without affecting Rigidbody2D
  - Modify OnNetworkSpawn to use authority-based checks instead of IsServer checks
  - Ensure Active_Player has Rigidbody2D.simulated = true during their turn
  - Ensure Spectating_Player has Rigidbody2D.simulated = false during opponent's turn
  - Decouple visual Transform updates from Rigidbody2D physics state
  - _Requirements: 1.1, 1.2, 2.1, 2.2, 2.4, 3.2, 12.3, 12.4, 14.1, 14.2_

- [x] 9. Modify CarromGameManager to integrate telemetry system
  - Add TelemetryRecorder, BatchTransmitter, JitterBuffer, PlaybackEngine component references
  - Add velocityThreshold field (default 0.1f)
  - Implement OnShotStart method to initialize telemetry recording and send shot_start RPC
  - Modify AreAllObjectsStopped to use velocityThreshold for consistency
  - Implement OnShotComplete method to detect shot completion and trigger end-state synchronization
  - Implement ConstructEndState method to build EndStatePayload with all piece positions/rotations
  - Implement TransferAuthority method to switch network ownership between players
  - Call OnShotStart when turn begins
  - Call OnShotComplete when all pieces stop moving
  - Ensure authority transfer completes before next turn begins
  - _Requirements: 1.3, 1.4, 10.1, 10.2, 11.1, 12.1, 12.2, 12.5_

- [x] 10. Modify StrikerController to trigger telemetry recording
  - Add TelemetryRecorder reference (get from CarromGameManager)
  - Call NotifyShotStartClientRpc before applying force to striker
  - Call telemetryRecorder.StartRecording() when shot is initiated
  - Call telemetryRecorder.StopRecording() when shot completes (all pieces stopped)
  - Notify CarromGameManager when shot completes for end state transmission
  - _Requirements: 7.1, 7.2, 15.1, 15.2_

- [x] 11. Implement spectating player physics disable and buffer initialization
  - In BatchTransmitter.NotifyShotStartClientRpc, freeze visual updates for game pieces
  - Initialize JitterBuffer when shot_start RPC received
  - Start 1.0-second countdown timer
  - Set all game piece Rigidbody2D.simulated = false on spectating player
  - _Requirements: 7.2, 7.3, 7.4, 2.1, 2.3_

- [x] 12. Implement end state synchronization and authority transfer
  - In BatchTransmitter.ReceiveEndStateClientRpc, complete playback animation
  - Apply EndStatePayload to snap all piece positions and rotations
  - Trigger authority transfer after end state applied
  - Set Rigidbody2D.simulated = true on new active player
  - Set Rigidbody2D.simulated = false on new spectating player
  - _Requirements: 10.4, 10.5, 11.2, 11.3, 11.4, 12.1, 12.2, 12.3, 12.4_

- [x] 13. Create Unity prefabs and configure scene
  - Create TelemetrySystem prefab with TelemetryRecorder, BatchTransmitter, JitterBuffer, PlaybackEngine components
  - Add TelemetrySystem prefab to Carrom scene
  - Configure PieceRegistry with all game pieces (striker, coins, queen)
  - Assign piece IDs in Unity Inspector
  - Wire up component references in CarromGameManager
  - Configure buffer duration in JitterBuffer Inspector (default 1.0s)
  - Configure velocity threshold in TelemetryRecorder Inspector (default 0.1f)
  - Assign Carrom collision AudioClip to PlaybackEngine
  - _Requirements: 17.1, 17.2_

- [x] 14. Final integration and gameplay validation
  - Verify all component references are properly assigned
  - Verify NetworkPhysicsObject attached to all game pieces
  - Verify BatchTransmitter NetworkBehaviour is registered with Unity Netcode
  - Test full turn cycle: shot → recording → transmission → buffering → playback → end state → authority transfer
  - Verify active player experiences zero latency
  - Verify spectating player sees smooth playback after 1-second delay
  - Verify collision audio plays correctly on spectating player during replay
  - Verify Rigidbody2D.simulated = false prevents physics calculations on spectator
  - _Requirements: 1.1, 3.1, 9.5_

## Notes

- No test scripts - validation through actual gameplay
- All structs use NGO's INetworkSerializable for automatic serialization
- No custom byte array serialization, compression, or object pooling
- No custom retry logic - rely on NGO's Reliable Sequenced channel
- Use Rigidbody2D.simulated = false instead of kinematic mode for spectator
- Collision audio captured during recording and replayed during playback for authentic game feel
- All telemetry components isolated in `Scripts/Carrom/Telemetry/` directory
