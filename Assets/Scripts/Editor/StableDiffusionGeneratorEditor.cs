using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Collections;


[CustomEditor(typeof(StableDiffusionImageGenerator))]
public class StableDiffusionImageGeneratorEditor : Editor


{
    private bool isRefreshing = false;
    private bool isGenerating = false;

    private void OnEnable()
    {
        // Reset states when editor is enabled
        isRefreshing = false;
        isGenerating = false;
    }

    public override void OnInspectorGUI()
    {
        StableDiffusionImageGenerator generator = (StableDiffusionImageGenerator)target;


        // Draw default inspector UI
        DrawDefaultInspector();



        EditorGUILayout.Space(10);

        using (new EditorGUI.DisabledScope(isRefreshing))
        {
            if (GUILayout.Button("Refresh Model List"))
            {
                isRefreshing = true;
                EditorCoroutineUtility.StartCoroutine(generator.RefreshModelList(), this);
                generator.OnRefreshComplete += OnRefreshComplete;
            }
        }

        // Model selection dropdown
        if (generator.ModelList != null && generator.ModelList.Count > 0)

        {
            EditorGUILayout.Space(5);
            EditorGUILayout.LabelField("Available Models:", EditorStyles.boldLabel);

            List<string> modelNames = new List<string>();
            int currentIndex = 0;
            
            for (int i = 0; i < generator.ModelList.Count; i++)
            {
                modelNames.Add(generator.ModelList[i].model_name);
                if (generator.ModelList[i].model_name == generator.SelectedModel)
                {
                    currentIndex = i;
                }
            }

            int newIndex = EditorGUILayout.Popup("Select Model", currentIndex, modelNames.ToArray());
            if (newIndex != currentIndex)
            {
                generator.SelectedModel = generator.ModelList[newIndex].model_name;
                EditorUtility.SetDirty(generator);
            }
        }

        EditorGUILayout.Space(10);

        using (new EditorGUI.DisabledScope(isGenerating))
        {
            if (GUILayout.Button("Generate Image"))
            {
                isGenerating = true;
                generator.OnGenerateComplete += OnGenerateComplete;
                generator.GenerateImage();
            }
        }

        EditorGUILayout.Space(5);
        EditorGUILayout.LabelField($"Refreshing: {isRefreshing}, Generating: {isGenerating}");
    }

    private void OnRefreshComplete()
    {
        isRefreshing = false;
        Debug.Log("模型列表刷新完成");
        Repaint();
        
        // Unsubscribe from event to prevent multiple subscriptions
        var generator = (StableDiffusionImageGenerator)target;
        generator.OnRefreshComplete -= OnRefreshComplete;

    }

    private void OnGenerateComplete(Sprite sprite)
    {
        isGenerating = false;
        Debug.Log("图像生成完成");
        Repaint();

        // Unsubscribe from event to prevent multiple subscriptions
        var generator = (StableDiffusionImageGenerator)target;
        generator.OnGenerateComplete -= OnGenerateComplete;

    }

    private void OnDestroy()
    {
        // Clean up event subscriptions when editor is destroyed
        if (target is StableDiffusionImageGenerator generator)
        {
            generator.OnRefreshComplete -= OnRefreshComplete;
            generator.OnGenerateComplete -= OnGenerateComplete;
        }
    }
} 