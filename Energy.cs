using UnityEngine;
using Mirror;

[DisallowMultipleComponent]
public abstract partial class Energy : NetworkBehaviour
{
    [SyncVar] int _current = 0;

    [SerializeField] bool spawnFull = false;
    [SerializeField] float recoveryInterval = 1f;

    int? _pendingApply;
    int _lastMax = -1;
    float _recoveryTimer = 0f;

    public abstract int max { get; }
    public abstract int recoveryRate { get; }
    public abstract int drainRate { get; }

    public float Percent()
    {
        int m = max;
        if (m <= 0) return 0f;
        int c = _current > m ? m : _current;
        return (float)c / m;
    }

    public int current
    {
        get
        {
            int m = max;
            return _current > m ? m : _current;
        }
        [Server]
        set
        {
            int v = value < 0 ? 0 : value;
            _current = v;
            if (max <= 0) _pendingApply = v;
            else _pendingApply = null;
        }
    }

    protected Health health;

    protected virtual void Awake()
    {
        health = GetComponent<Health>();
    }

    public override void OnStartServer()
    {
        base.OnStartServer();
        _lastMax = max;
        if (spawnFull && _pendingApply == null && _current <= 0 && _lastMax > 0)
            _current = _lastMax;
        _recoveryTimer = 0f;
    }

    [ServerCallback]
    void Update()
    {
        if (_pendingApply.HasValue && max > 0)
        {
            _current = _pendingApply.Value < 0 ? 0 : _pendingApply.Value;
            _pendingApply = null;
        }

        if (max > 0 && health != null && health.current > 0 && recoveryRate > 0 && _current < max)
        {
            _recoveryTimer += Time.deltaTime;
            if (_recoveryTimer >= recoveryInterval)
            {
                int next = _current + recoveryRate;
                if (next > max) next = max;
                current = next;
                _recoveryTimer = 0f;
            }
        }
        else
        {
            _recoveryTimer = 0f;
        }
    }

#if UNITY_SERVER || UNITY_EDITOR
    [Server]
    public void Recovering()
    {
        if (!enabled || health == null) return;
        if (health.current > 0 && recoveryRate > 0 && _current < max)
        {
            int next = _current + recoveryRate;
            if (next > max) next = max;
            current = next;
        }
    }

    [Server]
    public void Draining()
    {
        if (!enabled || health == null) return;
        if (health.current > 0 && drainRate > 0 && _current > 0)
        {
            int next = _current - drainRate;
            if (next < 0) next = 0;
            current = next;
        }
    }
#endif
}
