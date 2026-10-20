using System;
using System.Collections.Generic;
using UnityEngine;

namespace CastMetrics;

/// <summary>
/// Fast local-rig-attached VR readout. No scene-wide Transform enumeration.
/// Follow and billboard every late frame; change text only five times/second.
/// </summary>
public sealed class CastMetricsBehaviour : MonoBehaviour
{
    public CastMetricsBehaviour(IntPtr pointer) : base(pointer) { }

    private GameObject? _hud;
    private TextMesh? _text;
    private Transform? _eye;
    private Transform? _hand;
    private Transform? _wandTip;
    private float _nextTextRefresh;
    private float _nextRigBind;
    private bool _loggedAnchor;

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
            Font? font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            if (font != null)
            {
                _text.font = font;
                MeshRenderer? renderer = _hud.GetComponent<MeshRenderer>();
                if (renderer != null) renderer.material = font.material;
            }
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogWarning("[CastMetrics] Font fallback: " + ex.Message);
        }
        _hud.SetActive(false);
        Plugin.Logger.LogInfo("[CastMetrics] Readout created. Fast LateUpdate follow; local-rig binding.");
    }

    private void LateUpdate()
    {
        if (_hud == null || _text == null) return;
        if (!Plugin.Enabled.Value ||
            (!Plugin.ShowCasts.Value && !Plugin.ShowDps.Value))
        {
            SetVisible(false);
            return;
        }

        float now = Time.realtimeSinceStartup;
        if (_eye == null || _hand == null || now >= _nextRigBind)
        {
            _nextRigBind = now + 2f;
            BindLocalRig();
        }
        if (_eye == null)
        {
            SetVisible(false);
            return;
        }

        // A fixed VR controller anchor is significantly more reliable than
        // a global nearest-wand search. Local wand-tip child is preferred.
        bool hasWand = _wandTip != null && _wandTip.gameObject.activeInHierarchy;
        Transform? anchor = hasWand ? _wandTip : _hand;
        if (anchor == null && !Plugin.DebugPreviewAtHeadset.Value)
        {
            SetVisible(false);
            return;
        }

        if (Plugin.HideWhenIdle.Value &&
            now - CastWindow.LastCast > Plugin.IdleSeconds.Value)
        {
            SetVisible(false);
            return;
        }

        if (now >= _nextTextRefresh)
        {
            _nextTextRefresh = now + 0.2f;
            string casts = !CastHooks.HasVerifiedHook
                ? "Casts/s: pending hook"
                : !CastHooks.HasObservedCast
                    ? "Casts/s: waiting for events"
                    : "Casts/s: " +
                      CastWindow.CastsPerSecond(now, Plugin.WindowSeconds.Value).ToString("0.00") +
                      (CastHooks.IsExperimentalSpades ? " (test)" : "");
            const string dps = "DPS: pending attribution";
            _text.text = Plugin.ShowCasts.Value && Plugin.ShowDps.Value
                ? casts + "\n" + dps
                : Plugin.ShowCasts.Value ? casts : dps;
            CastHooks.ReportProbeSnapshot(now);
        }

        Vector3 position;
        if (anchor != null)
        {
            // Local controller/wand axes so the text stays attached during roll.
            position = anchor.position + anchor.right * Plugin.OffsetX.Value +
                       anchor.up * Plugin.OffsetY.Value +
                       anchor.forward * Plugin.OffsetZ.Value;
        }
        else
        {
            // Explicit opt-in diagnostics only, never a normal HUD fallback.
            position = _eye.position + _eye.forward * 0.65f - _eye.up * 0.15f;
        }

        _hud.transform.position = position;
        // Original v0.1 TextMesh read backwards in the actual Spades recording.
        // Reverse the face relative to the eye; keep upright in world space.
        Vector3 toEye = _eye.position - position;
        if (toEye.sqrMagnitude > 0.0001f)
        {
            _hud.transform.rotation = Quaternion.LookRotation(toEye, Vector3.up) *
                                      Quaternion.Euler(0f, 180f, 0f);
        }
        _hud.transform.localScale = Vector3.one * Plugin.Scale.Value;
        SetVisible(true);
    }

    private void BindLocalRig()
    {
        GameObject? rig = GameObject.Find("PlayerRigv4");
        if (rig == null)
        {
            _eye = null;
            _hand = null;
            _wandTip = null;
            return;
        }
        Transform root = rig.transform;
        // Must use the headset's stereo eye, not Camera.main (which can be
        // RebornCam's desktop recording camera).
        _eye = root.Find("Camera (eye)");
        _hand = root.Find("Controller (right)") ?? root.Find("Controller (left)");
        _wandTip = _hand == null ? null : FindLocalWandTip(_hand);
        if (!_loggedAnchor && _eye != null && _hand != null)
        {
            _loggedAnchor = true;
            Plugin.Logger.LogInfo("[CastMetrics] Local eye=" + _eye.name +
                "; controller=" + _hand.name +
                "; wand-tip child=" + (_wandTip == null ? "(not found; using controller)" : _wandTip.name) +
                ". Global wand search disabled.");
        }
    }

    private static Transform? FindLocalWandTip(Transform hand)
    {
        // Inspect only the local controller's descendants, at most 100 nodes.
        var queue = new Queue<Transform>();
        queue.Enqueue(hand);
        int inspected = 0;
        while (queue.Count != 0 && inspected++ < 100)
        {
            Transform t = queue.Dequeue();
            if (t != hand && t.gameObject.activeInHierarchy)
            {
                string n = t.name.ToLowerInvariant();
                if (n.Contains("wand") && n.Contains("tip")) return t;
            }
            int children = Math.Min(t.childCount, 30);
            for (int i = 0; i < children; i++) queue.Enqueue(t.GetChild(i));
        }
        return null;
    }

    private void SetVisible(bool visible)
    {
        if (_hud != null && _hud.activeSelf != visible)
            _hud.SetActive(visible);
    }

    private void OnDestroy()
    {
        if (_hud != null) UnityEngine.Object.Destroy(_hud);
    }
}
