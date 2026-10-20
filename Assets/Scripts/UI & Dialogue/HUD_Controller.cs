using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.UI;

public class HUD_Controller : MonoBehaviour
{
    [SerializeField] private Slider stressBarFill;
    private void Awake()
    {
        if (stressBarFill == null)
            stressBarFill = GetComponent<Slider>();
    }

    private void OnEnable()
    {
            PlayerStress.OnStressChanged += UpdateStressBar;
        
    }
    private void OnDisable()
    {
            PlayerStress.OnStressChanged -= UpdateStressBar;
    }

    private void UpdateStressBar(float normalizeValue)
    {
            stressBarFill.value = normalizeValue;
    }
}
