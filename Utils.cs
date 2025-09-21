using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using System.Linq;
using System.Security.Cryptography;
using System.Reflection;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Events;
using UnityEngine.Rendering; 

[Serializable] public class UnityEventString : UnityEvent<String> {}

public class Utils
{
    public static bool IsHeadless()
    {
#if UNITY_SERVER
        return true; // Dedicated Server build target
#else
        return Application.isBatchMode ||
               SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null;
#endif
    }

    public static long Clamp(long value, long min, long max)
    {
        if (value < min) return min;
        if (value > max) return max;
        return value;
    }

    public static bool AnyKeyUp(KeyCode[] keys)
    {
        if (IsHeadless()) return false;
        foreach (KeyCode key in keys)
            if (Input.GetKeyUp(key))
                return true;
        return false;
    }

    public static bool AnyKeyDown(KeyCode[] keys)
    {
        if (IsHeadless()) return false;
        foreach (KeyCode key in keys)
            if (Input.GetKeyDown(key))
                return true;
        return false;
    }

    public static bool AnyKeyPressed(KeyCode[] keys)
    {
        if (IsHeadless()) return false;
        foreach (KeyCode key in keys)
            if (Input.GetKey(key))
                return true;
        return false;
    }

    public static bool IsPointInScreen(Vector2 point)
    {
        if (IsHeadless()) return false;
        return 0 <= point.x && point.x < Screen.width &&
               0 <= point.y && point.y < Screen.height;
    }

    public static float BoundsRadius(Bounds bounds) =>
        (bounds.extents.x + bounds.extents.z) / 2;

    public static float ClosestDistance(Entity a, Entity b)
    {
        float distance = Vector3.Distance(a.transform.position, b.transform.position);
        if (a.collider == null || b.collider == null) return distance;

        float radiusA = BoundsRadius(a.collider.bounds);
        float radiusB = BoundsRadius(b.collider.bounds);
        float distanceInside = distance - radiusA - radiusB;
        return Mathf.Max(distanceInside, 0);
    }

    public static Vector3 ClosestPoint(Entity entity, Vector3 point)
    {
        if (entity.collider == null) return entity.transform.position;
        float radius = BoundsRadius(entity.collider.bounds);
        Vector3 direction = entity.transform.position - point;
        Vector3 directionSubtracted = Vector3.ClampMagnitude(direction, direction.magnitude - radius);
        return point + directionSubtracted;
    }

    static readonly Dictionary<Transform, int> castBackups = new Dictionary<Transform, int>();
    static readonly int kIgnoreRaycastLayer = LayerMask.NameToLayer("Ignore Raycast");

    public static bool RaycastWithout(
        Vector3 origin, Vector3 direction, out RaycastHit hit,
        float maxDistance, GameObject ignore,
        int layerMask = Physics.DefaultRaycastLayers)
    {
        hit = default;

        if (ignore == null)
            return Physics.Raycast(origin, direction, out hit, maxDistance, layerMask);

        castBackups.Clear();

        foreach (Transform tf in ignore.GetComponentsInChildren<Transform>(true))
        {
            castBackups[tf] = tf.gameObject.layer;
            tf.gameObject.layer = kIgnoreRaycastLayer;
        }

        bool result = Physics.Raycast(origin, direction, out hit, maxDistance, layerMask);

        foreach (KeyValuePair<Transform, int> kvp in castBackups)
            kvp.Key.gameObject.layer = kvp.Value;

        return result;
    }

    public static Bounds CalculateBoundsForAllRenderers(GameObject go)
    {
        Bounds bounds = new Bounds();
        bool initialized = false;
        foreach (Renderer rend in go.GetComponentsInChildren<Renderer>())
        {
            if (!initialized)
            {
                bounds = rend.bounds;
                initialized = true;
            }
            else bounds.Encapsulate(rend.bounds);
        }
        return bounds;
    }

    public static Transform GetNearestTransform(List<Transform> transforms, Vector3 from)
    {
        Transform nearest = null;
        foreach (Transform tf in transforms)
        {
            if (nearest == null ||
                Vector3.Distance(tf.position, from) < Vector3.Distance(nearest.position, from))
                nearest = tf;
        }
        return nearest;
    }

    public static string PrettySeconds(float seconds)
    {
        TimeSpan t = TimeSpan.FromSeconds(seconds);
        string res = "";
        if (t.Days > 0) res += t.Days + "d";
        if (t.Hours > 0) res += (res.Length > 0 ? " " : "") + t.Hours + "h";
        if (t.Minutes > 0) res += (res.Length > 0 ? " " : "") + t.Minutes + "m";
        if (t.Milliseconds > 0) res += (res.Length > 0 ? " " : "") + t.Seconds + "." + (t.Milliseconds / 100) + "s";
        else if (t.Seconds > 0) res += (res.Length > 0 ? " " : "") + t.Seconds + "s";
        return res != "" ? res : "0s";
    }

    public static float GetAxisRawScrollUniversal()
    {
        if (IsHeadless()) return 0;
        float scroll = Input.GetAxisRaw("Mouse ScrollWheel");
        if (scroll < 0) return -1;
        if (scroll > 0) return 1;
        return 0;
    }

    public static float GetPinch()
    {
        if (IsHeadless()) return 0;
        if (Input.touchCount == 2)
        {
            Touch touchZero = Input.GetTouch(0);
            Touch touchOne = Input.GetTouch(1);
            Vector2 touchZeroPrevPos = touchZero.position - touchZero.deltaPosition;
            Vector2 touchOnePrevPos = touchOne.position - touchOne.deltaPosition;
            float prevTouchDeltaMag = (touchZeroPrevPos - touchOnePrevPos).magnitude;
            float touchDeltaMag = (touchZero.position - touchOne.position).magnitude;
            return touchDeltaMag - prevTouchDeltaMag;
        }
        return 0;
    }

    public static float GetZoomUniversal()
    {
        if (IsHeadless()) return 0;
        if (Input.mousePresent)
            return GetAxisRawScrollUniversal();
        else if (Input.touchSupported)
            return GetPinch();
        return 0;
    }

    static readonly Regex lastNounRegex = new Regex(@"([A-Z][a-z]*)", RegexOptions.Compiled);

    public static string ParseLastNoun(string text)
    {
        MatchCollection matches = lastNounRegex.Matches(text);
        return matches.Count > 0 ? matches[matches.Count - 1].Value : "";
    }

    public static bool IsCursorOverUserInterface()
    {
        if (IsHeadless()) return false;

        EventSystem es = EventSystem.current;
        if (es != null)
        {
            if (es.IsPointerOverGameObject())
                return true;

            for (int i = 0; i < Input.touchCount; ++i)
                if (es.IsPointerOverGameObject(Input.GetTouch(i).fingerId))
                    return true;
        }

        return GUIUtility.hotControl != 0;
    }

    public static string PBKDF2Hash(string text, string salt)
    {
        byte[] saltBytes = Encoding.UTF8.GetBytes(salt);
        using (var pbkdf2 = new Rfc2898DeriveBytes(text, saltBytes, 10000))
        {
            byte[] hash = pbkdf2.GetBytes(20);
            return BitConverter.ToString(hash).Replace("-", string.Empty);
        }
    }

    static readonly Dictionary<KeyValuePair<Type, string>, MethodInfo[]> lookup =
        new Dictionary<KeyValuePair<Type, string>, MethodInfo[]>();

    public static MethodInfo[] GetMethodsByPrefix(Type type, string methodPrefix)
    {
        var key = new KeyValuePair<Type, string>(type, methodPrefix);
        if (!lookup.TryGetValue(key, out var methods))
        {
            methods = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance)
                          .Where(m => m.Name.StartsWith(methodPrefix))
                          .ToArray();
            lookup[key] = methods;
        }
        return methods;
    }

    public static void InvokeMany(Type type, object onObject, string methodPrefix, params object[] args)
    {
        foreach (MethodInfo method in GetMethodsByPrefix(type, methodPrefix))
            method.Invoke(onObject, args);
    }

    public static Quaternion ClampRotationAroundXAxis(Quaternion q, float min, float max)
    {
        q.x /= q.w;
        q.y /= q.w;
        q.z /= q.w;
        q.w = 1.0f;
        float angleX = 2.0f * Mathf.Rad2Deg * Mathf.Atan(q.x);
        angleX = Mathf.Clamp(angleX, min, max);
        q.x = Mathf.Tan(0.5f * Mathf.Deg2Rad * angleX);
        return q;
    }
}
