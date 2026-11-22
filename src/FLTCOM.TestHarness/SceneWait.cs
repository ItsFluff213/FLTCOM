using System;
using System.Collections;
using UnityEngine;

namespace FLTCOM.TestHarness
{
    internal static class SceneWait
    {
        private const float TimeoutSeconds = 240;
        private const float UnpackSeconds = 20;
        private const float SettleSeconds = 2;

        public static IEnumerator Until(GameScenes scene, int loadsBefore, Func<int> sceneLoads, Func<bool>? extra)
        {
            var deadline = Time.realtimeSinceStartup + TimeoutSeconds;
            while (sceneLoads() <= loadsBefore || HighLogic.LoadedScene != scene || !SceneReady(scene) || (extra != null && !extra()))
            {
                if (Time.realtimeSinceStartup > deadline)
                {
                    throw new TimeoutException("scene " + scene + " was not ready after " + TimeoutSeconds + " s, the game is in " + HighLogic.LoadedScene);
                }
                yield return null;
            }
            if (scene == GameScenes.FLIGHT)
            {
                var unpackDeadline = Time.realtimeSinceStartup + UnpackSeconds;
                while (FlightGlobals.ActiveVessel != null && FlightGlobals.ActiveVessel.packed && Time.realtimeSinceStartup < unpackDeadline)
                {
                    yield return null;
                }
            }
            var settle = Time.realtimeSinceStartup + SettleSeconds;
            while (Time.realtimeSinceStartup < settle)
            {
                yield return null;
            }
        }

        private static bool SceneReady(GameScenes scene)
        {
            if (scene != GameScenes.FLIGHT)
            {
                return true;
            }
            return FlightGlobals.ready && FlightGlobals.ActiveVessel != null;
        }
    }
}
