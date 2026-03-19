# Requirements Document

## Introduction

This feature adds a friend challenge / private lobby system to the Unity multiplayer Carrom game.
A player can challenge a friend directly from the Friends panel, which creates a private UGS Lobby
and navigates the inviting player to the CharacterSelection scene. The lobby join code is displayed
there so the inviting player can share it with their friend verbally or via any external channel.
The invited friend enters the join code on the main menu to join the private lobby and proceed into
the same CharacterSelection flow. No Cloud Save, push notifications, or invite TTL logic is involved.

## Glossary

- **Invite_Button**: The UI button on each FriendsListItem that replaces the existing Remove button
  and triggers the private lobby creation flow.
- **Private_Lobby**: A UGS Lobby created with `isPrivate = true` and `maxPlayers = 2`.
- **Lobby_Join_Code**: The short alphanumeric code exposed by `joinedLobby.LobbyCode` that allows
  a second player to join the Private_Lobby via `LobbyManager.JoinLobbyByCode`.
- **Join_Code_Display**: A UI text element in the CharacterSelection scene that shows the
  Lobby_Join_Code to the hosting player.
- **Join_Code_Input**: A TMP_InputField on the main menu scene where a player types a
  Lobby_Join_Code to join a Private_Lobby.
- **LobbyManager**: The existing singleton (`LobbyManager.Instance`) that manages UGS Lobby and
  Relay lifecycle.
- **FriendsListItem**: The existing prefab/script that renders one friend row in the Friends panel.
- **MainMenuManager**: The existing singleton that owns top-level menu navigation and hosts the
  public lobby play button.
- **CharacterSelection**: The existing Unity scene used for the pre-game waiting/character-pick
  phase, shared by both public and private lobby flows.

---

## Requirements

### Requirement 1: Invite Button on Friends List

**User Story:** As a player, I want an Invite button next to each friend's name in the Friends
panel, so that I can quickly start a private match with a specific friend.

#### Acceptance Criteria

1. THE FriendsListItem SHALL display an "Invite" button in place of the existing Remove button for
   each entry in the friends list.
2. WHEN the Invite button is pressed, THE FriendsListItem SHALL invoke the private lobby creation
   flow with the selected friend's identifier.
3. WHILE a lobby creation operation is in progress, THE FriendsListItem SHALL disable the Invite
   button to prevent duplicate requests.
4. WHEN the lobby creation operation completes (success or failure), THE FriendsListItem SHALL
   re-enable the Invite button.

---

### Requirement 2: Private Lobby Creation and Navigation

**User Story:** As a player, I want the game to create a private lobby when I invite a friend and
take me to the CharacterSelection scene, so that I can wait for my friend to join.

#### Acceptance Criteria

1. WHEN the Invite button is pressed, THE LobbyManager SHALL call `CreateLobby` with `isPrivate =
   true` and `maxPlayers = 2`.
2. WHEN the Private_Lobby is created successfully, THE LobbyManager SHALL transition the inviting
   player to the CharacterSelection scene using the existing `OnLobbyStartGame` event and
   `LoadingSceneManager`.
3. IF the Private_Lobby creation fails, THEN THE LobbyManager SHALL display an error message to the
   player via the existing ErrorMenu panel and SHALL NOT navigate away from the current scene.

---

### Requirement 3: Join Code Display in CharacterSelection

**User Story:** As a player who created a private lobby, I want to see the lobby join code in the
CharacterSelection scene, so that I can share it with my friend.

#### Acceptance Criteria

1. WHEN the CharacterSelection scene loads for a Private_Lobby host, THE Join_Code_Display SHALL
   show the Lobby_Join_Code from `joinedLobby.LobbyCode`.
2. THE Join_Code_Display SHALL remain visible while the host is waiting for the second player to
   join.
3. WHILE the CharacterSelection scene is active for a public lobby, THE Join_Code_Display SHALL NOT
   be visible.

---

### Requirement 4: Join-by-Code UI on Main Menu

**User Story:** As a player who received a join code, I want to enter it on the main menu and join
my friend's private lobby, so that we can play together.

#### Acceptance Criteria

1. THE MainMenuManager SHALL display a Join_Code_Input field and a "Join" button alongside the
   existing public lobby play button.
2. WHEN the player enters a Lobby_Join_Code and presses the Join button, THE LobbyManager SHALL call
   `JoinLobbyByCode` with the provided code.
3. WHEN `JoinLobbyByCode` succeeds, THE LobbyManager SHALL transition the joining player to the
   CharacterSelection scene using the existing relay-polling and `JoinGame` flow.
4. IF `JoinLobbyByCode` fails (invalid code, lobby full, or lobby not found), THEN THE LobbyManager
   SHALL display an error message via the ErrorMenu panel and SHALL return the player to the main
   menu.
5. WHILE a join operation is in progress, THE MainMenuManager SHALL disable the Join button and the
   Join_Code_Input to prevent duplicate attempts.
6. WHEN the join operation completes (success or failure), THE MainMenuManager SHALL re-enable the
   Join button and the Join_Code_Input.

---

### Requirement 5: Public Lobby Flow Preservation

**User Story:** As a player, I want the existing public matchmaking flow to continue working
unchanged, so that I can still find random opponents.

#### Acceptance Criteria

1. THE LobbyManager SHALL continue to expose `QuickJoinOrCreatePublicLobby` and its behaviour SHALL
   be unaffected by the addition of the private lobby flow.
2. WHEN the player presses the existing public lobby play button, THE MainMenuManager SHALL call
   `QuickJoinOrCreatePublicLobby` exactly as before.
3. THE addition of the Join_Code_Input and Join button SHALL NOT alter the layout or behaviour of
   the existing public lobby play button.
