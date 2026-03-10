using System;

/// <summary>
/// Small static container to hold local pre-network selection between scenes.
/// Set SelectedCharacter = -1 to mean 'no selection'.
/// </summary>
public static class NonNetworkSelectionData
{
    // -1 => none selected
    public static int SelectedCharacter = -1;

    public static void Reset()
    {
        SelectedCharacter = -1;
    }
}