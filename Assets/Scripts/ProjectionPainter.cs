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

    [Header("Brush Settings")]
    public Color brushColor = Color.red;
    [Range(0.001f, 0.1f)]
    public float brushSize = 0.01f;          // World space brush size

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

    // Store texture pixels
    private Color[] mainTexPixels;
    private Texture2D mainTex2D;
    private int texWidth, texHeight;

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

        mainTex2D = targetRenderer.material.mainTexture as Texture2D;
        if (mainTex2D == null)
        {
            Debug.LogError("Material's main texture is not a Texture2D or is null!");
            return;
        }

        mainTex2D = Instantiate(mainTex2D);
        targetRenderer.material.mainTexture = mainTex2D;
        texWidth = mainTex2D.width;
        texHeight = mainTex2D.height;
        mainTexPixels = mainTex2D.GetPixels();
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
        ReadbackUVData();
        ApplyPaint();
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

    private void ReadbackUVData()

    {
        RenderTexture.active = uvRenderTexture;
        readbackTexture.ReadPixels(new Rect(0, 0, uvRenderTexture.width, uvRenderTexture.height), 0, 0);
        readbackTexture.Apply();
        RenderTexture.active = null;
    }

    private void ApplyPaint()
    {
        Vector2 brushCenter = GetBrushCenterInRT();
        Debug.Log($"Brush Center in RT: {brushCenter}");
        Color[] uvColors = readbackTexture.GetPixels();
        int rtW = uvRenderTexture.width;
        int rtH = uvRenderTexture.height;
        int paintCount = 0;

        const int sampleRadius = 2;
        int paintRadius = 5; // Increased for better coverage

        for (int py = 0; py < rtH; py++)
        {
            for (int px = 0; px < rtW; px++)
            {
                float dist = Vector2.Distance(new Vector2(px, py), brushCenter);
                if (dist > brushSize) continue;

                Vector2 uv = SampleUV(px, py, uvColors, rtW, rtH, sampleRadius);
                PaintAtUV(uv, dist, paintRadius, ref paintCount);
            }
        }

        mainTex2D.SetPixels(mainTexPixels);
        mainTex2D.Apply();
    }

    private Vector2 SampleUV(int px, int py, Color[] uvColors, int rtW, int rtH, int sampleRadius)
    {
        float u = 0, v = 0;
        float totalWeight = 0;

        for (int offsetX = -sampleRadius; offsetX <= sampleRadius; offsetX++)
        {
            for (int offsetY = -sampleRadius; offsetY <= sampleRadius; offsetY++)
            {
                int sampleX = Mathf.Clamp(px + offsetX, 0, rtW - 1);
                int sampleY = Mathf.Clamp(py + offsetY, 0, rtH - 1);
                int index = sampleY * rtW + sampleX;

                float distance = Mathf.Sqrt(offsetX * offsetX + offsetY * offsetY);
                float weight = Mathf.Exp(-(distance * distance) / (2 * sampleRadius * sampleRadius));
                totalWeight += weight;

                u += uvColors[index].r * weight;
                v += uvColors[index].g * weight;
            }
        }

        return new Vector2(u / totalWeight, v / totalWeight);
    }

    private void PaintAtUV(Vector2 uv, float dist, int radius, ref int paintCount)
    {
        float texXf = uv.x * texWidth;
        float texYf = uv.y * texHeight;

        for (int offsetX = -radius; offsetX <= radius; offsetX++)
        {
            for (int offsetY = -radius; offsetY <= radius; offsetY++)
            {
                int paintX = Mathf.FloorToInt(texXf) + offsetX;
                int paintY = Mathf.FloorToInt(texYf) + offsetY;

                if (paintX < 0 || paintX >= texWidth || paintY < 0 || paintY >= texHeight)
                    continue;

                float paintDist = Mathf.Sqrt(offsetX * offsetX + offsetY * offsetY);
                if (paintDist > radius)
                    continue;

                float brushStrength = 1.0f - (dist / brushSize);
                brushStrength *= 1.0f - (paintDist / radius);
                brushStrength = Mathf.SmoothStep(0, 1, brushStrength);

                int texIdx = paintY * texWidth + paintX;
                mainTexPixels[texIdx] = Color.Lerp(mainTexPixels[texIdx], brushColor, brushStrength);
                paintCount++;
            }
        }
    }

    private Vector2 GetBrushCenterInRT()
    {
        return new Vector2(
        uvRenderTexture.width * 0.5f,
        uvRenderTexture.height * 0.5f
    );
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

    private void OnDrawGizmos()
    {
        if (!Application.isPlaying || brushTransform == null) return;

        // Draw brush position and direction
        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(brushTransform.position, brushSize);
        Gizmos.DrawLine(brushTransform.position, brushTransform.position + brushTransform.forward * 0.1f);

        // Draw paint camera position and frustum
        if (paintCamera != null)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(paintCamera.transform.position, 0.01f);
            Gizmos.matrix = Matrix4x4.TRS(
                paintCamera.transform.position,
                paintCamera.transform.rotation,
                Vector3.one
            );
            Gizmos.DrawFrustum(
                Vector3.zero,
                paintCamera.fieldOfView,
                paintCamera.farClipPlane,
                paintCamera.nearClipPlane,
                paintCamera.aspect
            );
        }
    }

    private void OnDestroy()
    {
        if (uvRenderTexture != null)
            uvRenderTexture.Release();

        if (paintCamera != null)
            Destroy(paintCamera.gameObject);

        if (uvMaterial != null)
            Destroy(uvMaterial);
    }

}
