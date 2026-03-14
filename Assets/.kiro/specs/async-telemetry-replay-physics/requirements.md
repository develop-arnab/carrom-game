# Requirements Document

## Introduction

This document specifies requirements for an asynchronous telemetry replay architecture that eliminates multiplayer physics synchronization lag in a Unity-based Carrom game. The system replaces real-time continuous physics synchronization with a recording-and-replay model where the active player experiences zero-latency local physics while the spectating player views a smooth, delayed playback reconstructed from transmitted telemetry data.

## Glossary

- **Active_Player**: The client device that currently has network authority and executes local physics simulation for the current turn
- **Spectating_Player**: The client device that does not have network authority and displays a replayed animation of the opponent's turn
- **Telemetry_Recorder**: The component on the Active_Player that captures physics state data during simulation
- **Telemetry_Buffer**: The pre-allocated data structure that stores recorded physics frames without heap allocations
- **Batch_Transmitter**: The component that aggregates recorded frames and sends them via network in chunks
- **Jitter_Buffer**: The queue on the Spectating_Player that stores incoming telemetry batches before playback begins
- **Playback_Engine**: The component that interpolates and renders piece movements on the Spectating_Player
- **Game_Piece**: Any movable object on the Carrom board (striker, carrom men, queen)
- **Physics_Frame**: A single FixedUpdate step containing state data for all moving Game_Pieces
- **End_State_Payload**: The final authoritative message containing resting positions of all Game_Pieces after a shot concludes
- **Authority_Transfer**: The process of switching network ownership from one player to the other between turns
- **Velocity_Threshold**: The minimum velocity magnitude below which a Game_Piece is considered stationary

## Requirements

### Requirement 1: Network Authority Management

**User Story:** As a game developer, I want strict network authority control, so that only one device calculates physics per turn and prevents split-reality board states.

#### Acceptance Criteria

1. WHEN a turn begins, THE Active_Player SHALL have exclusive network authority for all Game_Piece objects
2. WHILE the Active_Player has authority, THE Spectating_Player SHALL NOT execute physics calculations for any Game_Piece
3. WHEN a turn concludes, THE Authority_Transfer SHALL transfer network ownership to the other player
4. THE Active_Player SHALL maintain authority until the End_State_Payload is transmitted and acknowledged

### Requirement 2: Physics Isolation on Spectating Client

**User Story:** As a spectating player, I want my device to avoid physics calculations during opponent turns, so that I receive a consistent board state without local computation conflicts.

#### Acceptance Criteria

1. WHEN the Spectating_Player loses network authority, THE Spectating_Player SHALL set all Game_Piece Rigidbody2D components to kinematic mode
2. WHILE in kinematic mode, THE Game_Piece Rigidbody2D components SHALL NOT respond to physics forces or collisions
3. THE Spectating_Player SHALL disable physics simulation for Game_Pieces during opponent turns
4. WHEN authority is transferred back, THE Spectating_Player SHALL restore Rigidbody2D components to dynamic mode

### Requirement 3: Local Physics Execution for Active Player

**User Story:** As the active player, I want zero-latency physics response, so that my shots feel instantaneous and responsive.

#### Acceptance Criteria

1. WHEN the Active_Player executes a shot, THE Active_Player SHALL run Box2D physics simulation locally without network delay
2. THE Active_Player SHALL maintain all Game_Piece Rigidbody2D components in dynamic mode during their turn
3. THE Active_Player SHALL calculate collisions, velocities, and positions using the local physics engine
4. THE Active_Player SHALL experience physics updates at the native FixedUpdate rate without network synchronization delays

### Requirement 4: Frame-Perfect Telemetry Recording

**User Story:** As a game developer, I want precise physics state capture, so that the spectating player can reconstruct the shot accurately.

#### Acceptance Criteria

1. WHEN a shot begins, THE Telemetry_Recorder SHALL initialize recording on the Active_Player
2. DURING each FixedUpdate step, THE Telemetry_Recorder SHALL capture state data for all Game_Pieces with velocity magnitude exceeding the Velocity_Threshold
3. FOR each moving Game_Piece, THE Telemetry_Recorder SHALL record Piece_ID, X_Position, Y_Position, and Z_Rotation
4. THE Telemetry_Recorder SHALL store recorded frames in the Telemetry_Buffer using pre-allocated memory structures
5. THE Telemetry_Recorder SHALL NOT allocate heap memory during the recording loop to prevent garbage collection spikes

### Requirement 5: Allocation-Free Telemetry Buffer

**User Story:** As a game developer, I want memory-efficient telemetry storage, so that recording does not cause performance degradation or garbage collection pauses.

#### Acceptance Criteria

1. THE Telemetry_Buffer SHALL use pre-allocated arrays or ring buffers for frame storage
2. THE Telemetry_Buffer SHALL use struct data types for frame records to avoid heap allocations
3. WHEN the buffer reaches capacity, THE Telemetry_Buffer SHALL either expand capacity or trigger immediate batch transmission
4. THE Telemetry_Buffer SHALL support retrieval of recorded frames for batch transmission without copying data

### Requirement 6: Micro-Batched Network Transmission

**User Story:** As a game developer, I want efficient network usage, so that telemetry data is transmitted without flooding the network channel.

#### Acceptance Criteria

1. THE Batch_Transmitter SHALL aggregate recorded Physics_Frames into batches representing 200ms to 250ms of simulation time
2. WHEN a batch is complete, THE Batch_Transmitter SHALL transmit the batch via Unity Relay using a Reliable Sequenced channel
3. THE Batch_Transmitter SHALL NOT send individual Physics_Frames as separate network messages
4. WHILE recording continues, THE Batch_Transmitter SHALL continue batching and transmitting at the specified interval

### Requirement 7: Shot Initiation Signal

**User Story:** As a spectating player, I want to know when my opponent's shot begins, so that I can prepare to receive and buffer telemetry data.

#### Acceptance Criteria

1. WHEN the Active_Player initiates a shot, THE Active_Player SHALL send a shot_start RPC to the Spectating_Player
2. WHEN the Spectating_Player receives the shot_start RPC, THE Spectating_Player SHALL freeze visual updates for Game_Pieces
3. WHEN the Spectating_Player receives the shot_start RPC, THE Spectating_Player SHALL initialize the Jitter_Buffer
4. WHEN the Spectating_Player receives the shot_start RPC, THE Spectating_Player SHALL start a 1.0-second countdown timer

### Requirement 8: Jitter Buffer Management

**User Story:** As a spectating player, I want incoming telemetry data buffered, so that playback can be smooth regardless of network timing variations.

#### Acceptance Criteria

1. WHEN the Spectating_Player receives a telemetry batch, THE Jitter_Buffer SHALL enqueue the batch in arrival order
2. THE Jitter_Buffer SHALL maintain received batches until the 1.0-second timer expires
3. WHILE the timer is active, THE Jitter_Buffer SHALL continue accepting and storing incoming batches
4. WHEN the timer expires, THE Jitter_Buffer SHALL signal the Playback_Engine to begin rendering

### Requirement 9: Smooth Interpolated Playback

**User Story:** As a spectating player, I want to see a smooth animation of my opponent's shot, so that the game feels polished and professional.

#### Acceptance Criteria

1. WHEN the 1.0-second buffer period completes, THE Playback_Engine SHALL begin reading frames from the Jitter_Buffer
2. FOR each frame interval, THE Playback_Engine SHALL interpolate Game_Piece positions using Vector2.Lerp
3. FOR each frame interval, THE Playback_Engine SHALL interpolate Game_Piece rotations using Mathf.LerpAngle
4. THE Playback_Engine SHALL render interpolated positions and rotations at the Spectating_Player device framerate
5. THE Playback_Engine SHALL maintain smooth visual motion independent of the original physics simulation framerate

### Requirement 10: End-State Synchronization

**User Story:** As a game developer, I want guaranteed final state accuracy, so that both players see identical board positions after a shot concludes.

#### Acceptance Criteria

1. WHEN all Game_Pieces have velocity magnitude below the Velocity_Threshold, THE Active_Player SHALL detect shot completion
2. WHEN shot completion is detected, THE Active_Player SHALL construct an End_State_Payload containing final positions and rotations for all Game_Pieces
3. WHEN the End_State_Payload is ready, THE Active_Player SHALL transmit it via a Reliable channel
4. WHEN the Spectating_Player receives the End_State_Payload, THE Playback_Engine SHALL complete the current animation sequence
5. WHEN animation completes, THE Spectating_Player SHALL snap all Game_Piece positions and rotations to the values in the End_State_Payload

### Requirement 11: Authoritative State Enforcement

**User Story:** As a game developer, I want the final board state to match the Active_Player's physics result exactly, so that gameplay remains fair and consistent.

#### Acceptance Criteria

1. THE End_State_Payload SHALL contain the authoritative final position and rotation for every Game_Piece
2. WHEN the Spectating_Player applies the End_State_Payload, THE Spectating_Player SHALL override any interpolation discrepancies
3. THE Spectating_Player SHALL apply End_State_Payload values without visual interpolation to ensure exact alignment
4. AFTER applying the End_State_Payload, both clients SHALL have identical Game_Piece transforms within floating-point precision limits

### Requirement 12: Turn Transition and Authority Handoff

**User Story:** As a player, I want seamless turn transitions, so that gameplay continues smoothly after each shot.

#### Acceptance Criteria

1. WHEN the End_State_Payload is applied on the Spectating_Player, THE Authority_Transfer SHALL begin
2. THE Authority_Transfer SHALL transfer network ownership of all Game_Pieces to the previously Spectating_Player
3. WHEN Authority_Transfer completes, THE new Active_Player SHALL enable dynamic Rigidbody2D mode for all Game_Pieces
4. WHEN Authority_Transfer completes, THE new Spectating_Player SHALL enable kinematic Rigidbody2D mode for all Game_Pieces
5. THE Authority_Transfer SHALL complete before the next turn begins

### Requirement 13: Telemetry Data Optimization

**User Story:** As a game developer, I want minimal network bandwidth usage, so that the game performs well on limited connections.

#### Acceptance Criteria

1. THE Telemetry_Recorder SHALL record state data only for Game_Pieces with velocity magnitude exceeding the Velocity_Threshold
2. THE Telemetry_Recorder SHALL use compact data types for transmission (byte for Piece_ID, float for positions and rotation)
3. THE Telemetry_Recorder SHALL NOT record state for stationary Game_Pieces
4. THE Batch_Transmitter SHALL compress batches if compression reduces payload size by more than 20 percent

### Requirement 14: Visual Decoupling on Spectator

**User Story:** As a game developer, I want visual rendering separated from physics state, so that the spectating player can display smooth animations independent of physics timing.

#### Acceptance Criteria

1. THE Spectating_Player SHALL update Game_Piece visual transforms independently of Rigidbody2D physics state
2. THE Playback_Engine SHALL modify Transform components directly without affecting Rigidbody2D components
3. WHILE in kinematic mode, THE Game_Piece visual positions SHALL be controlled exclusively by the Playback_Engine
4. THE Playback_Engine SHALL render at the display framerate regardless of the physics simulation rate

### Requirement 15: Telemetry Recording Lifecycle

**User Story:** As a game developer, I want controlled recording start and stop, so that telemetry capture aligns precisely with shot execution.

#### Acceptance Criteria

1. WHEN the Active_Player initiates a shot, THE Telemetry_Recorder SHALL begin recording immediately
2. WHEN all Game_Pieces reach velocity magnitude below the Velocity_Threshold, THE Telemetry_Recorder SHALL stop recording
3. WHEN recording stops, THE Telemetry_Recorder SHALL flush any remaining frames to the Batch_Transmitter
4. THE Telemetry_Recorder SHALL reset the Telemetry_Buffer for the next turn after transmission completes

### Requirement 16: Parser for Telemetry Batches

**User Story:** As a game developer, I want reliable telemetry deserialization, so that the spectating player correctly interprets received data.

#### Acceptance Criteria

1. WHEN the Spectating_Player receives a telemetry batch, THE Telemetry_Parser SHALL deserialize the batch into Physics_Frame structures
2. WHEN a batch contains invalid data, THE Telemetry_Parser SHALL return a descriptive error and discard the batch
3. THE Telemetry_Serializer SHALL format Physics_Frame structures into network-transmittable byte arrays
4. FOR ALL valid Physics_Frame collections, serializing then deserializing SHALL produce equivalent frame data (round-trip property)

### Requirement 17: Configurable Buffer Duration

**User Story:** As a game developer, I want adjustable buffer timing, so that I can tune the system for different network conditions.

#### Acceptance Criteria

1. THE Jitter_Buffer SHALL support configuration of the buffer duration via an inspector-exposed parameter
2. THE default buffer duration SHALL be 1.0 seconds
3. WHEN the buffer duration is modified, THE Spectating_Player SHALL use the new duration for subsequent shots
4. THE buffer duration SHALL accept values between 0.5 seconds and 2.0 seconds

### Requirement 18: Graceful Network Interruption Handling

**User Story:** As a player, I want the game to handle network issues gracefully, so that temporary disconnections don't break gameplay.

#### Acceptance Criteria

1. IF a telemetry batch fails to arrive within 500ms of the expected time, THEN THE Jitter_Buffer SHALL log a warning
2. IF the End_State_Payload fails to arrive within 5 seconds of shot initiation, THEN THE Spectating_Player SHALL request retransmission
3. IF retransmission fails, THEN THE Spectating_Player SHALL display a connection error message
4. WHEN network connectivity is restored, THE Authority_Transfer SHALL resume from the last confirmed state

## Notes

This architecture prioritizes Active_Player experience (0ms latency) while ensuring Spectating_Player receives a high-quality, smooth replay. The 1-second buffer provides sufficient time to absorb network jitter and construct seamless interpolated playback. All requirements are designed to integrate with existing Unity Relay infrastructure and Carrom game logic.
