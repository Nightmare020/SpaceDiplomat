using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Very small gatekeeper: exposes two helpers used by Chat Manager.
///  
/// TERMINAL = joy >= 0.99 (success) OR anger >= 0.99 (failure)
/// 
/// </summary>
public static class GoapGate
{
    public static bool IsSuccess(float joy) => joy >= 0.99f;
    public static bool IsFailure(float anger) => anger >= 0.99f;
}
