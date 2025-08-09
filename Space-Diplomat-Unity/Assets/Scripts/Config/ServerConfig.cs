using UnityEngine;

public static class ServerConfig
{
    private static ServerSettings _settings;
    private static string _overrideBaseUrl;

    public static string BaseUrl
    {
        get
        {
            if (!string.IsNullOrEmpty(_overrideBaseUrl)) 
                return _overrideBaseUrl;

            if (_settings == null)
                _settings = Resources.Load<ServerSettings>("ServerSettings");

            // Fallback to localhost if the asset is missing
            return _settings != null ? _settings.BaseUrl : "http://127.0.0.1:5000";
        }
    }

    // Call this if the remote health checks fail
    public static void OverrideBaseUrl(string url) => _overrideBaseUrl = url;
}
