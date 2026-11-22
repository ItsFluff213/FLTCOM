using KSP.UI.Screens;
using UnityEngine;

namespace FLTCOM.Ui
{
    /// <summary>
    /// The stock toolbar button. A click opens the display in the default browser, or, when the server could not
    /// start, says why on screen.
    /// </summary>
    internal sealed class ToolbarButton
    {
        private const ApplicationLauncher.AppScenes Scenes =
            ApplicationLauncher.AppScenes.SPACECENTER | ApplicationLauncher.AppScenes.FLIGHT | ApplicationLauncher.AppScenes.MAPVIEW | ApplicationLauncher.AppScenes.TRACKSTATION;

        private ApplicationLauncherButton? _button;
        private string? _url;
        private string _error = "FLTCOM is starting";

        public void Serve(string url) => _url = url;

        public void Fail(string error)
        {
            _url = null;
            _error = error;
        }

        public void Attach()
        {
            GameEvents.onGUIApplicationLauncherReady.Add(Add);
            if (ApplicationLauncher.Ready)
            {
                Add();
            }
        }

        public void Detach()
        {
            GameEvents.onGUIApplicationLauncherReady.Remove(Add);
            if (_button != null && ApplicationLauncher.Instance != null)
            {
                ApplicationLauncher.Instance.RemoveModApplication(_button);
            }
            _button = null;
        }

        private void Add()
        {
            if (_button != null || ApplicationLauncher.Instance == null)
            {
                return;
            }
            _button = ApplicationLauncher.Instance.AddModApplication(Click, Click, null, null, null, null, Scenes, Icon());
        }

        private void Click()
        {
            _button?.SetFalse(false);
            if (_url != null)
            {
                PluginFiles.Log("toolbar opens " + _url);
                Application.OpenURL(_url);
            }
            else
            {
                ScreenMessages.PostScreenMessage("FLTCOM: " + _error, 6f, ScreenMessageStyle.UPPER_CENTER);
            }
        }

        private static Texture2D Icon()
        {
            const int size = 38;
            var icon = new Texture2D(size, size, TextureFormat.ARGB32, false);
            var amber = new Color(1f, 0.69f, 0.2f, 1f);
            var clear = new Color(0, 0, 0, 0);
            float centre = (size - 1) / 2f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float r = Mathf.Sqrt((x - centre) * (x - centre) + (y - centre) * (y - centre));
                    bool ring = Mathf.Abs(r - 15f) < 1.5f || Mathf.Abs(r - 8f) < 1.2f || r < 2.5f;
                    bool cross = (Mathf.Abs(x - centre) < 0.8f || Mathf.Abs(y - centre) < 0.8f) && r < 17f;
                    icon.SetPixel(x, y, ring || cross ? amber : clear);
                }
            }
            icon.Apply();
            return icon;
        }
    }
}
