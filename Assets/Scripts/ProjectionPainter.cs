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

    [Header("Compute Shader")]
    public ComputeShader painterComputeShader;
    private RenderTexture paintRenderTexture;
    private int texWidth, texHeight;
    private int computeKernel;


    [Header("Brush Settings")]
    
    [Tooltip("The black and white texture of the brush")]
    [SerializeField] private Texture2D brushTexture;
    public Color brushColor = Color.red;
    public int brushSize = 10;

    private Texture2D currentBrushTexture;

    public Texture2D BrushTexture
    {
        get => brushTexture;
        set
        {
            if (brushTexture != value)
            {
                brushTexture = value;
                UpdateBrushTextureInComputeShader();
            }
        }
    }

    [Header("Visualization Settings")]
    private LineRenderer brushGuideLineRenderer;
    private GameObject brushVisualizer;
    public Material brushVisualizerMaterial;
    public float guideLineLength = 0.2f;
    [Tooltip("Angle offset for guide line in degrees")]
    public Vector3 guideLineRotationOffset = new Vector3(0, 0, 0);
    public Color normalColor = Color.white;
    public Color hoverColor = Color.yellow;
    private GameObject brushSizeIndicator;
    private MeshRenderer brushSizeIndicatorRenderer;
    [SerializeField] private Shader brushSizeIndicatorShader;

    [Range(0.1f, 1.0f)] 
    public float brushSizeIndicatorThickness = 0.2f;
    public Color brushSizeIndicatorColor = Color.white;




    [Header("Debug Settings")]
    public MeshRenderer debugMeshRenderer;
    private Material debugPlaneMaterial;

    // Material for UV shader
    private Material uvMaterial;

    private bool isDrawing = false;

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
        SetupComputeShader();
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
        uvRenderTexture.filterMode = FilterMode.Bilinear;
        uvRenderTexture.wrapMode = TextureWrapMode.Clamp;
        uvRenderTexture.Create();
    }

    private void SetupComputeShader()
    {
        if (painterComputeShader != null)
        {
            computeKernel = painterComputeShader.FindKernel("CSMain");
            if (computeKernel < 0)
            {
                Debug.LogError("Compute kernel 'CSMain' not found!");
            }
        }

        UpdateBrushTextureInComputeShader();
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
        paintCamera.aspect = 1.0f;
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
            isDrawing = triggerValue > 0;

            if (isDrawing)
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
        MenuEvents.OnBrushSizeChanged += HandleBrushSizeChanged;
        MenuEvents.OnBrushTextureChanged += HandleBrushTextureChanged;
        MenuEvents.OnBrushColorChanged += HandleBrushColorChanged;
    }

    private void OnDisable()
    {
        if (paintAction != null)
        {
            paintAction.action.Disable();
        }
        MenuEvents.OnBrushSizeChanged -= HandleBrushSizeChanged;
        MenuEvents.OnBrushTextureChanged -= HandleBrushTextureChanged;
        MenuEvents.OnBrushColorChanged -= HandleBrushColorChanged;
    }

    private void HandleBrushSizeChanged(float size)
    {
        brushSize = Mathf.RoundToInt(size);
    }

    private void HandleBrushTextureChanged(Texture2D texture)
    {
        BrushTexture = texture;
    }

    private void HandleBrushColorChanged(Color color)
    {
        brushColor = color;
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
        
        // Render
        cmd.DrawRenderer(targetRenderer, uvMaterial, 0, 0);
        
        Graphics.ExecuteCommandBuffer(cmd);
        cmd.Release();

        // Debug display
        if (debugMeshRenderer != null)
        {
            debugMeshRenderer.material.mainTexture = uvRenderTexture;
        }

    }

    private void PaintWithComputeShader()
    {
        if (painterComputeShader == null || paintRenderTexture == null || uvRenderTexture == null)
        {
            Debug.LogError("Compute shader or necessary textures are not set");
            return;
        }

        int uvWidth = uvRenderTexture.width;
        int uvHeight = uvRenderTexture.height;
        int mainWidth = paintRenderTexture.width;
        int mainHeight = paintRenderTexture.height;

        // Compute the rectangular region affected by the brush
        int centerX = uvWidth / 2;
        int centerY = uvHeight / 2;
        int brushRadius = Mathf.Max(1, Mathf.CeilToInt(brushSize * 0.5f));

        // Compute the region to process (ensuring it doesn't exceed texture boundaries)

        int startX = Mathf.Max(0, centerX - brushRadius);
        int startY = Mathf.Max(0, centerY - brushRadius);
        int endX = Mathf.Min(uvWidth, centerX + brushRadius);
        int endY = Mathf.Min(uvHeight, centerY + brushRadius);

        // Calculate the size of the region
        int regionWidth = endX - startX;
        int regionHeight = endY - startY;


        int kernel = painterComputeShader.FindKernel("CSMain");
        
        // Pass region information to Compute Shader
        painterComputeShader.SetInt("_UVTexWidth", uvWidth);
        painterComputeShader.SetInt("_UVTexHeight", uvHeight);
        painterComputeShader.SetInt("_MainTexWidth", mainWidth);
        painterComputeShader.SetInt("_MainTexHeight", mainHeight);
        painterComputeShader.SetFloat("_BrushSize", brushSize);
        painterComputeShader.SetInt("_BrushCenterX", centerX);
        painterComputeShader.SetInt("_BrushCenterY", centerY);
        painterComputeShader.SetInt("_SampleRadius", 2);
        painterComputeShader.SetVector("_BrushColor", brushColor);
        painterComputeShader.SetInts("_RegionStart", startX, startY);


        painterComputeShader.SetTexture(kernel, "_UVTex", uvRenderTexture);
        painterComputeShader.SetTexture(kernel, "_MainTex", paintRenderTexture);
            
        // Use circular area calculation
        int diameter = brushSize * 2;
        int threadGroupSize = 16; // The size of one thread group
        

        // Ensure the processing area is at least one complete thread group size
        int dispatchSize = Mathf.CeilToInt((float)diameter / threadGroupSize) * threadGroupSize;
        

        // Calculate the new starting position to ensure the circular area is centered
        startX = Mathf.Max(0, centerX - dispatchSize/2);
        startY = Mathf.Max(0, centerY - dispatchSize/2);
        

        // Ensure it doesn't exceed the texture boundaries
        startX = Mathf.Min(startX, uvWidth - dispatchSize);
        startY = Mathf.Min(startY, uvHeight - dispatchSize);
        

        // Calculate the number of thread groups
        int threadGroupsX = dispatchSize / threadGroupSize;
        int threadGroupsY = dispatchSize / threadGroupSize;


        painterComputeShader.SetInts("_RegionStart", startX, startY);
        painterComputeShader.SetInt("_DispatchSize", dispatchSize);

        painterComputeShader.Dispatch(kernel, threadGroupsX, threadGroupsY, 1);
    }

    private void UpdateBrushTextureInComputeShader()
    {
        if (painterComputeShader != null)
        {
            if (brushTexture != null)
            {
                painterComputeShader.SetTexture(computeKernel, "_BrushTex", brushTexture);
                painterComputeShader.SetInt("_HasBrushTex", 1);
                painterComputeShader.SetInt("_BrushTexWidth", brushTexture.width);
                painterComputeShader.SetInt("_BrushTexHeight", brushTexture.height);
            }
            else
            {
                // Create a default texture
                Texture2D defaultTex = new Texture2D(1, 1);
                defaultTex.SetPixel(0, 0, Color.white);
                defaultTex.Apply();
                
                painterComputeShader.SetTexture(computeKernel, "_BrushTex", defaultTex);
                painterComputeShader.SetInt("_HasBrushTex", 0);
                painterComputeShader.SetInt("_BrushTexWidth", 1);
                painterComputeShader.SetInt("_BrushTexHeight", 1);
            }
            currentBrushTexture = brushTexture;
        }
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

        // Setup brush size indicator
        brushSizeIndicator = new GameObject("Brush Size Indicator");
        brushSizeIndicator.transform.SetParent(brushTransform);

        var meshFilter = brushSizeIndicator.AddComponent<MeshFilter>();
        brushSizeIndicatorRenderer = brushSizeIndicator.AddComponent<MeshRenderer>();

        Mesh quadMesh = new Mesh();
        quadMesh.vertices = new Vector3[] {
            new Vector3(-0.5f, -0.5f, 0),
            new Vector3(0.5f, -0.5f, 0),
            new Vector3(0.5f, 0.5f, 0),
            new Vector3(-0.5f, 0.5f, 0)
        };
        quadMesh.triangles = new int[] { 0, 1, 2, 0, 2, 3 };
        quadMesh.uv = new Vector2[] {
            new Vector2(0, 0),
            new Vector2(1, 0),
            new Vector2(1, 1),
            new Vector2(0, 1)
        };

        meshFilter.mesh = quadMesh;

        if (brushSizeIndicatorShader != null)
        {
            Material indicatorMaterial = new Material(brushSizeIndicatorShader);
            indicatorMaterial.renderQueue = 3000;
            indicatorMaterial.SetFloat("_Thickness", brushSizeIndicatorThickness);
            indicatorMaterial.SetColor("_Color", brushSizeIndicatorColor);
            brushSizeIndicatorRenderer.material = indicatorMaterial;
            brushSizeIndicatorRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            brushSizeIndicatorRenderer.receiveShadows = false;
        }
        else
        {
            Debug.LogError("Brush size indicator shader not assigned!");
        }
    }



    private void UpdateVisualizers()

    {
        if (brushTransform == null || brushGuideLineRenderer == null || brushSizeIndicator == null) return;

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

        if (didHit && hit.collider.gameObject == targetRenderer.gameObject && !isDrawing)
        {
            brushGuideLineRenderer.SetPosition(0, startPos);
            brushGuideLineRenderer.SetPosition(1, hit.point);

            // Update color directly on the material
            brushGuideLineRenderer.material.color = hoverColor;
            // Also update the gradient
            brushGuideLineRenderer.startColor = hoverColor;
            brushGuideLineRenderer.endColor = hoverColor;



            float distanceToSurface = hit.distance;
            float fieldOfViewRad = paintCamera.fieldOfView * Mathf.Deg2Rad;

            float frustumHeight = 2.0f * distanceToSurface * Mathf.Tan(fieldOfViewRad * 0.5f);
            float uvToWorldRatio = frustumHeight / (float)uvRenderTextureQuality;
            float worldSpaceSize = (brushSize / (float)uvRenderTextureQuality) * frustumHeight;

            brushSizeIndicator.transform.position = hit.point + hit.normal * 0.001f;
            brushSizeIndicator.transform.rotation = Quaternion.LookRotation(-hit.normal);
            brushSizeIndicator.transform.localScale = new Vector3(worldSpaceSize, worldSpaceSize, 1);

            brushSizeIndicator.SetActive(true);
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

            brushSizeIndicator.SetActive(false);
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

        if (brushSizeIndicatorRenderer != null && brushSizeIndicatorRenderer.material != null)
        {
            Destroy(brushSizeIndicatorRenderer.material);
        }
    }


}
