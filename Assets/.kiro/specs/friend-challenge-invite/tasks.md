# Implementation Plan: Friend Challenge / Private Lobby Invite

## Overview

Implement the private lobby invite flow by modifying four existing files and adding one new MonoBehaviour. Tasks follow the exact order specified: FriendsListItem → LobbyManager → MainMenuManager → LobbyJoinCodeDisplay → Unity Editor wiring.

## Tasks

- [x] 1. Update FriendsListItem.cs — swap removeButton for inviteButton
  - Replace `[SerializeField] private Button removeButton` with `[SerializeField] private Button inviteButton`
  - Comment out `removeButton.onClick.AddListener(RemoveFriend)` in `Start()`
  - Comment out the `RemoveFriend()` method body (keep signature commented)
  - Add `inviteButton.onClick.AddListener(InviteFriend)` in `Start()`
  - Add `async void InviteFriend()`: set `inviteButton.interactable = false`, call `LobbyManager.Instance.CreateLobby("Carrom", 2, true, GameMode.Carrom)` inside try, re-enable button and open ErrorMenu in catch
  - _Requirements: 1.1, 1.2, 1.3, 1.4, 2.1, 2.3_

  - [ ]* 1.1 Write property test for invite button disabled during creation
    - **Property 2: Invite button disabled during creation**
    - **Validates: Requirements 1.3**

  - [ ]* 1.2 Write property test for invite button re-enabled on failure
    - **Property 3: Invite button re-enabled on failure**
    - **Validates: Requirements 1.4**

  - [ ]* 1.3 Write property test for invite triggers private lobby creation
    - **Property 1: Invite triggers private lobby creation**
    - **Validates: Requirements 1.2, 2.1**

- [x] 2. Update LobbyManager.cs — wrap JoinLobbyByCode in try/catch
  - Locate the existing `JoinLobbyByCode(string lobbyCode)` method
  - Wrap the `await LobbyService.Instance.JoinLobbyByCodeAsync(...)` call in a try/catch
  - In the catch block: retrieve ErrorMenu via `PanelManager.GetSingleton("error")` and call `panel.Open(ErrorMenu.Action.None, "Failed to join lobby. Check the code and try again.", "OK")`
  - Ensure `joinedLobby` and `OnJoinedLobby` are only set/fired inside the try (success path)
  - _Requirements: 4.4_

  - [ ]* 2.1 Write unit test for JoinLobbyByCode opens ErrorMenu on failure
    - Verify `JoinLobbyByCode` opens ErrorMenu when `LobbyService` throws
    - _Requirements: 4.4_

- [ ] 3. Checkpoint — Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

- [x] 4. Update MainMenuManager.cs — add join-by-code UI wiring
  - Add `[SerializeField] private TMP_InputField joinCodeInput = null;`
  - Add `[SerializeField] private Button joinButton = null;`
  - In `Initialize()` (or equivalent setup method), add `if (joinButton != null) joinButton.onClick.AddListener(JoinByCode);`
  - Add `async void JoinByCode()`: early-return if `joinCodeInput.text.Trim()` is empty, disable both controls, call `LobbyManager.Instance.JoinLobbyByCode(code)` in try, re-enable both controls and open ErrorMenu in catch
  - Do not alter the existing public lobby play button or `QuickJoinOrCreatePublicLobby` call
  - _Requirements: 4.1, 4.2, 4.4, 4.5, 4.6, 5.1, 5.2, 5.3_

  - [ ]* 4.1 Write unit test for JoinByCode does nothing on empty input
    - Verify `JoinByCode()` does nothing when `joinCodeInput.text` is empty or whitespace
    - _Requirements: 4.2_

  - [ ]* 4.2 Write property test for join button triggers JoinLobbyByCode with trimmed input
    - **Property 6: Join button triggers JoinLobbyByCode with trimmed input**
    - **Validates: Requirements 4.2**

  - [ ]* 4.3 Write property test for join controls disabled during join operation
    - **Property 7: Join controls disabled during join operation**
    - **Validates: Requirements 4.5, 4.6**

- [x] 5. Create LobbyJoinCodeDisplay.cs
  - Create `Scripts/Menu/LobbyJoinCodeDisplay.cs` as a new MonoBehaviour
  - Add `[SerializeField] private TextMeshProUGUI codeText = null;`
  - In `Start()`: call `LobbyManager.Instance.GetJoinedLobby()`, assign `codeText.text = lobby != null ? lobby.LobbyCode : ""`
  - Add `using TMPro;` and `using UnityEngine;` imports
  - _Requirements: 3.1, 3.2, 3.3_

  - [ ]* 5.1 Write unit test for LobbyJoinCodeDisplay with active lobby
    - Verify `Start()` sets `codeText.text` to `lobby.LobbyCode` when a lobby is joined
    - _Requirements: 3.1_

  - [ ]* 5.2 Write unit test for LobbyJoinCodeDisplay with no lobby
    - Verify `Start()` sets `codeText.text` to `""` when no lobby is joined (no crash)
    - _Requirements: 3.3_

  - [ ]* 5.3 Write property test for join code displayed for any joined lobby
    - **Property 5: Join code displayed for any joined lobby**
    - **Validates: Requirements 3.1**

- [ ] 6. Checkpoint — Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

- [x] 7. Wire up Unity Editor
  - On the FriendsListItem prefab: in the Inspector, reassign the button reference from the old `removeButton` slot to the new `inviteButton` slot pointing at the Invite button GameObject
  - On the MainMenu panel GameObject: assign the `joinCodeInput` TMP_InputField reference and the `joinButton` Button reference in the MainMenuManager Inspector
  - In the CharacterSelection scene: add a new GameObject with `LobbyJoinCodeDisplay` component attached, assign the `codeText` TextMeshProUGUI reference in the Inspector
  - _Requirements: 1.1, 3.1, 4.1_

- [ ] 8. Final checkpoint — Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

## Notes

- Tasks marked with `*` are optional and can be skipped for a faster MVP
- Task 7 (Unity Editor wiring) must be done manually in the Unity Editor — it cannot be automated by a coding agent
- Property tests use FsCheck (C#/Unity) with a minimum of 100 iterations per property
- Each property test file should include the tag comment: `// Feature: friend-challenge-invite, Property {N}: {property_text}`
- The existing `HandleLobbyPolling` loop and `OnLobbyStartGame` event require no changes — the client transition to CharacterSelection is already handled by that path
