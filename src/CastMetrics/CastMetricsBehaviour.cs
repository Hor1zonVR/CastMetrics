using System;
using UnityEngine;

namespace CastMetrics;

/// <summary>
/// Frame-accurate VR readout. A tracked controller is not proof that a wand is equipped.
/// </summary>
public sealed class CastMetricsBehaviour : MonoBehaviour
{
    public CastMetricsBehaviour(IntPtr pointer) : base(pointer) { }

    private GameObject? _hud;
    private TextMesh? _text;
    private Transform? _rig;
    private Transform? _eye;
    private Transform? _hand;
    private readonly WandTracker _wand = new();
    private float _nextTextRefresh;
    private float _nextRigBind;
    private bool _loggedLocalRig;

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
        Plugin.Logger.LogInfo("[CastMetrics] Readout created: hides without held-wand anchor.");
    }

    private void LateUpdate()
    {
        if (_hud == null || _text == null) return;

        float now = Time.realtimeSinceStartup;
        // Keep collecting bounded casting diagnostics even when the HUD is hidden.
        CastHooks.ReportProbeSnapshot(now);
        if (!Plugin.Enabled.Value || (!Plugin.ShowCasts.Value && !Plugin.ShowDps.Value))
        {
            SetVisible(false);
            return;
        }

        if (_rig == null || _eye == null || _hand == null || now >= _nextRigBind)
        {
            _nextRigBind = now + 2f;
            BindLocalRig();
        }
        if (_eye == null)
        {
            SetVisible(false);
            return;
        }

        _wand.Bind(_rig, _hand, now);
        Transform? anchor = _wand.HeldAnchor;
        if (anchor == null && !Plugin.DebugPreviewAtHeadset.Value)
        {
            SetVisible(false);
            return;
        }
        if (Plugin.HideWhenIdle.Value && now - CastWindow.LastCast > Plugin.IdleSeconds.Value)
        {
            SetVisible(false);
            return;
        }

        if (now >= _nextTextRefresh)
        {
            _nextTextRefresh = now + 0.2f;
            string casts = !CastHooks.HasVerifiedHook
                ? "Casts/s: hook not found"
                : !CastHooks.HasObservedCast
                    ? "Casts/s: awaiting recognized spell"
                    : "Casts/s: " + CastWindow.CastsPerSecond(now, Plugin.WindowSeconds.Value).ToString("0.00") +
                      (CastHooks.IsExperimentalSpades ? " (test)" : "");
            const string dps = "DPS: pending attribution";
            _text.text = Plugin.ShowCasts.Value && Plugin.ShowDps.Value
                ? casts + "\n" + dps
                : Plugin.ShowCasts.Value ? casts : dps;
        }

        Vector3 position = anchor == null
            ? _eye.position + _eye.forward * 0.65f - _eye.up * 0.15f
            : anchor.position + anchor.right * Plugin.OffsetX.Value +
              anchor.up * Plugin.OffsetY.Value + anchor.forward * Plugin.OffsetZ.Value;
        _hud.transform.position = position;
        Vector3 toEye = _eye.position - position;
        if (toEye.sqrMagnitude > 0.0001f)
        {
            // This is the readable orientation from the second VR recording.
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
            _rig = null;
            _eye = null;
            _hand = null;
            return;
        }
        _rig = rig.transform;
        _eye = _rig.Find("Camera (eye)");
        _hand = _rig.Find("Controller (right)");
        if (!_loggedLocalRig && _eye != null && _hand != null)
        {
            _loggedLocalRig = true;
            Plugin.Logger.LogInfo("[CastMetrics] Local VR eye/right hand resolved. " +
                "Readout requires a separate equipped-wand match.");
        }
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
