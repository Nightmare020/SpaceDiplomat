using UnityEngine;

public static class PlayerData
{
    /** INSIDE SPACESHIP **/
    public static Vector3 savedPositionShip = Vector3.zero; // Position to save the player's position
    public static Quaternion savedRotationShip = Quaternion.identity; // Rotation to save the player's rotation
    public static bool hasSavedPositionShip = false; // Flag to check if the player has a saved position

    /** IN SPACE **/
    public static Vector3 savedPositionSpace = Vector3.zero; // Position to save the player's position
    public static Quaternion savedRotationSpace = Quaternion.identity; // Rotation to save the player's rotation
    public static bool hasSavedPositionSpace = false; // Flag to check if the player has a saved position

    public static bool IsInGalaxyMap = false; // Flag to check if the player is in the space exploration map
    public static string SelectedAlienName = ""; // Name of the selected alien
}
