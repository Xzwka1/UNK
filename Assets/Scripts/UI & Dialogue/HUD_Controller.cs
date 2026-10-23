using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Drives the dual-polarity Stress bar. PlayerStress broadcasts SignedNormalized (-1..+1),
/// so the slider is forced to range -1..+1 with 0 at the centre (equilibrium).
/// Negative = Brittle (crystal), Positive = Tensile (liquid).
/// </summary>
public class HUD_Controller : MonoBehaviour
{
    [SerializeField] private Slider stressBarFill;

    private void Awake()
    {
        if (stressBarFill == null)
            stressBarFill = GetComponent<Slider>();

        if (stressBarFill != null)
        {
            stressBarFill.minValue = -1f;
            stressBarFill.maxValue = 1f;
        }
    }

    private void OnEnable()
    {
        PlayerStress.OnStressChanged += UpdateStressBar;
    }

    private void OnDisable()
    {
        PlayerStress.OnStressChanged -= UpdateStressBar;
    }

    /// <summary>SignedNormalized in [-1..+1] from PlayerStress.</summary>
    private void UpdateStressBar(float normalizeValue)
    {
        if (stressBarFill == null) return;
        stressBarFill.value = normalizeValue;
    }
}
