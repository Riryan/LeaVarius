/// Server Component Stripping — keep one prefab for both builds, but automatically disable purely visual stuff on headless servers. 
/// Zero gameplay risk, immediate CPU/memory savings.
///  Drop this in as ServerComponentStripper.cs and add it to root prefabs you spawn on the server (Player, Monster, NPC, Harvestable). 
/// It runs only in headless mode (Application.isBatchMode), so Client+Host in Editor keeps visuals.

using System;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class ServerComponentStripper : MonoBehaviour
{
    [SerializeField] bool destroy = false;
    [SerializeField] bool stripRenderers = true;
    [SerializeField] bool stripAnimators = true;
    [SerializeField] bool stripParticleSystems = true;
    [SerializeField] bool stripLights = true;
    [SerializeField] bool stripAudioSources = true;
    [SerializeField] bool stripCameras = true;
    [SerializeField] bool stripReflectionProbes = true;
    [SerializeField] bool stripTextMeshPro = true;

    void Awake()
    {
#if UNITY_SERVER
        if (!Application.isBatchMode) return;
        Strip();
#endif
    }

#if UNITY_SERVER
    void Strip()
    {
        if (stripRenderers) StripComponents<Renderer>();
        if (stripAnimators) StripComponents<Animator>();
        if (stripParticleSystems) StripParticles();
        if (stripLights) StripComponents<Light>();
        if (stripAudioSources) StripComponents<AudioSource>();
        if (stripCameras) StripComponents<Camera>();
        if (stripReflectionProbes) StripComponents<ReflectionProbe>();
        if (stripTextMeshPro) StripByTypeName("TMPro.TMP_Text, Unity.TextMeshPro");
    }

    void StripComponents<T>() where T : Component
    {
        var arr = GetComponentsInChildren<T>(true);
        for (int i = 0; i < arr.Length; i++) DisableOrDestroy(arr[i]);
    }

    void StripParticles()
    {
        var psArr = GetComponentsInChildren<ParticleSystem>(true);
        for (int i = 0; i < psArr.Length; i++)
        {
            var ps = psArr[i];
            if (destroy) Destroy(ps);
            else
            {
                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                var r = ps.GetComponent<Renderer>();
                if (r) r.enabled = false;
            }
        }
    }

    void StripByTypeName(string typeName)
    {
        var t = Type.GetType(typeName);
        if (t == null) return;
        var arr = GetComponentsInChildren(t, true);
        for (int i = 0; i < arr.Length; i++)
        {
            var c = arr[i];
            if (destroy) Destroy(c);
            else if (c is Behaviour b) b.enabled = false;
        }
    }

    void DisableOrDestroy(Component c)
    {
        if (destroy) { Destroy(c); return; }
        if (c is Behaviour b) b.enabled = false;
        else if (c is Renderer r) r.enabled = false;
    }
#endif
}
