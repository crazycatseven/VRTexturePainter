using UnityEngine;
using UnityEngine.Rendering; // for CommandBuffer
using UnityEngine.InputSystem;

public class ProjectionPainter : MonoBehaviour
{


    [Header("Input Settings")]
    public InputActionReference paintAction;

    [Header("References")]
    public Shader uvShader;                  // UV visualization shader
    public Renderer targetRenderer;          // Target object to paint on
    public Transform brushTransform;         // VR Controller or brush transform

    [Header("Paint Camera Settings")]
    private Camera paintCamera;              // Generated paint camera
    public float paintCameraDistance = 0.05f;  // Distance from brush tip
    public float paintCameraFOV = 30f;        // Narrow FOV for precise painting

    [Header("Texture / RenderTexture Settings")]
    public RenderTextureQuality uvRenderTextureQuality = RenderTextureQuality.Medium;
    private RenderTexture uvRenderTexture;
    private Texture2D readbackTexture;

    [Header("Compute Shader")]
    public ComputeShader painterComputeShader;
    private RenderTexture paintRenderTexture;
    private int texWidth, texHeight;


    [Header("Brush Settings")]
    public Color brushColor = Color.red;
    [Range(0.001f, 0.1f)]
    public float brushSize = 0.01f;          // World space brush size
    public float paintRadius = 5.0f;         // Brush radius in pixels

    [Header("Visualization Settings")]
    private LineRenderer brushGuideLineRenderer;
    private GameObject brushVisualizer;
    public Material brushVisualizerMaterial;
    public float guideLineLength = 0.2f;
    [Tooltip("Angle offset for guide line in degrees")]
    public Vector3 guideLineRotationOffset = new Vector3(0, 0, 0);

    public Color normalColor = Color.white;
    public Color hoverColor = Color.yellow;

    [Header("Debug Settings")]
    public MeshRenderer debugMeshRenderer;
    private Material debugPlaneMaterial;

    // Material for UV shader
    private Material uvMaterial;

    public enum RenderTextureQuality
    {
        Low = 256,
        Medium = 512,
        High = 1024,
        Ultra = 2048
    }


    private void Start()
    {
        if (!ValidateReferences()) return;
        SetupMaterialAndTexture();
        SetupRenderTextures();
        SetupPaintCamera();
        SetupVisualizers();
        SetupDebugPlane();
    }


    private bool ValidateReferences()
    {
        if (targetRenderer == null)
        {
            Debug.LogError("Target renderer not assigned!");
            return false;
        }
        if (uvShader == null)
        {
            Debug.LogError("UV shader not assigned!");
            return false;
        }
        if (brushTransform == null)
        {
            Debug.LogError("Brush transform not assigned!");
            return false;
        }
        return true;
    }

    private void SetupMaterialAndTexture()
    {
        uvMaterial = new Material(uvShader);

        Texture2D originalMainTex = targetRenderer.material.mainTexture as Texture2D;
        if (originalMainTex == null)
        {
            Debug.LogError("Material's main texture is not a Texture2D or is null!");
            return;
        }

        texWidth = originalMainTex.width;
        texHeight = originalMainTex.height;

        // Create a RenderTexture supporting random write
        paintRenderTexture = new RenderTexture(texWidth, texHeight, 0, RenderTextureFormat.ARGBFloat);
        paintRenderTexture.enableRandomWrite = true;
        paintRenderTexture.useMipMap = false;
        paintRenderTexture.filterMode = FilterMode.Point;
        paintRenderTexture.wrapMode = TextureWrapMode.Clamp;
        paintRenderTexture.Create();

        // Copy the original texture to the paintRenderTexture
        Graphics.Blit(originalMainTex, paintRenderTexture);

        // Update the targetRenderer's material to use the paintRenderTexture
        targetRenderer.material.mainTexture = paintRenderTexture;
    }

    private void SetupRenderTextures()

    {
        int resolution = (int)uvRenderTextureQuality;

        uvRenderTexture = new RenderTexture(resolution, resolution, 0, RenderTextureFormat.ARGBFloat);
        uvRenderTexture.enableRandomWrite = true;
        uvRenderTexture.useMipMap = false;
        uvRenderTexture.filterMode = FilterMode.Point;
        uvRenderTexture.wrapMode = TextureWrapMode.Clamp;
        uvRenderTexture.Create();

        readbackTexture = new Texture2D(
            uvRenderTexture.width,
            uvRenderTexture.height,
            TextureFormat.RGBAFloat,
            false,
            true
        );
    }

    private void SetupPaintCamera()
    {
        GameObject paintCamObj = new GameObject("Paint Camera");
        paintCamera = paintCamObj.AddComponent<Camera>();
        paintCamera.enabled = false;  // We only use it for rendering to RT
        paintCamera.clearFlags = CameraClearFlags.SolidColor;
        paintCamera.backgroundColor = Color.clear;
        paintCamera.cullingMask = 1 << targetRenderer.gameObject.layer;
        paintCamera.nearClipPlane = 0.01f;
        paintCamera.farClipPlane = 1000f;
        paintCamera.fieldOfView = paintCameraFOV;
    }

    private void UpdatePaintCameraTransform()
    {
        if (brushTransform == null || paintCamera == null) return;

        // Calculate rotated direction using the same offset as guide line
        Quaternion offsetRotation = Quaternion.Euler(guideLineRotationOffset);
        Vector3 offsetDirection = offsetRotation * brushTransform.forward;

        // Position the paint camera behind the brush tip
        paintCamera.transform.position = brushTransform.position - offsetDirection * paintCameraDistance;
        paintCamera.transform.forward = offsetDirection;
    }

    private void Update()
    {
        UpdatePaintCameraTransform();
        UpdateVisualizers();

        // Check trigger value every frame
        if (paintAction != null)
        {
            float triggerValue = paintAction.action.ReadValue<float>();

            if (triggerValue > 0)
            {
                Paint();
            }
        }
    }

    private void SetupDebugPlane()
    {
        if (debugMeshRenderer == null) return;

        // Create and setup material for debug plane
        debugPlaneMaterial = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        debugPlaneMaterial.mainTexture = uvRenderTexture;
        debugMeshRenderer.material = debugPlaneMaterial;
    }



    // ================================  INPUT  ================================
    private void OnEnable()
    {
        if (paintAction != null)
        {
            paintAction.action.Enable();
        }
    }

    private void OnDisable()
    {
        if (paintAction != null)
        {
            paintAction.action.Disable();
        }
    }


    // ================================  PAINT CORE  ================================



    private void Paint()
    {
        if (paintCamera == null || uvMaterial == null){
            Debug.LogError("Paint camera or UV material is not set");
            return;
        }
        
        RenderUVPass();
        PaintWithComputeShader();
    }

    private void RenderUVPass()
    {
        CommandBuffer cmd = new CommandBuffer { name = "Render UV pass" };
        cmd.SetRenderTarget(uvRenderTexture);
        cmd.ClearRenderTarget(true, true, Color.clear);
        cmd.SetViewProjectionMatrices(paintCamera.worldToCameraMatrix, paintCamera.projectionMatrix);
        cmd.DrawRenderer(targetRenderer, uvMaterial, 0, 0);
        Graphics.ExecuteCommandBuffer(cmd);
        cmd.Release();

        if (debugMeshRenderer != null)
        {
            debugMeshRenderer.material.mainTexture = uvRenderTexture;
        }
    }

    private void PaintWithComputeShader(){
        if (painterComputeShader == null || paintRenderTexture == null || uvRenderTexture == null)
        {
            Debug.LogError("Compute shader or render textures are not set");
            return;
        }

        int uvWidth = uvRenderTexture.width;
        int uvHeight = uvRenderTexture.height;

        int mainWidth = paintRenderTexture.width;
        int mainHeight = paintRenderTexture.height;

        int kernel = painterComputeShader.FindKernel("CSMain");
        painterComputeShader.SetInt("_UVTexWidth", uvWidth);
        painterComputeShader.SetInt("_UVTexHeight", uvHeight);
        painterComputeShader.SetInt("_MainTexWidth", mainWidth);
        painterComputeShader.SetInt("_MainTexHeight", mainHeight);
        painterComputeShader.SetFloat("_BrushSize", brushSize);
        painterComputeShader.SetFloat("_PaintRadius", brushSize > 0 ? paintRadius : 5); // 这里 paintRadius 直接以像素为单位使用
        painterComputeShader.SetInt("_SampleRadius", 2);
        painterComputeShader.SetVector("_BrushColor", brushColor);

        painterComputeShader.SetTexture(kernel, "_UVTex", uvRenderTexture);
        painterComputeShader.SetTexture(kernel, "_MainTex", paintRenderTexture);

        painterComputeShader.Dispatch(kernel, 1, 1, 1);
    }


    // ================================  VISUALIZATION  ================================
    private void SetupVisualizers()
    {
        // Setup guide line
        GameObject lineObj = new GameObject("Brush Guide Line");
        lineObj.transform.SetParent(brushTransform);
        brushGuideLineRenderer = lineObj.AddComponent<LineRenderer>();
        
        // Create and configure the material
        Material lineMaterial = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        lineMaterial.color = normalColor;  // Set initial color
        
        brushGuideLineRenderer.material = lineMaterial;
        brushGuideLineRenderer.startWidth = 0.001f;
        brushGuideLineRenderer.endWidth = 0.001f;
        brushGuideLineRenderer.positionCount = 2;
        
        // Enable use of vertex colors
        brushGuideLineRenderer.useWorldSpace = true;
        brushGuideLineRenderer.colorGradient = new Gradient()
        {
            mode = GradientMode.Fixed,
            alphaKeys = new GradientAlphaKey[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(1, 1) },
            colorKeys = new GradientColorKey[] { new GradientColorKey(normalColor, 0), new GradientColorKey(normalColor, 1) }
        };
    }

    private void UpdateVisualizers()
    {
        if (brushTransform == null || brushGuideLineRenderer == null) return;

        // Calculate rotated direction using offset
        Quaternion offsetRotation = Quaternion.Euler(guideLineRotationOffset);
        Vector3 offsetDirection = offsetRotation * brushTransform.forward;

        RaycastHit hit;
        Vector3 startPos = brushTransform.position;
        bool didHit = Physics.Raycast(
            startPos, 
            offsetDirection, 
            out hit, 
            guideLineLength,
            1 << targetRenderer.gameObject.layer);

        if (didHit && hit.collider.gameObject == targetRenderer.gameObject)
        {
            brushGuideLineRenderer.SetPosition(0, startPos);
            brushGuideLineRenderer.SetPosition(1, hit.point);

            // Update color directly on the material
            brushGuideLineRenderer.material.color = hoverColor;
            // Also update the gradient
            brushGuideLineRenderer.startColor = hoverColor;
            brushGuideLineRenderer.endColor = hoverColor;
        }
        else
        {
            brushGuideLineRenderer.SetPosition(0, brushTransform.position);
            brushGuideLineRenderer.SetPosition(1, brushTransform.position + offsetDirection * guideLineLength);

            // Reset color directly on the material
            brushGuideLineRenderer.material.color = normalColor;
            // Also update the gradient
            brushGuideLineRenderer.startColor = normalColor;
            brushGuideLineRenderer.endColor = normalColor;
        }
    }

    private void OnDestroy()
    {
        if (uvRenderTexture != null)
            uvRenderTexture.Release();

        if (paintRenderTexture != null)
            paintRenderTexture.Release();

        if (paintCamera != null)
            Destroy(paintCamera.gameObject);

        if (uvMaterial != null)
            Destroy(uvMaterial);
    }

}
