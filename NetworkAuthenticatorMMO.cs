using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using UnityEngine;
using Mirror;

public class NetworkAuthenticatorMMO : NetworkAuthenticator
{
    [Header("Components")]
    public NetworkManagerMMO manager;

    [Header("Login")]
    public string loginAccount = "";
    public string loginPassword = "";

    [Header("Security")]
    [Tooltip("Static salt mixed with account name for client-side PBKDF2. Keep >= 16 chars.")]
    public string passwordSalt = "at_least_16_byte";
    public int accountMaxLength = 16;

    [Tooltip("Use non-enumerating error ('invalid credentials') instead of 'invalid account'.")]
    public bool genericLoginErrors = false;

    [Tooltip("Seconds between allowed login attempts per connection.")]
    public float loginCooldownSeconds = 0.4f;

    [Tooltip("Max consecutive failures before a temporary lockout.")]
    public int maxFailuresBeforeLockout = 5;

    [Tooltip("Temporary lockout duration in seconds after too many failures.")]
    public float lockoutSeconds = 3f;
    readonly Dictionary<int, (int fails, float nextAllowed)> _attempts = new();
    static readonly Regex kAccountRx = new Regex(@"^[A-Za-z0-9_]+$", RegexOptions.Compiled);

    void Awake()
    {
        if (manager == null)
            manager = FindObjectOfType<NetworkManagerMMO>() ?? NetworkManager.singleton as NetworkManagerMMO;
    }

    public override void OnStartClient()
    {
        NetworkClient.RegisterHandler<LoginSuccessMsg>(OnClientLoginSuccess, false);
    }

    public override void OnClientAuthenticate()
    {
        string hash = Utils.PBKDF2Hash(loginPassword, passwordSalt + loginAccount);
        LoginMsg message = new LoginMsg { account = loginAccount, password = hash, version = Application.version };
        NetworkClient.connection.Send(message);
        Debug.Log("login message was sent");
        if (manager != null)
            manager.state = NetworkState.Handshake;
            }

    void OnClientLoginSuccess(LoginSuccessMsg msg)
    {
        OnClientAuthenticated.Invoke();
    }

    public override void OnStartServer()
    {
        NetworkServer.RegisterHandler<LoginMsg>(OnServerLogin, false);
    }

    public override void OnServerAuthenticate(NetworkConnectionToClient conn) { }

    public virtual bool IsAllowedAccountName(string accountRaw)
    {
        if (string.IsNullOrWhiteSpace(accountRaw)) return false;
        string s = accountRaw.Normalize(NormalizationForm.FormC).Trim();
        return s.Length <= accountMaxLength && kAccountRx.IsMatch(s);
    }

    bool AccountLoggedIn(string account)
    {
        return manager.lobby.ContainsValue(account) ||
               Player.onlinePlayers.Values.Any(p => p.account == account);
    }

    void OnServerLogin(NetworkConnectionToClient conn, LoginMsg message)
    {
        int id = conn.connectionId;
        float now = Time.unscaledTime;
        // simple per-connection rate limit / lockout
        if (_attempts.TryGetValue(id, out var info) && now < info.nextAllowed)
        {
            manager.ServerSendError(conn, "slow down", false);
            return;
        }

        if (message.version == Application.version)
        {
            string account = message.account?.Normalize(NormalizationForm.FormC).Trim();

            if (IsAllowedAccountName(account))
            {
                if (Database.singleton.TryLogin(account, message.password))
                {
                    if (!AccountLoggedIn(account))
                    {
                        manager.lobby[conn] = account;
                        Debug.Log("login successful: " + account);
                        conn.Send(new LoginSuccessMsg());
                        OnServerAuthenticated.Invoke(conn);
                        _attempts[id] = (0, now + loginCooldownSeconds);
                    }
                    else
                    {
                        manager.ServerSendError(conn, "already logged in", true);
                        _attempts[id] = (info.fails, now + loginCooldownSeconds);
                    }
                }
                else
                {
                    int fails = info.fails + 1;
                    float delay = (fails >= maxFailuresBeforeLockout) ? lockoutSeconds : loginCooldownSeconds;
                    _attempts[id] = (fails, now + delay);

                    string msg = genericLoginErrors ? "invalid credentials" : "invalid account";
                    manager.ServerSendError(conn, msg, true);
                }
            }
            else
            {
                manager.ServerSendError(conn, "account name not allowed", true);
                _attempts[id] = (info.fails, now + loginCooldownSeconds);
            }
        }
        else
        {
            manager.ServerSendError(conn, "outdated version", true);
            _attempts[id] = (info.fails, now + loginCooldownSeconds);
        }
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        if (passwordSalt == null || passwordSalt.Length < 16)
            passwordSalt = "at_least_16_byte"; // keep default, satisfy >=16 bytes
        if (accountMaxLength < 2) accountMaxLength = 2;
        if (maxFailuresBeforeLockout < 1) maxFailuresBeforeLockout = 1;
        if (loginCooldownSeconds < 0f) loginCooldownSeconds = 0.2f;
        if (lockoutSeconds < 0f) lockoutSeconds = 0f;
    }
#endif
}
