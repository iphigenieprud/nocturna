using System.IO;
using UnityEditor;
using UnityEngine;

// Right-click a texture in the Project window > "Make Blue Parts White".
// Makes a copy of the texture where blue/purple parts (like flower petals) become white,
// and everything else (green leaves, stems) stays the same. The original isn't touched.
public static class MakeBluePartsWhite
{
    [MenuItem("Assets/Make Blue Parts White", true)]
    static bool CanRun()
    {
        return Selection.activeObject is Texture2D;
    }

    [MenuItem("Assets/Make Blue Parts White")]
    static void Run()
    {
        Texture2D src = (Texture2D)Selection.activeObject;
        string path = AssetDatabase.GetAssetPath(src);

        // Read the pixels (works even if the texture isn't marked readable)
        RenderTexture rt = RenderTexture.GetTemporary(src.width, src.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        Graphics.Blit(src, rt);
        RenderTexture previous = RenderTexture.active;
        RenderTexture.active = rt;
        Texture2D tex = new Texture2D(src.width, src.height, TextureFormat.RGBA32, false);
        tex.ReadPixels(new Rect(0, 0, src.width, src.height), 0, 0);
        tex.Apply();
        RenderTexture.active = previous;
        RenderTexture.ReleaseTemporary(rt);

        // Blue and purple pixels become soft white; everything else stays
        Color[] px = tex.GetPixels();
        int changed = 0;
        for (int i = 0; i < px.Length; i++)
        {
            Color c = px[i];
            float h, s, v;
            Color.RGBToHSV(c, out h, out s, out v);

            bool bluish = h > 0.5f && h < 0.9f; // cyan through blue to purple
            if (!bluish || s < 0.12f) continue;

            float amount = Mathf.InverseLerp(0.12f, 0.35f, s); // smooth edge between petal and leaf
            Color white = Color.HSVToRGB(h, 0.06f, Mathf.Max(v, 0.9f)); // a hint of the old tint keeps it natural
            Color result = Color.Lerp(c, white, amount);
            result.a = c.a;
            px[i] = result;
            changed++;
        }
        tex.SetPixels(px);
        tex.Apply();

        // Save the copy next to the original
        string dir = Path.GetDirectoryName(path);
        string newPath = Path.Combine(dir, Path.GetFileNameWithoutExtension(path) + "_White.png").Replace("\\", "/");
        File.WriteAllBytes(newPath, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(newPath);

        // Same import settings as the original (transparency, type)
        TextureImporter from = AssetImporter.GetAtPath(path) as TextureImporter;
        TextureImporter to = AssetImporter.GetAtPath(newPath) as TextureImporter;
        if (from != null && to != null)
        {
            to.textureType = from.textureType;
            to.alphaIsTransparency = from.alphaIsTransparency;
            to.alphaSource = from.alphaSource;
            to.mipmapEnabled = from.mipmapEnabled;
            to.wrapMode = from.wrapMode;
            to.SaveAndReimport();
        }

        Object created = AssetDatabase.LoadAssetAtPath<Texture2D>(newPath);
        EditorGUIUtility.PingObject(created);
        Selection.activeObject = created;
        Debug.Log("Made '" + Path.GetFileName(newPath) + "' (" + changed + " pixels turned white). Put it in your flower material's Base Map.");
    }
}