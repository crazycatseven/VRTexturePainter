using UnityEngine;
using UnityEngine.Rendering; // for CommandBuffer

[RequireComponent(typeof(Camera))]
public class ProjectionPainter : MonoBehaviour
{
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

    // Store texture pixels
    private Color[] mainTexPixels;
    private Texture2D mainTex2D;
    private int texWidth, texHeight;

    // Material for UV shader
    private Material uvMaterial;

    public enum RenderTextureQuality
    {
        Low,
        Medium,
        High,
        Ultra
    }

    private void Start()
    {
        if (targetRenderer == null)
        {
            Debug.LogError("targetRenderer not assigned!");
            return;
        }

        // 1) Create UV material
        if (uvShader == null)
        {
            Debug.LogError("uvShader not assigned!");
            return;
        }
        uvMaterial = new Material(uvShader);

        // 2) Ensure texture is readable
        mainTex2D = targetRenderer.material.mainTexture as Texture2D;
        if (mainTex2D == null)
        {
            Debug.LogError("Material's main texture is not a Texture2D or is null!");
            return;
        }

        // Create writable texture copy
        mainTex2D = Instantiate(mainTex2D);
        targetRenderer.material.mainTexture = mainTex2D;
        texWidth = mainTex2D.width;
        texHeight = mainTex2D.height;
        mainTexPixels = mainTex2D.GetPixels();

        // 3) Setup RenderTexture
        if (uvRenderTexture == null)
        {
            int resolution = 512; // Default medium quality
            switch (uvRenderTextureQuality)
            {
                case RenderTextureQuality.Low:
                    resolution = 256;
                    break;
                case RenderTextureQuality.Medium:
                    resolution = 512;
                    break;
                case RenderTextureQuality.High:
                    resolution = 1024;
                    break;
                case RenderTextureQuality.Ultra:
                    resolution = 2048;
                    break;
            }
            uvRenderTexture = new RenderTexture(resolution, resolution, 0, RenderTextureFormat.ARGBFloat);
            uvRenderTexture.enableRandomWrite = true;
            uvRenderTexture.useMipMap = false;
            uvRenderTexture.filterMode = FilterMode.Bilinear;
            uvRenderTexture.wrapMode = TextureWrapMode.Clamp;
            uvRenderTexture.Create();
        }

        // Create texture for GPU readback
        readbackTexture = new Texture2D(
            uvRenderTexture.width,
            uvRenderTexture.height,
            TextureFormat.RGBAFloat,
            false,
            true  // Linear color space
        );
    }

    private void Update()
    {
        if (Input.GetMouseButton(0))
        {
            Paint();
        }
    }

    private void Paint()
    {
        Debug.Log("Paint Start");

        if (uvMaterial == null || mainCamera == null)
        {
            Debug.LogWarning("Material or Camera missing");
            return;
        }

        // --------------------- Core: CommandBuffer Rendering ----------------------
        // 1) Create and setup CommandBuffer
        CommandBuffer cmd = new CommandBuffer { name = "Render UV pass" };

        // Set render target and clear
        cmd.SetRenderTarget(uvRenderTexture);
        cmd.ClearRenderTarget(true, true, Color.clear);

        // Copy camera matrices for correct perspective
        cmd.SetViewProjectionMatrices(mainCamera.worldToCameraMatrix, mainCamera.projectionMatrix);

        // 2) Draw renderer with UV material
        cmd.DrawRenderer(targetRenderer, uvMaterial, 0, 0);

        // 3) Execute and release
        Graphics.ExecuteCommandBuffer(cmd);
        cmd.Release();
        // --------------------- Rendering Complete ----------------------

        // 4) Read back UV data
        RenderTexture.active = uvRenderTexture;
        readbackTexture.ReadPixels(new Rect(0, 0, uvRenderTexture.width, uvRenderTexture.height), 0, 0);
        readbackTexture.Apply();
        RenderTexture.active = null;

        // 5) Paint based on mouse position
        Vector2 mousePos = Input.mousePosition;
        float scaleX = (float)uvRenderTexture.width / Screen.width;
        float scaleY = (float)uvRenderTexture.height / Screen.height;
        Vector2 brushCenter = new Vector2(mousePos.x * scaleX, mousePos.y * scaleY);

        Color[] uvColors = readbackTexture.GetPixels();
        int rtW = uvRenderTexture.width;
        int rtH = uvRenderTexture.height;
        int paintCount = 0;

        for (int py = 0; py < rtH; py++)
        {
            for (int px = 0; px < rtW; px++)
            {
                float dist = Vector2.Distance(new Vector2(px, py), brushCenter);
                if (dist > brushRadius) continue;

                // Use bilinear sampling to get UV values
                const int sampleRadius = 2;
                float u = 0, v = 0;
                float totalWeight = 0;
                for (int offsetX = -sampleRadius; offsetX <= sampleRadius; offsetX++)

                {
                    for (int offsetY = -sampleRadius; offsetY <= sampleRadius; offsetY++)
                    {
                        int sampleX = Mathf.Clamp(px + offsetX, 0, rtW - 1);
                        int sampleY = Mathf.Clamp(py + offsetY, 0, rtH - 1);
                        int index = sampleY * rtW + sampleX;

                        // Use Gaussian weight
                        float distance = Mathf.Sqrt(offsetX * offsetX + offsetY * offsetY);
                        float weight = Mathf.Exp(-(distance * distance) / (2 * sampleRadius * sampleRadius));
                        totalWeight += weight;

                        u += uvColors[index].r * weight;
                        v += uvColors[index].g * weight;
                    }
                }

                u /= totalWeight;
                v /= totalWeight;


                // Get texture coordinates
                float texXf = u * texWidth;
                float texYf = v * texHeight;

                int radius = 2;
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

                        // Calculate smooth brush strength
                        float brushStrength = 1.0f - (dist / brushRadius);
                        brushStrength *= 1.0f - (paintDist / radius);
                        brushStrength = Mathf.SmoothStep(0, 1, brushStrength);

                        int texIdx = paintY * texWidth + paintX;
                        mainTexPixels[texIdx] = Color.Lerp(mainTexPixels[texIdx], brushColor, brushStrength);
                        paintCount++;
                    }
                }
            }
        }

        mainTex2D.SetPixels(mainTexPixels);
        mainTex2D.Apply();

        Debug.Log("Paint End, Painted " + paintCount + " pixels");

        // Debug UV sampling
        Debug.Log("UV Sample Debug:");
        for (int i = -2; i <= 2; i++) {
            for (int j = -2; j <= 2; j++) {
                int checkX = Mathf.RoundToInt(brushCenter.x) + i;
                int checkY = Mathf.RoundToInt(brushCenter.y) + j;
                if (checkX >= 0 && checkX < rtW && checkY >= 0 && checkY < rtH) {
                    int index = checkY * rtW + checkX;
                    float u = uvColors[index].r;
                    float v = uvColors[index].g;
                    Debug.Log($"UV at ({checkX}, {checkY}): ({u:F4}, {v:F4})");
                }
            }
        }
    }

    // Helper method for pixel painting
    private void PaintPixel(int x, int y, Color color, float strength)
    {
        if (x < 0 || x >= texWidth || y < 0 || y >= texHeight) return;
        
        int index = y * texWidth + x;
        mainTexPixels[index] = Color.Lerp(mainTexPixels[index], color, strength);
    }
}
