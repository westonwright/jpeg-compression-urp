using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
public static class RendererFeatureFunctions
{
    public static bool CreateMaterial(Shader shader, ref Material material)
    {
        if (shader == null)
        {
            Debug.LogError("Shader Invalid!");
            return false;
        }
        else
        {
            material = new Material(shader);
            material.hideFlags = HideFlags.HideAndDontSave;
            return true;
        }
    }

    public static void DisposeMaterial(ref Material material)
    {
        if (material != null)
        {
            CoreUtils.Destroy(material);
            material = null;
        }
    }

    public static bool ValidUniversalPipeline(RenderPipelineAsset pipeline, bool opaque, bool depth)
    {
        UniversalRenderPipelineAsset universal = (UniversalRenderPipelineAsset)pipeline;
        if (universal == null)
        {
            Debug.LogError("Not using proper Universal Render Pipeline Asset, check project settings!");
            return false;
        }
        if (opaque)
        {
            if (!universal)
            {
                Debug.LogError("Camera Opaque Texture not enabled on Universal Render Pipeline Asset!", universal);
                return false;
            }
        }
        if (depth)
        {
            if (!universal)
            {
                Debug.LogError("Camera Depth Texture not enabled on Universal Render Pipeline Asset!", universal);
                return false;
            }
        }
        return true;
    }

    public static bool LoadShader(ref Shader shaderRefrence, string filePath, string fileName)
    {
        shaderRefrence = Resources.Load<Shader>(filePath + fileName);
        if (shaderRefrence == null)
        {
            Debug.LogError("Missing Shader " + fileName + "! Package may be corrupted!");
            Debug.LogError("Please reimport Package");
            return false;
        }
        return true;
    }
}
