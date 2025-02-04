using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections.Generic;
using UnityEngine.UI;

public class VRMenuController : MonoBehaviour, IVRMenuSystem
{
    [Header("Input Settings")]
    public InputActionReference menuToggleAction;
    
    [Header("Menu Settings")]
    public Transform leftControllerTransform;
    public Vector3 menuOffset = new Vector3(0.1f, 0.1f, 0.5f);
    public Canvas menuCanvas;
    
    [Header("Fade Settings")]
    public float fadeDuration = 0.2f;
    public AnimationCurve fadeCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);
    
    private bool isVisible;
    private List<IVRMenuItem> menuItems = new List<IVRMenuItem>();
    private Camera mainCamera;
    private CanvasGroup canvasGroup;
    private float fadeTimer;
    private bool isFading;

    public bool IsVisible => isVisible;

    private void Awake()
    {
        mainCamera = Camera.main;
        menuCanvas.gameObject.SetActive(false);
        
        canvasGroup = menuCanvas.GetComponent<CanvasGroup>();
        if (canvasGroup == null)
        {
            canvasGroup = menuCanvas.gameObject.AddComponent<CanvasGroup>();
        }
    }

    private void OnEnable()
    {
        if (menuToggleAction != null)
        {
            menuToggleAction.action.Enable();
            menuToggleAction.action.performed += OnMenuToggle;
        }
    }

    private void OnDisable()
    {
        if (menuToggleAction != null)
        {
            menuToggleAction.action.performed -= OnMenuToggle;
            menuToggleAction.action.Disable();
        }
    }

    private void Update()
    {
        if (isFading)
        {
            UpdateFade();
        }
    }

    private void UpdateFade()
    {
        fadeTimer += Time.deltaTime;
        float progress = fadeTimer / fadeDuration;
        
        if (progress >= 1f)
        {
            progress = 1f;
            isFading = false;
            
            if (!isVisible)
            {
                menuCanvas.gameObject.SetActive(false);
            }
        }

        float alpha = isVisible ? 
            fadeCurve.Evaluate(progress) : 
            fadeCurve.Evaluate(1f - progress);
            
        canvasGroup.alpha = alpha;
    }

    private void UpdateMenuTransform()
    {
        Vector3 targetPosition = leftControllerTransform.position + 
                               leftControllerTransform.right * menuOffset.x +
                               leftControllerTransform.up * menuOffset.y +
                               leftControllerTransform.forward * menuOffset.z;
        
        transform.position = targetPosition;

        Vector3 directionToCamera = mainCamera.transform.position - transform.position;
        directionToCamera.y = 0;
        
        if (directionToCamera != Vector3.zero)
        {
            transform.rotation = Quaternion.LookRotation(-directionToCamera, Vector3.up);
        }
    }

    private void OnMenuToggle(InputAction.CallbackContext context)
    {
        if (isVisible)
            HideMenu();
        else
            ShowMenu();
    }

    public void ShowMenu()
    {
        isVisible = true;
        UpdateMenuTransform();
        menuCanvas.gameObject.SetActive(true);
        StartFade();
    }

    public void HideMenu()
    {
        isVisible = false;
        StartFade();
    }

    private void StartFade()
    {
        fadeTimer = 0f;
        isFading = true;
        
        if (isVisible)
        {
            canvasGroup.alpha = 0f;
        }
    }

    public void RegisterMenuItem(IVRMenuItem item)
    {
        if (!menuItems.Contains(item))
        {
            menuItems.Add(item);
            item.Initialize();
        }
    }

    public void UnregisterMenuItem(IVRMenuItem item)
    {
        menuItems.Remove(item);
    }
} 