using System;
using System.Collections.Generic;
using UnityEngine;

namespace CastMetrics;

/// <summary>
/// Conservatively finds a named weapon under the local player rig and checks
/// whether it is by the right hand. No full-scene searches, no controller fallback.
/// This will deliberately hide the readout if the actual weapon object is unknown.
/// </summary>
internal sealed class WandTracker
{
    private Transform? _root;
    private Transform? _hand;
    private Transform? _wand;
    private Transform? _tip;
    private float _nextScan;
    private float _nextReport;
    private int _reports;
    private bool _wasHeld;
    private string _lastSelection = "";

    internal Transform? HeldAnchor
    {
        get
        {
            if (!IsHeld()) return null;
            if (_tip != null && _tip.gameObject.activeInHierarchy) return _tip;
            return _wand;
        }
    }

    internal void Bind(Transform? root, Transform? hand, float now)
    {
        if (root == null || hand == null)
        {
            _root = null;
            _hand = null;
            _wand = null;
            _tip = null;
            return;
        }
        if (_root != root || _hand != hand)
        {
            _root = root;
            _hand = hand;
            _wand = null;
            _tip = null;
            _nextScan = 0f;
            _nextReport = 0f;
            _reports = 0;
            _lastSelection = "";
        }
        if (now < _nextScan) return;
        _nextScan = now + 1.5f;
        Scan(now);
    }

    private bool IsHeld()
    {
        if (_wand == null || _hand == null || !_wand.gameObject.activeInHierarchy)
            return false;
        return (_wand.position - _hand.position).sqrMagnitude < 0.35f * 0.35f;
    }

    private void Scan(float now)
    {
        if (_root == null || _hand == null) return;
        var queue = new Queue<Transform>();
        queue.Enqueue(_root);
        Transform? best = null;
        float bestDistance = float.MaxValue;
        var seen = new List<string>();
        int inspected = 0;

        // Local player hierarchy only, bounded to 1800 nodes every 1.5 seconds.
        while (queue.Count > 0 && inspected++ < 1800)
        {
            Transform t = queue.Dequeue();
            string name = t.name;
            if (t != _root && IsWandName(name) && !IsUiHierarchy(t))
            {
                if (seen.Count < 14)
                    seen.Add(name + "[" + (t.gameObject.activeInHierarchy ? "active" : "inactive") + "]");
                if (t.gameObject.activeInHierarchy)
                {
                    float distance = (t.position - _hand.position).sqrMagnitude;
                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        best = t;
                    }
                }
            }
            int children = Math.Min(t.childCount, 60);
            for (int i = 0; i < children; i++) queue.Enqueue(t.GetChild(i));
        }

        _wand = best;
        _tip = _wand == null ? null : FindTip(_wand);
        bool held = IsHeld();
        string selected = _wand == null ? "(none)" : _wand.name;
        if (selected != _lastSelection || held != _wasHeld)
        {
            _lastSelection = selected;
            _wasHeld = held;
            Plugin.Logger.LogInfo("[CastMetrics] Wand binding: object=" + selected +
                "; inHand=" + held + "; tip=" + (_tip == null ? "(none)" : _tip.name));
        }
        if (now >= _nextReport && _reports < 7)
        {
            _nextReport = now + 15f;
            _reports++;
            Plugin.Logger.LogInfo("[CastMetrics] Local-rig wand-name scan: inspected=" + inspected +
                "; found=[" + string.Join(", ", seen) + "]; inHand=" + held);
            if (seen.Count == 0)
                Plugin.Logger.LogInfo("[CastMetrics] Right-controller children: " + ChildNames(_hand));
        }
    }

    private static bool IsWandName(string name)
    {
        string n = name.ToLowerInvariant();
        return n.Contains("wand") || n.Contains("staff") ||
               n.Contains("runemage") || n.Contains("weapon");
    }

    private static bool IsUiHierarchy(Transform node)
    {
        for (int i = 0; node != null && i < 14; i++, node = node.parent)
        {
            string n = node.name.ToLowerInvariant();
            if (n.Contains("canvas") || n.Contains("inventory") ||
                n.Contains("paperdoll") || n.Contains("journal") ||
                n.Contains("menu") || n.Contains("backpack")) return true;
        }
        return false;
    }

    private static string ChildNames(Transform hand)
    {
        var names = new List<string>();
        var queue = new Queue<Transform>();
        queue.Enqueue(hand);
        int inspected = 0;
        while (queue.Count > 0 && inspected++ < 70)
        {
            Transform t = queue.Dequeue();
            if (t != hand && names.Count < 28)
                names.Add(t.name + (t.gameObject.activeInHierarchy ? "" : " (inactive)"));
            int children = Math.Min(t.childCount, 30);
            for (int i = 0; i < children; i++) queue.Enqueue(t.GetChild(i));
        }
        return string.Join(" -> ", names);
    }

    private static Transform? FindTip(Transform weapon)
    {
        var queue = new Queue<Transform>();
        queue.Enqueue(weapon);
        int inspected = 0;
        while (queue.Count > 0 && inspected++ < 90)
        {
            Transform t = queue.Dequeue();
            if (t != weapon && t.gameObject.activeInHierarchy)
            {
                string n = t.name.ToLowerInvariant();
                if (n.Contains("tip") || n.Contains("muzzle") || n.Contains("end"))
                    return t;
            }
            int children = Math.Min(t.childCount, 30);
            for (int i = 0; i < children; i++) queue.Enqueue(t.GetChild(i));
        }
        return null;
    }
}
