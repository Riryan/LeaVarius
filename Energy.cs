using UnityEngine;
using UnityEngine.Events;
using Mirror;

[RequireComponent(typeof(Health))] 
public abstract partial class Energy : NetworkBehaviour
{
    [SyncVar] int _current = 0;

    public int current
    {
        get
        {
            int m = max;
            return _current > m ? m : _current;
        }
        set
        {
            if (!isServer) return;
            bool wasEmpty = _current == 0;
            int m = max;
            int v = value;
            if (v < 0) v = 0;
            if (v > m) v = m;
            _current = v;
            if (_current == 0 && !wasEmpty)
                onEmpty.Invoke();
        }
    }

    public abstract int max { get; }
    public abstract int recoveryRate { get; }
    public abstract int drainRate { get; }

    [Tooltip("Reference wired automatically if left empty.")]
    public Health health;

    [Tooltip("If true, energy starts at max on server spawn.")]
    public bool spawnFull = true;

    [Header("Events")]
    public UnityEvent onEmpty;

    void Awake()
    {
        if (health == null) health = GetComponent<Health>();
    }

#if UNITY_SERVER || UNITY_EDITOR
    public override void OnStartServer()
    {
        if (spawnFull) current = max;
        InvokeRepeating(nameof(Recover), 1f, 1f);
    }

    void OnDisable()
    {
        if (isServer) CancelInvoke(nameof(Recover));
    }

    void OnDestroy()
    {
        if (isServer) CancelInvoke(nameof(Recover));
    }
#endif

    public float Percent()
    {
        int m = max;
        return (m > 0 && current > 0) ? (float)current / m : 0f;
    }

#if UNITY_SERVER || UNITY_EDITOR
    [Server]
    public void Recover()
    {
        if (!enabled || health == null) return;
        if (health.current > 0 && recoveryRate > 0)
            current = _current + recoveryRate;
    }
    [Server]
    public void Draining()
    {
        if (!enabled || health == null) return;
        if (health.current > 0 && drainRate > 0)
            current = _current - drainRate;
    }
#endif
}
