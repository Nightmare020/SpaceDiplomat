using System.Diagnostics;
using System.IO;
using UnityEngine;

public class PythonServerLauncher : MonoBehaviour
{
    private Process serverProcess;

    // Path to built Python server script for the chat connection with Groq
    public string pythonServerPath = "chat_server.exe";

    private const string GROQ_API_KEY = "";
    private const string MAX_TOKENS = "200";
    private const string TEMPERATURE = "0.7";

    // Awake is called when the script instance is being loaded
    void Awake()
    {
        LaunchPythonServer();
    }

    // Method to stop the Python server process
    void OnApplicationQuit()
    {
        if (serverProcess != null && !serverProcess.HasExited)
        {
            serverProcess.Kill();
            serverProcess.Dispose();
        }
    }

    // Method to launch the Python server process
    void LaunchPythonServer()
    {
        string exePath = Path.Combine(Application.streamingAssetsPath, pythonServerPath);

        if (!File.Exists(exePath))
        {
            UnityEngine.Debug.LogError("Python server script not found at: " + exePath);
            return;
        }

        ProcessStartInfo startInfo = new ProcessStartInfo
        {
            FileName = exePath, // Ensure Python is in your PATH or provide full path to python executable
            UseShellExecute = false,
            RedirectStandardOutput = true,
            CreateNoWindow = true
        };

        // Inject environment variables
        startInfo.EnvironmentVariables["GROQ_API_KEY"] = GROQ_API_KEY;
        startInfo.EnvironmentVariables["MAX_TOKENS"] = MAX_TOKENS;
        startInfo.EnvironmentVariables["TEMPERATURE"] = TEMPERATURE;

        serverProcess = new Process();
        serverProcess.StartInfo = startInfo;

        serverProcess.OutputDataReceived += (sender, args) =>
        {
            if (!string.IsNullOrEmpty(args.Data))
            {
                UnityEngine.Debug.Log("Python Server Output: " + args.Data);
            }
        };

        try
        {
            serverProcess.Start();
            serverProcess.BeginOutputReadLine(); // Start reading output asynchronously
            UnityEngine.Debug.Log("Python server started successfully.");
        }
        catch (System.Exception ex)
        {
            UnityEngine.Debug.LogError("Failed to start Python server: " + ex.Message);
        }
    }
}
