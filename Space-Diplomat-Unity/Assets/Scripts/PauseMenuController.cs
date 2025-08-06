using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.Audio;

public class PauseMenuController : MonoBehaviour
{
    [Header("Enable in this scene")]
    [SerializeField] private bool enableInThisScenes = true;

    [Header("UI")]
    [SerializeField] private GameObject rootCanvas; // Reference to the root canvas of the pause menu
    [SerializeField] private Slider volumeSlider; // Slider for adjusting the volume
    [SerializeField] private Button resumeButton; // Button to resume the game
    [SerializeField] private Button quitButton; // Button to quit the game

    [Header("Audio")]
    [SerializeField] private AudioMixer audioMixer; // Reference to the audio mixer for volume control

    private bool isOpen;
    private GameInputContext previousContext; // Store the previous input context before opening the pause menu
    private bool _wired; // Singleton instance of PauseMenuController

    // Awake is called when the script instance is being loaded
    void Awake()
    {
        // If disabled for this scene, just bail out early
        if (!enableInThisScenes)
        {
            enabled = false;
            return;
        }

        // Rqeuires a canvas to function
        if (!rootCanvas)
        {
            enabled = false;
            return;
        }

        rootCanvas.SetActive(false); // Initially hide the pause menu

        // Hook UI buttons to their respective actions
        if (resumeButton)
        {
            resumeButton.onClick.AddListener(Close);
        }

        if (quitButton)
        {
            quitButton.onClick.AddListener(QuitGame);
        }

        // Hook the volume slider to the audio mixer
        if (volumeSlider)
        {
            volumeSlider.onValueChanged.AddListener(SetVolumeFromSlider);
        }

        // initialize slider from current value
        InitVolumeSlider();

        if (InputService.Instance != null)
        {
            // Subscribe to inputs
            InputService.Instance.Pause += OnPauseToggle;
            InputService.Instance.menuReturn += OnReturnToGame;
            _wired = true;
        }
    }

    // OnDestroy is called when the script instance is being destroyed
    void OnDestroy()
    {
        // Unsubscribe from inputs
        if (_wired && InputService.Instance != null)
        {
            InputService.Instance.Pause -= OnPauseToggle;
            InputService.Instance.menuReturn -= OnReturnToGame;
        }

    }

    // ---------------- Open/Close -----------------
    public void OnPauseToggle()
    {
        if (!enabled) return;

        if (isOpen)
        {
            Close();
        }
        else
        {
            Open();
        }
    }

    private void Open()
    {
        if (isOpen) return;
        isOpen = true;

        if (InputService.Instance != null)
        {
            previousContext = InputService.Instance.CurrentContext; // Store the previous context
            InputService.Instance.SwitchContext(GameInputContext.PauseMenu); // Switch to pause menu context
        }

        Time.timeScale = 0f; // Pause the game time

        rootCanvas.SetActive(true); // Show the pause menu

        // Device-aware cursor/selection
        bool gamepadUsed = (InputService.Instance != null &&
            InputService.Instance.LastUsedDevice == LastDeviceKind.Gamepad);

        Cursor.visible = !gamepadUsed; // Hide cursor for gamepad users
        Cursor.lockState = gamepadUsed ? CursorLockMode.Locked : CursorLockMode.None; // Lock cursor for gamepad users

        // Focus "Resume" so D-pad/left stick works immediately
        if (resumeButton)
        {
            EventSystem.current?.SetSelectedGameObject(resumeButton.gameObject);
        }
    }

    private void Close()
    {
        if (!isOpen) return;
        isOpen = false;

        rootCanvas.SetActive(false); // Hide the pause menu
        Time.timeScale = 1f; // Resume game time

        if (InputService.Instance != null)
        {
            // Restore previous gameplay context
            InputService.Instance.SwitchContext(previousContext); // Switch back to the previous context
        }

        // Restore cursor state
        Cursor.visible = false; // Hide cursor when closing pause menu
        Cursor.lockState = CursorLockMode.Locked; // Lock cursor when closing pause menu
    }

    private void OnReturnToGame()
    {
        if (isOpen)
        {
            Close(); // Close the pause menu if it's open
        }
    }

    // ---------------- Volume -----------------
    private void InitVolumeSlider()
    {
        if (!volumeSlider) return;

        var am = AudioManager.audioInstance;
        if (am == null) return;

        volumeSlider.SetValueWithoutNotify(am.GetNormalizedMusicVolume()); // Initialize the slider with the current volume
    }

    private void SetVolumeFromSlider(float t)
    {
        var am = AudioManager.audioInstance;
        if (am != null) am.SetNormalizedMusicVolume(t); // Set the volume in the audio manager based on the slider value
    }

    // ---------------- Quit Game -----------------
    private void QuitGame()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false; // Stop playing in the editor
#else
        Application.Quit(); // Quit the application
#endif
    }
}
