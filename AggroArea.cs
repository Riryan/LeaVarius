using System.Collections.Generic;
using UnityEngine;
using Mirror;

[DisallowMultipleComponent]
public sealed class AggroArea : MonoBehaviour
{
    [SerializeField] private Collider trigger;
    [SerializeField] private Entity owner;

    private static readonly Stack<HashSet<Entity>> Pool = new Stack<HashSet<Entity>>(32);
    private static readonly Entity[] Empty = System.Array.Empty<Entity>();
    private HashSet<Entity> tracked;

    public int Count => tracked?.Count ?? 0;
    public IReadOnlyCollection<Entity> Tracked => tracked ?? (IReadOnlyCollection<Entity>)Empty;

    public void CopySnapshotNonAlloc(List<Entity> buffer)
    {
        buffer.Clear();
        if (tracked == null) return;
        foreach (var e in tracked)
            if (e != null) buffer.Add(e);
    }

    private void Awake()
    {
        if (owner == null) owner = GetComponentInParent<Entity>();
        if (trigger == null) trigger = GetComponent<Collider>();
    }

    private void OnEnable()
    {
#if UNITY_SERVER || UNITY_EDITOR
        tracked = Pool.Count > 0 ? Pool.Pop() : new HashSet<Entity>();
        tracked.Clear();
        if (trigger != null) trigger.isTrigger = true;
#endif
    }

    private void OnDisable()
    {
#if UNITY_SERVER || UNITY_EDITOR
        if (tracked != null)
        {
            tracked.Clear();
            Pool.Push(tracked);
            tracked = null;
        }
#endif
    }

    [ServerCallback]
    private void OnTriggerEnter(Collider other)
    {
        if (tracked == null) return;
        var e = other.GetComponentInParent<Entity>();
        if (e == null || e == owner) return;
        tracked.Add(e);
    }

    [ServerCallback]
    private void OnTriggerExit(Collider other)
    {
        if (tracked == null) return;
        var e = other.GetComponentInParent<Entity>();
        if (e == null) return;
        tracked.Remove(e);
    }
}
