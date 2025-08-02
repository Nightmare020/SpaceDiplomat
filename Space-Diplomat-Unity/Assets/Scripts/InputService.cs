using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public enum GameInputContext
{
    Astronaut,
    PauseMenu,
    Spaceship
}

public enum LastDeviceKind { Unknown, KeyboardMouse, Gamepad }

public class InputService : MonoBehaviour
{
    private static InputService _instance; // Singleton instance of InputService
    private static bool _applicationIsQuitting = false; // Flag to check if the application is quitting

    public static InputService Instance
    {
        get 
        {
            // If the application is quitting, return null to avoid creating a new instance
            if (_applicationIsQuitting)
            {
                return null;
            }

            // If an instance does not exist, create a new one
            if (_instance == null)
            {
                _instance = FindFirstObjectByType<InputService>();
            }

            // Create a new one if it still doesn't exist
            if (_instance == null)
            {
                GameObject gameObject = new GameObject("InputService");
                _instance = gameObject.AddComponent<InputService>();
                DontDestroyOnLoad(gameObject); // Ensure the InputService persists across scenes
            }

            // Return the existing or newly created instance
            return _instance;
        }
    }

    private SpaceshipInputs shipControls; // Input actions for spaceship controls
    private AstronautInputs astroControls; // Input actions for astronaut controls
    private PauseMenuInputs pauseControls; // Input actions for pause menu controls

    // ------------- Shared events -------------
    public event Action Interact; // Action to be invoked when the interact action is triggered
    public event Action Pause; // Action to be invoked when the pause action is triggered

    // ------------- Spaceship -------------
    public Vector2 shipMove { get; private set; } // X = turn, Y = thrust/brake
    public float shipTurn => Mathf.Clamp(shipMove.x, -1f, 1f); // Turn value for the ship, clamped between -1 and 1
    public float shipThrust => Mathf.Clamp01(Mathf.Max(0f, shipMove.y)); // Thrust value for the ship, clamped between 0 and 1
    public float shipBrake => Mathf.Clamp01(Mathf.Max(0f, -shipMove.y)); // Brake value for the ship, clamped between 0 and 1
    public event Action shipReturnToShip; // Action to be invoked when the player returns to the ship


    // ------------- Player Astronaut -------------
    public Vector2 astroMove { get; private set; } // X = horizontal movement, Y = vertical movement
    public Vector2 astroLook { get; private set; } // X = horizontal look, Y = vertical look
    public event Action astroJump; // Jump value for the astronaut

    // ------------- Pause Menu -------------
    public Vector2 menuMove { get; private set; } // X = horizontal movement, Y = vertical movement
    public event Action menuSubmit; // Action to be invoked when the menu submit action is triggered
    public event Action menuReturn; // Action to be invoked when the menu back action is triggered

    public GameInputContext CurrentContext { get; private set; } = GameInputContext.Astronaut; // Current input context
    public LastDeviceKind LastUsedDevice { get; private set; } = LastDeviceKind.Unknown; // Last used input device kind

    // Awake is called when the script instance is being loaded
    private void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject); // Ensure only one instance of InputService exists
            return;
        }
        _instance = this;
        DontDestroyOnLoad(gameObject); // Make sure InputService persists across scenes

        // Initialize input actions
        shipControls = new SpaceshipInputs();
        astroControls = new AstronautInputs();
        pauseControls = new PauseMenuInputs();

        // ------------- Wire actions -------------
        // Spaceship controls
        shipControls.Move.Newaction.performed += ctx => { SetLastDevice(ctx.control.device); shipMove = ctx.ReadValue<Vector2>(); };
        shipControls.Move.Newaction.canceled += ctx => shipMove = Vector2.zero; // Reset shipMove when the action is canceled
        shipControls.Interact.Newaction.performed += ctx => { SetLastDevice(ctx.control.device); Interact?.Invoke(); };
        shipControls.Pause.Newaction.performed += ctx => { SetLastDevice(ctx.control.device); Pause?.Invoke(); };
        shipControls.BackToShip.Newaction.performed += ctx => { SetLastDevice(ctx.control.device); shipReturnToShip?.Invoke(); };

        // Astronaut controls
        astroControls.Move.Newaction.performed += ctx => { SetLastDevice(ctx.control.device); astroMove = ctx.ReadValue<Vector2>(); };
        astroControls.Move.Newaction.canceled += ctx => astroMove = Vector2.zero; // Reset astroMove when the action is canceled
        astroControls.Look.Newaction.performed += ctx => { SetLastDevice(ctx.control.device); astroLook = ctx.ReadValue<Vector2>(); };
        astroControls.Look.Newaction.canceled += ctx => astroLook = Vector2.zero; // Reset astroLook when the action is canceled
        astroControls.Jump.Newaction.performed += ctx => { SetLastDevice(ctx.control.device); astroJump?.Invoke(); }; // Set jump value to 1 when the jump action is performed
        astroControls.Interact.Newaction.performed += ctx => { SetLastDevice(ctx.control.device); Interact?.Invoke(); };
        astroControls.Pause.Newaction.performed += ctx => { SetLastDevice(ctx.control.device); Pause?.Invoke(); };

        // Pause menu controls
        pauseControls.Move.Newaction.performed += ctx => menuMove = ctx.ReadValue<Vector2>();
        pauseControls.Move.Newaction.canceled += _ => menuMove = Vector2.zero; // Reset menuMove when the action is canceled
        pauseControls.Submit.Newaction.performed += _ => menuSubmit?.Invoke();
        pauseControls.Cancel.Newaction.performed += _ => menuReturn?.Invoke();

        // Enable the input actions
        shipControls.Enable();
        astroControls.Enable();
        pauseControls.Enable();

        // Set the initial input context to Astronaut
        SwitchContext(GameInputContext.Astronaut);
    }

    private void OnDestroy()
    {
        // Disable input actions when the InputService is destroyed
        shipControls?.Disable();
        astroControls?.Disable();
        pauseControls?.Disable();

        // Unsubscribe from all events to prevent memory leaks
        shipControls?.Dispose();
        astroControls?.Dispose();
        pauseControls?.Dispose();

        if (_instance == this)
        {
            _instance = null; // Clear the instance if this is the one being destroyed
        }
    }

    // ---------------- Context Switching ----------------
    public void SwitchContext(GameInputContext ctx)
    {
        CurrentContext = ctx;

        // Disable all input maps first
        DisableShipMaps();
        DisableAstronautMaps();
        DisablePauseMaps();

        // Clear cached state so old input doesn't leak into new mode
        shipMove = Vector2.zero;
        astroMove = Vector2.zero;
        astroLook = Vector2.zero;
        menuMove = Vector2.zero;

        // Enable the appropriate input map based on the context
        switch (ctx)
        {
            case GameInputContext.Astronaut:
                astroControls.Move.Enable();
                astroControls.Look.Enable();
                astroControls.Jump.Enable();
                astroControls.Interact.Enable();
                astroControls.Pause.Enable();
                break;
            case GameInputContext.PauseMenu:
                pauseControls.Move.Enable();
                pauseControls.Submit.Enable();
                pauseControls.Cancel.Enable();
                break;
            case GameInputContext.Spaceship:
                shipControls.Move.Enable();
                shipControls.Interact.Enable();
                shipControls.Pause.Enable();
                shipControls.BackToShip.Enable();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(ctx), ctx, null);
        }
    }


    private void DisableShipMaps()
    {
        shipControls.Move.Disable();
        shipControls.Interact.Disable();
        shipControls.Pause.Disable();
        shipControls.BackToShip.Disable();
    }

    private void DisableAstronautMaps()
    {
        astroControls.Move.Disable();
        astroControls.Look.Disable();
        astroControls.Jump.Disable();
        astroControls.Interact.Disable();
        astroControls.Pause.Disable();
    }

    private void DisablePauseMaps()
    {
        pauseControls.Move.Disable();
        pauseControls.Submit.Disable();
        pauseControls.Cancel.Disable();
    }

    private void OnApplicationQuit()
    {
        _applicationIsQuitting = true; // Set the flag to true when the application is quitting
    }

    private void SetLastDevice (InputDevice device)
    {
        if (device == null) return;

        if (device is Gamepad)
        {
            LastUsedDevice = LastDeviceKind.Gamepad;
        }
        else if (device is Keyboard || device is Mouse)
        {
            LastUsedDevice = LastDeviceKind.KeyboardMouse;
        }
        else
        {
            LastUsedDevice = LastDeviceKind.Unknown;
        }
    }
}
