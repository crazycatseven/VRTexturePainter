using UnityEngine;
using UnityEngine.Rendering; // for CommandBuffer

[RequireComponent(typeof(Camera))]
public class ProjectionPainter : MonoBehaviour
{

    [Header("Painter Camera")]
    private Camera paintCamera;              // Camera used for UV sampling
    public float paintCameraDistance = 1f;


    [Header("References")]
    public Shader uvShader;                  // UV visualization shader
    public Renderer targetRenderer;          // Target object to paint on
    public Camera mainCamera;                // For view/projection matrices

    [Header("Texture / RenderTexture Settings")]
    public RenderTextureQuality uvRenderTextureQuality = RenderTextureQuality.Medium;
    private RenderTexture uvRenderTexture;
    private Texture2D readbackTexture;

    [Header("Brush Settings")]
    public Color brushColor = Color.red;
    [Range(1, 100)]
    public float brushRadius = 10;
    public float worldSpaceRadius = 0.1f;


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
    }

    private bool ValidateReferences()
    {
        if (targetRenderer == null)
        {
            Debug.LogError("targetRenderer not assigned!");
            return false;
        }
        if (uvShader == null)
        {
            Debug.LogError("uvShader not assigned!");
            return false;
        }
        if (mainCamera == null)
        {
            Debug.LogError("mainCamera not assigned!");
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

    private void Update()

    {
        if (Input.GetMouseButton(0))
        {
            Paint();
        }
        else{
            UpdatePaintCameraPosition(Input.mousePosition);
        }
    }

    // ================================ PAINT CAMERA ================================

    private void SetupPaintCamera()
    {
        GameObject paintCamObj = new GameObject("Paint Camera");
        paintCamera = paintCamObj.AddComponent<Camera>();
        paintCamera.enabled= false;
        paintCamera.clearFlags = CameraClearFlags.SolidColor;
        paintCamera.backgroundColor = Color.clear;
        paintCamera.cullingMask = 1 << targetRenderer.gameObject.layer;
        paintCamera.nearClipPlane = 0.01f;
    }

    private void UpdatePaintCameraPosition(Vector2 mousePos)
    {
        // Cast a ray from the main camera
        Ray ray = mainCamera.ScreenPointToRay(mousePos);
        RaycastHit hit;

        if (Physics.Raycast(ray, out hit, Mathf.Infinity, 1 << targetRenderer.gameObject.layer))
        {
            Vector3 paintPos = hit.point + hit.normal * paintCameraDistance;
            paintCamera.transform.position = paintPos;
            paintCamera.transform.LookAt(hit.point, Vector3.up);

            float worldSpaceBrushSize = brushRadius * 0.01f;
            float halfFOV = Mathf.Atan2(worldSpaceBrushSize, paintCameraDistance);
            paintCamera.fieldOfView = halfFOV * 2.0f * Mathf.Rad2Deg;
            paintCamera.nearClipPlane = paintCameraDistance * 0.5f;
        }
    }


    // ================================  PAINT CORE  ================================

    private void Paint()
    {
        if (paintCamera == null || uvMaterial == null) return;

        Vector3 mousePos = Input.mousePosition;
        UpdatePaintCameraPosition(mousePos);

        RenderUVPass();
        ReadbackUVData();
        ApplyPaint(mousePos);
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
    }

    private void ReadbackUVData()
    {
        RenderTexture.active = uvRenderTexture;
        readbackTexture.ReadPixels(new Rect(0, 0, uvRenderTexture.width, uvRenderTexture.height), 0, 0);
        readbackTexture.Apply();
        RenderTexture.active = null;
    }

    private void ApplyPaint(Vector3 mousePos)
    {
        Vector2 brushCenter = GetBrushCenterInRT(mousePos);
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
                if (dist > brushRadius) continue;

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

                float brushStrength = 1.0f - (dist / brushRadius);
                brushStrength *= 1.0f - (paintDist / radius);
                brushStrength = Mathf.SmoothStep(0, 1, brushStrength);

                int texIdx = paintY * texWidth + paintX;
                mainTexPixels[texIdx] = Color.Lerp(mainTexPixels[texIdx], brushColor, brushStrength);
                paintCount++;
            }
        }
    }

    private Vector2 GetBrushCenterInRT(Vector3 screenPos)
    {
        Vector2 viewportPoint = paintCamera.ScreenToViewportPoint(screenPos);
        return new Vector2(
            viewportPoint.x * uvRenderTexture.width,
            viewportPoint.y * uvRenderTexture.height
        );
    }

    private void OnDrawGizmos()
    {
        if (!Application.isPlaying || paintCamera == null) return;

        // Draw paint camera frustum
        Gizmos.color = Color.yellow;
        Matrix4x4 matrix = Matrix4x4.TRS(
            paintCamera.transform.position,
            paintCamera.transform.rotation,
            Vector3.one
        );
        Gizmos.matrix = matrix;
        Gizmos.DrawFrustum(
            Vector3.zero,
            paintCamera.fieldOfView,
            paintCameraDistance * 2,
            0.01f,
            paintCamera.aspect
        );

        // Draw brush area
        Ray ray = mainCamera.ScreenPointToRay(Input.mousePosition);
        if (Physics.Raycast(ray, out RaycastHit hit, 1000f, 1 << targetRenderer.gameObject.layer))
        {
            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(hit.point, brushRadius * 0.01f);
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
