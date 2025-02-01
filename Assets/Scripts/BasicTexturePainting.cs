using UnityEngine;
using System.Collections.Generic;

public class BasicTexturePainting : MonoBehaviour
{
    public Color paintColor = Color.red;         // Color to paint
    public float brushWorldRadius = 0.1f;  // Brush radius in world space
    public int samplesPerAxis = 32;        // Increase sampling precision
    public float projectionDistance = 0.1f; // Projection distance
    
    // Specify the object to draw on (Tool script attached to another GameObject, not the object being drawn)
    public GameObject targetObject;


    private Texture2D textureCopy;                 // Cloneable writable texture
    private Renderer objectRenderer;
    private Mesh targetMesh;


    void Start()
    {
        if (targetObject == null)
        {
            Debug.LogError("No target object specified!");
            return;
        }

        objectRenderer = targetObject.GetComponent<Renderer>();
        if (objectRenderer == null)
        {
            Debug.LogError("The target object does not have a Renderer component!");
            return;
        }

        Texture2D mainTex = objectRenderer.material.mainTexture as Texture2D;
        if (mainTex != null)
        {
            textureCopy = new Texture2D(mainTex.width, mainTex.height, TextureFormat.RGBA32, false);
            textureCopy.SetPixels(mainTex.GetPixels());
            textureCopy.Apply();
            objectRenderer.material.mainTexture = textureCopy;
        }
        else
        {
            Debug.LogError("The material of the specified object does not have a Texture2D type main texture!");
        }

        if (targetObject != null)
        {
            MeshFilter meshFilter = targetObject.GetComponent<MeshFilter>();
            if (meshFilter != null)
            {
                targetMesh = meshFilter.sharedMesh;
            }
        }
    }

    void Update()
    {
        if (Input.GetMouseButton(0))
        {
            Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            RaycastHit hit;

            if (Physics.Raycast(ray, out hit) && hit.collider.gameObject == targetObject)
            {
                PaintAtPoint(hit.point, hit.normal);
            }
        }
    }

    void PaintAtPoint(Vector3 hitPoint, Vector3 normal)
    {
        if (textureCopy == null) return;

        // Create a brush coordinate system
        Vector3 tangent = Vector3.Cross(normal, Vector3.up).normalized;
        if (tangent.magnitude < 0.001f)

            tangent = Vector3.Cross(normal, Vector3.right).normalized;
        Vector3 bitangent = Vector3.Cross(normal, tangent);

        float stepSize = (brushWorldRadius * 2) / samplesPerAxis;

        // Create a temporary color buffer
        Color[] colorBuffer = new Color[textureCopy.width * textureCopy.height];
        colorBuffer = textureCopy.GetPixels();


        // Sample in spherical space
        for (int x = 0; x < samplesPerAxis; x++)

        {
            for (int y = 0; y < samplesPerAxis; y++)
            {
                float offsetX = -brushWorldRadius + x * stepSize;
                float offsetY = -brushWorldRadius + y * stepSize;

                // Sample in spherical space
                for (float angle = 0; angle < 360; angle += 30)

                {
                    // Calculate the rotated normal
                    Quaternion rotation = Quaternion.AngleAxis(angle, normal);
                    Vector3 rotatedTangent = rotation * tangent;
                    Vector3 rotatedBitangent = rotation * bitangent;


                    // Calculate the sample point
                    Vector3 samplePoint = hitPoint + (rotatedTangent * offsetX) + (rotatedBitangent * offsetY);


                    // Check if it's within the brush radius
                    if (Vector3.Distance(samplePoint, hitPoint) > brushWorldRadius)
                        continue;


                    // Shoot rays in all directions from the sample point
                    for (float phi = -90; phi <= 90; phi += 45)

                    {
                        Quaternion phiRotation = Quaternion.AngleAxis(phi, rotatedBitangent);
                        Vector3 rayDirection = phiRotation * -normal;
                        Ray sampleRay = new Ray(samplePoint + normal * projectionDistance, rayDirection);
                        RaycastHit[] hits = Physics.RaycastAll(sampleRay, projectionDistance * 2);

                        foreach (RaycastHit sampleHit in hits)
                        {
                            if (sampleHit.collider.gameObject != targetObject)
                                continue;

                            // Get UV coordinates
                            Vector2 uv = sampleHit.textureCoord;
                            

                            // Use bilinear interpolation to calculate the exact texture coordinates
                            float exactX = uv.x * textureCopy.width;
                            float exactY = uv.y * textureCopy.height;


                            // Calculate the four surrounding pixels
                            int x1 = Mathf.FloorToInt(exactX);
                            int y1 = Mathf.FloorToInt(exactY);
                            int x2 = x1 + 1;
                            int y2 = y1 + 1;


                            // Ensure coordinates are within valid range
                            x1 = Mathf.Clamp(x1, 0, textureCopy.width - 1);
                            x2 = Mathf.Clamp(x2, 0, textureCopy.width - 1);
                            y1 = Mathf.Clamp(y1, 0, textureCopy.height - 1);

                            y2 = Mathf.Clamp(y2, 0, textureCopy.height - 1);

                            // Calculate weights
                            float weightX = exactX - x1;
                            float weightY = exactY - y1;


                            // Calculate attenuation
                            float distance = Vector3.Distance(samplePoint, hitPoint);
                            float opacity = 1 - (distance / brushWorldRadius);
                            opacity = Mathf.Clamp01(opacity);


                            // Mix colors of the four adjacent pixels
                            int[] pixels = new int[] {
                                y1 * textureCopy.width + x1,
                                y1 * textureCopy.width + x2,
                                y2 * textureCopy.width + x1,
                                y2 * textureCopy.width + x2

                            };

                            float[] weights = new float[] {
                                (1 - weightX) * (1 - weightY),
                                weightX * (1 - weightY),
                                (1 - weightX) * weightY,
                                weightX * weightY
                            };

                            for (int i = 0; i < 4; i++)
                            {
                                if (pixels[i] >= 0 && pixels[i] < colorBuffer.Length)
                                {
                                    colorBuffer[pixels[i]] = Color.Lerp(colorBuffer[pixels[i]], paintColor, opacity * weights[i]);
                                }
                            }
                        }
                    }
                }
            }
        }

        // Apply the color buffer
        textureCopy.SetPixels(colorBuffer);
        textureCopy.Apply();
    }
}
