using UnityEngine;

public class PlayerSpawnManager : MonoBehaviour
{
    public Transform player;

    // Awake is called when the script instance is being loaded
    void Awake()
    {
        if (player == null)
        {
            player = GameObject.FindWithTag("Player")?.transform; // Find the player object by tag if not assigned
        }
    }

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        if (player == null)
        {
            return; // Exit if player is not found
        }

        if (PlayerData.IsInGalaxyMap)
        {
            if (PlayerData.hasSavedPositionSpace)
            {
                // If the player has a saved position in space, teleport them to that position
                Teleport(PlayerData.savedPositionSpace, PlayerData.savedRotationSpace);
                PlayerData.hasSavedPositionSpace = false; // Reset the flag after teleporting
            }
        }
        else
        {
            if (PlayerData.hasSavedPositionShip)
            {
                // If the player has a saved position in the spaceship, teleport them to that position
                Teleport(PlayerData.savedPositionShip, PlayerData.savedRotationShip);
                PlayerData.hasSavedPositionShip = false; // Reset the flag after teleporting
            }
        }
    }

    void Teleport(Vector3 pos, Quaternion rot)
    {
        CharacterController controller = player.GetComponent<CharacterController>();
        if (controller != null)
        {
            controller.enabled = false; // Disable the CharacterController to avoid issues during teleportation
        }

        Rigidbody rb = player.GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.linearVelocity = Vector3.zero; // Reset the player's velocity to avoid unwanted movement after teleportation
            rb.angularVelocity = Vector3.zero; // Reset angular velocity to prevent spinning

            // Move the physics body to the new position
            rb.position = pos; // Set the position of the Rigidbody directly
            rb.rotation = rot; // Set the rotation of the Rigidbody directly
        }

        player.SetPositionAndRotation(pos, rot); // Teleport the player to the specified position and rotation

        if (controller != null)
        {
            controller.enabled = true; // Re-enable the CharacterController after teleportation
        }
    }
}
