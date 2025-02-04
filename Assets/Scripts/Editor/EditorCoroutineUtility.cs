using System.Collections;
using UnityEditor;

public static class EditorCoroutineUtility
{
    public static void StartCoroutine(IEnumerator routine, Editor editor)
    {
        EditorApplication.update += Update;
        
        void Update()
        {
            if (!routine.MoveNext())
            {
                EditorApplication.update -= Update;
                if (editor is StableDiffusionImageGeneratorEditor sdEditor)
                {
                    sdEditor.Repaint();
                }

            }
        }
    }
}
