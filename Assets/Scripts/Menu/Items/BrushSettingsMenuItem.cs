using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class BrushSettingsMenuItem : MonoBehaviour, IVRMenuItem
{
    [Header("UI References")]
    public Slider brushSizeSlider;
    public TMP_Dropdown brushTextureDropdown;
    public Button colorPickerButton;
    
    [Header("Brush Textures")]
    public Texture2D[] availableBrushTextures;

    public string Name => "Brush Settings";

    private void Start()
    {
        SetupUI();
    }

    public void Initialize()
    {
        var painter = FindObjectOfType<ProjectionPainter>();
        if (painter != null)
        {
            brushSizeSlider.value = painter.brushSize;
            // TODO: Set other initial values...
        }
    }


    private void SetupUI()
    {
        if (brushSizeSlider != null)
        {
            brushSizeSlider.onValueChanged.AddListener(OnBrushSizeChanged);
        }

        if (brushTextureDropdown != null)
        {
            brushTextureDropdown.onValueChanged.AddListener(OnBrushTextureChanged);
        }

        if (colorPickerButton != null)
        {
            colorPickerButton.onClick.AddListener(OnColorPickerClicked);
        }
    }

    public void OnValueChanged(object value)
    {
        // TODO: Handle external value changes
    }


    private void OnBrushSizeChanged(float size)
    {
        MenuEvents.TriggerBrushSizeChanged(size);
    }

    private void OnBrushTextureChanged(int index)
    {
        if (index >= 0 && index < availableBrushTextures.Length)
        {
            MenuEvents.TriggerBrushTextureChanged(availableBrushTextures[index]);
        }
    }

    private void OnColorPickerClicked()
    {
        // TODO: Implement color picker logic
    }
} 