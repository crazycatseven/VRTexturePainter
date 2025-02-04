using UnityEngine;
using UnityEngine.UI;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.Networking;
using Newtonsoft.Json;

public class StableDiffusionImageGenerator : MonoBehaviour
{
    // Event declarations
    public event System.Action OnRefreshComplete;
    public event System.Action<Sprite> OnGenerateComplete;

    [Header("API Settings")]
    public string apiUrl = "http://127.0.0.1:7860";
    
    [Header("Generation Settings")]
    public string prompt = "a cartoon dog with a hat";
    public int steps = 25;
    public int width = 512;
    public int height = 512;
    [SerializeField] private string selectedModel;
    
    [Header("UI References")]
    public Image targetImage;

    // Store available model list
    [SerializeField] private List<SDModel> modelList = new List<SDModel>();

    [System.Serializable]
    public class SDModel
    {
        public string model_name;
        public string filename;
    }

    public void GenerateImage()
    {
        string txt2imgUrl = $"{apiUrl}/sdapi/v1/txt2img";

        var payload = new Dictionary<string, object>
        {
            { "prompt", prompt },
            { "steps", steps },
            { "width", width },
            { "height", height },
            { "model", selectedModel }
        };

        UnityWebRequest request = new UnityWebRequest(txt2imgUrl, "POST");
        byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(payload));
        request.uploadHandler = new UploadHandlerRaw(bodyRaw);
        request.downloadHandler = new DownloadHandlerBuffer();

        request.SetRequestHeader("Content-Type", "application/json");
        request.timeout = 300;

        var operation = request.SendWebRequest();
        operation.completed += (asyncOperation) =>
        {
            if (request.result == UnityWebRequest.Result.Success)
            {
                var response = JsonConvert.DeserializeObject<Dictionary<string, object>>(request.downloadHandler.text);
                string base64Image = (response["images"] as Newtonsoft.Json.Linq.JArray)[0].ToString();

                byte[] imageBytes = Convert.FromBase64String(base64Image);
                Texture2D tex = new Texture2D(2, 2);
                tex.LoadImage(imageBytes);

                Sprite sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), Vector2.one * 0.5f);
                
                if (targetImage != null)
                {
                    targetImage.sprite = sprite;
                }
                
                OnGenerateComplete?.Invoke(sprite);
            }
            else
            {
                Debug.LogError($"Image generation failed: {request.error}");
            }

            request.Dispose();
        };
    }

    public IEnumerator RefreshModelList()
    {
        string modelsUrl = $"{apiUrl}/sdapi/v1/sd-models";

        using (UnityWebRequest request = UnityWebRequest.Get(modelsUrl))
        {
            request.SetRequestHeader("Content-Type", "application/json");
            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                try 
                {
                    modelList = JsonConvert.DeserializeObject<List<SDModel>>(request.downloadHandler.text);
                    if (modelList.Count > 0 && string.IsNullOrEmpty(selectedModel))
                    {
                        selectedModel = modelList[0].model_name;
                    }
                }
                catch (Exception e)
                {
                    Debug.LogError($"Failed to parse model list: {e.Message}");
                }
            }
            else
            {
                Debug.LogError($"Failed to fetch model list: {request.error}");
            }
        }

        OnRefreshComplete?.Invoke();
    }

    public string SelectedModel
    {
        get => selectedModel;
        set => selectedModel = value;
    }

    public List<SDModel> ModelList => modelList;
}