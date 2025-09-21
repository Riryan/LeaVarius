// NetworkName.cs
// Bandwidth-optimized: names replicate only on spawn and when they change.
// Back-compat goals: keep same component name; provide simple getters/setters;
// do NOT push any UI every frame. that saves about 40kb per client
// so at 1k CCU, that's a conservative ballpark 40mbps. 99% of that is wasted. 
using System;
using UnityEngine;
using Mirror;

[DisallowMultipleComponent]
public sealed class NetworkName : NetworkBehaviour
{
    [SerializeField] private int maxLength = 32;
    [SerializeField] private bool trimWhitespace = true;

    [SyncVar(hook = nameof(OnDisplayNameChanged))]
    private string displayName = string.Empty;

    public string DisplayName => displayName;
    public string GetName() => displayName;
    public event Action<string> NameChanged;

#if UNITY_SERVER || UNITY_EDITOR
    private static long s_updatesSentThisSession = 0;
    public static long UpdatesSentThisSession => s_updatesSentThisSession;
#endif

    [Server]
    public void SetDisplayNameServer(string newName)
    {
        string sanitized = Sanitize(newName);
        if (string.Equals(displayName, sanitized, StringComparison.Ordinal))
            return;
        displayName = sanitized;
#if UNITY_SERVER || UNITY_EDITOR
        s_updatesSentThisSession++;
#endif
    }

    [Server]
    public void InitializeDisplayName(string initialName) => SetDisplayNameServer(initialName);

#if !UNITY_SERVER || UNITY_EDITOR
    [Client]
    public void RequestRename(string requestedName)
    {
        if (!isLocalPlayer) return;
        CmdRequestRename(requestedName);
    }
#endif

    [Command]
    private void CmdRequestRename(string requestedName)
    {
        SetDisplayNameServer(requestedName);
    }

    private void OnDisplayNameChanged(string oldValue, string newValue)
    {
        NameChanged?.Invoke(newValue);
    }

    [Server]
    public void SetNameServer(string newName) => SetDisplayNameServer(newName);

    private string Sanitize(string raw)
    {
        string s = raw ?? string.Empty;
        if (trimWhitespace) s = s.Trim();
        if (s.Length > maxLength) s = s.Substring(0, maxLength);
        if (string.IsNullOrWhiteSpace(s)) s = "Unknown";
        return s;
    }
}
