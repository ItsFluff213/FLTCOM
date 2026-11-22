using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using FLTCOM.Server;
using UnityEngine;

namespace FLTCOM.Art
{
    /// <summary>
    /// Copies each body's scaled-space surface texture out of the running game, one body per call, and serves it as a
    /// PNG. The texture is drawn into a render target first, so a texture the game keeps unreadable still copies.
    /// </summary>
    internal sealed class BodyTextures
    {
        private const int Width = 2048;
        private const int Height = 1024;

        private readonly Dictionary<string, string?> _paths = new Dictionary<string, string?>(StringComparer.Ordinal);

        /// <summary>The served path of the body's texture, or null while it is not captured or could not be.</summary>
        public string? PathOf(CelestialBody body) => _paths.TryGetValue(body.bodyName, out string? path) ? path : null;

        public void CaptureNext(HttpServer server)
        {
            foreach (CelestialBody body in FlightGlobals.Bodies)
            {
                if (_paths.ContainsKey(body.bodyName))
                {
                    continue;
                }
                _paths[body.bodyName] = null;
                byte[]? png = Capture(body);
                if (png == null)
                {
                    PluginFiles.Log("no surface texture for " + body.bodyName);
                    return;
                }
                string path = "/art/body/" + Regex.Replace(body.bodyName, "[^A-Za-z0-9]", "_") + ".png";
                server.PublishArt(path, "image/png", png);
                _paths[body.bodyName] = path;
                return;
            }
        }

        private static byte[]? Capture(CelestialBody body)
        {
            Renderer? renderer = body.scaledBody != null ? body.scaledBody.GetComponent<Renderer>() : null;
            Texture? source = renderer != null && renderer.sharedMaterial != null ? renderer.sharedMaterial.mainTexture : null;
            if (source == null || body.orbitDriver == null)
            {
                return null;
            }
            RenderTexture target = RenderTexture.GetTemporary(Width, Height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            RenderTexture previous = RenderTexture.active;
            var copy = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            try
            {
                Graphics.Blit(source, target);
                RenderTexture.active = target;
                copy.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
                copy.Apply();
                return ImageConversion.EncodeToPNG(copy);
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(target);
                UnityEngine.Object.Destroy(copy);
            }
        }
    }
}
