using System;
using UnityEngine;

namespace CastMetrics;

/// <summary>Lightweight, read-only floating TextMesh; no colliders or game-object modifications.</summary>
public sealed class CastMetricsBehaviour : MonoBehaviour
{
    public CastMetricsBehaviour(IntPtr pointer) : base(pointer) { }

    private GameObject? _hud;
    private TextMesh? _text;
    private Camera? _camera;
    private float _nextUpdate;
    private float _nextCameraScan;
    private float _nextWandScan;
    private Transform? _wand;

    private void Start()
    {
        _hud = new GameObject("CastMetrics.Readout");
        UnityEngine.Object.DontDestroyOnLoad(_hud);
        _text = _hud.AddComponent<TextMesh>();
        _text.anchor = TextAnchor.MiddleCenter;
        _text.alignment = TextAlignment.Center;
        _text.fontSize = 44;
        _text.characterSize = 0.006f;
        _text.fontStyle = FontStyle.Bold;
        _text.color = new Color(0.82f, 0.95f, 1f, 0.96f);
        try
        {
            var font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            if (font != null)
            {
                _text.font = font;
                var renderer = _hud.GetComponent<MeshRenderer>();
                if (renderer != null) renderer.material = font.material;
            }
        }
        catch (Exception ex) { Plugin.Logger.LogWarning("[CastMetrics] Font fallback: " + ex.Message); }
        _hud.SetActive(false);
        Plugin.Logger.LogInfo("[CastMetrics] TextMesh readout created.");
    }

    private void Update()
    {
        if (_hud == null || _text == null) return;
        if (!Plugin.Enabled.Value)
        {
            _hud.SetActive(false);
            return;
        }
        float now = Time.realtimeSinceStartup;
        if (now < _nextUpdate) return;
        _nextUpdate = now + 0.1f;

        if (_camera == null || !_camera.isActiveAndEnabled)
        {
            if (now >= _nextCameraScan)
            {
                _nextCameraScan = now + 2f;
                _camera = Camera.main;
            }
        }
        if (_camera == null) { _hud.SetActive(false); return; }

        Transform? hooked = CastHooks.LastWand;
        if (hooked != null) _wand = hooked;
        if (_wand == null && now >= _nextWandScan)
        {
            _nextWandScan = now + 3f;
            _wand = FindNearbyWandTip(_camera.transform);
        }

        bool hasAnchor = _wand != null;
        if (!hasAnchor && !Plugin.DebugPreviewAtHeadset.Value)
        {
            _hud.SetActive(false);
            return;
        }
        if (Plugin.HideWhenIdle.Value && now - CastWindow.LastCast > Plugin.IdleSeconds.Value)
        {
            _hud.SetActive(false);
            return;
        }
        if (!Plugin.ShowCasts.Value && !Plugin.ShowDps.Value)
        {
            _hud.SetActive(false);
            return;
        }

        string castLine = !CastHooks.HasVerifiedHook
            ? "Casts/s: pending hook"
            : "Casts/s: " + CastWindow.CastsPerSecond(now, Plugin.WindowSeconds.Value).ToString("0.00");
        string dpsLine = "DPS: pending attribution";
        _text.text = Plugin.ShowCasts.Value && Plugin.ShowDps.Value ? castLine + "\n" + dpsLine :
                     Plugin.ShowCasts.Value ? castLine : dpsLine;

        Vector3 basePos = hasAnchor
            ? _wand!.TransformPoint(new Vector3(Plugin.OffsetX.Value, Plugin.OffsetY.Value, Plugin.OffsetZ.Value))
            : _camera.transform.position + _camera.transform.forward * 0.65f +
              _camera.transform.up * -0.15f;
        _hud.transform.position = basePos;
        _hud.transform.rotation = Quaternion.LookRotation(basePos - _camera.transform.position, Vector3.up);
        _hud.transform.localScale = Vector3.one * Plugin.Scale.Value;
        if (!_hud.activeSelf) _hud.SetActive(true);
    }

    private static Transform? FindNearbyWandTip(Transform cameraTransform)
    {
        // Only choose a currently active tip very close to the headset.
        // Never attach to distant players' wands or scene props.
        Transform? result = null;
        float best = 0.8f * 0.8f;
        try
        {
            foreach (Transform t in Resources.FindObjectsOfTypeAll<Transform>())
            {
                if (t == null || !t.gameObject.activeInHierarchy) continue;
                string name = t.name.ToLowerInvariant();
                if (!name.Contains("wand") || !name.Contains("tip")) continue;
                float dist = (t.position - cameraTransform.position).sqrMagnitude;
                if (dist < best) { best = dist; result = t; }
            }
        }
        catch { }
        return result;
    }

    private void OnDestroy()
    {
        if (_hud != null) UnityEngine.Object.Destroy(_hud);
    }
}
