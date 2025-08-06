using System.Collections;
using System.Diagnostics;
using System.IO;
using UnityEngine;

public class LlamaServerProcess : MonoBehaviour
{
    private Process _proc;

    [Header("Relative to StreamingAssets")]
    public string exeRelative = "Bin/llama-server.exe";
    public string modelRelative = "Models/llama-3.1-8b-instruct.Q4_K_M.gguf";
    public int port = 11434;
    public int ctxSize = 2048;
    public int gpuLayers = 0;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    private IEnumerator Start()
    {
        string root = Application.streamingAssetsPath;
        string exePath = Path.Combine(root, exeRelative);
        string modelPath = Path.Combine(root, modelRelative);

        if (!File.Exists(exePath))
        {
            UnityEngine.Debug.LogError($"Executable not found: {exePath}");
            yield break;
        }

        if (!File.Exists(modelPath))
        {
            UnityEngine.Debug.LogError($"Model file not found: {modelPath}");
            yield break;
        }

        var args =
            $"--model \"{modelPath}\" " +
            $"--port {port} --ctx-size {ctxSize} --n-gpu-layers {gpuLayers} --api";

        var psi = new ProcessStartInfo(exePath, args)
        {
            CreateNoWindow = true,
            UseShellExecute = false,
        };

        _proc = Process.Start(psi);
        DontDestroyOnLoad(gameObject);

        // Wait until the server is ready
        var url = $"http://localhost:{port}/v1/models";
        var startTime = Time.realtimeSinceStartup;
        while (Time.realtimeSinceStartup - startTime < 5f)
        {
            using (var www = new UnityEngine.Networking.UnityWebRequest(url, "GET"))
            {
                www.downloadHandler = new UnityEngine.Networking.DownloadHandlerBuffer();
                yield return www.SendWebRequest();

                if (www.result == UnityEngine.Networking.UnityWebRequest.Result.Success)
                { 
                    yield break;
                }
            }
            yield return new WaitForSeconds(0.25f);
        }
    }

    private void OnApplicationQuit()
    {
        try 
        { 
            if (_proc != null && !_proc.HasExited)
            {
                _proc.Kill();
            }
        }
        catch {}
    }
}
