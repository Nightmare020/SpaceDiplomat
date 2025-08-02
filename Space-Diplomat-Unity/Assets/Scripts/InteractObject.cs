using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

public class InteractObject : MonoBehaviour
{
    [Header("UI / Scene")]
    [SerializeField] private TextMeshProUGUI messageText; // Reference to the UI element that displays interaction messages
    [SerializeField] private string sceneToLoad = ""; // Name of the scene to load when the player interacts with the object

    private bool playerInRange; // Flag to check if the player is in range to interact

    // ----------------------- Public API called from PlayerController -----------------------
    public void PerformInteraction()
    {
        if (!playerInRange) return; // If the player is not in range, do nothing

        // Save position/rotation of the player
        var player = GameObject.FindWithTag("Player").transform;
        PlayerData.savedPositionShip = player.position; // Save the player's position
        PlayerData.savedRotationShip = player.rotation; // Save the player's rotation
        PlayerData.hasSavedPositionShip = true; // Set the flag indicating the position has been saved

        // Load the specified scene
        SceneChanger.instance.ChangeScene(sceneToLoad);
    }

    // ----------------------- Trigger bookkeeping -----------------------
    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Player")) return; // If the collider is not the player, do nothing

        // Set the playerInRange flag to true
        playerInRange = true;

        // Show the interaction message
        messageText?.gameObject.SetActive(true);

        // Set this interactable as the current one in the player controller
        other.GetComponent<PlayerController>()?.SetCurrentInteractable(this);
    }

    private void OnTriggerExit(Collider other)
    {
        if (!other.CompareTag("Player")) return; // If the collider is not the player, do nothing

        // Set the playerInRange flag to false
        playerInRange = false;

        // Hide the interaction message
        messageText?.gameObject.SetActive(false);

        other.GetComponent<PlayerController>()?.ClearInteractable(this); // Clear the interactable reference in the player controller
    }
}
