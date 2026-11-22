using System;
using System.Collections.Generic;
using System.Linq;
using FLTCOM.Core.Landing;
using FLTCOM.Guidance;
using UnityEngine;

namespace FLTCOM.Execution
{
    /// <summary>
    /// Reads what the landing planner needs from a loaded vessel and its body: drag area from the stock drag cubes,
    /// parachute area from their deployed cubes, a density table sampled from the stock atmosphere, and where the body
    /// has turned. Keeps the latest predicted touchdown per operation for the display.
    /// </summary>
    internal static class LandingReader
    {
        private const int DensitySamples = 71;
        private const float DescentMach = 0.5f;

        private static readonly Dictionary<string, LandingPrediction> Touchdowns = new Dictionary<string, LandingPrediction>();

        public static LandingPrediction? Touchdown(string operationId) => Touchdowns.TryGetValue(operationId, out LandingPrediction p) ? p : null;

        private static readonly Dictionary<string, DescentReading> Descents = new Dictionary<string, DescentReading>();

        public static DescentReading? Descent(string operationId) => Descents.TryGetValue(operationId, out DescentReading d) ? d : null;

        public static void Predicted(string operationId, LandingPrediction? prediction)
        {
            if (prediction != null && prediction.Landed)
            {
                Touchdowns[operationId] = prediction;
            }
        }

        public static void Descending(string operationId, LandingGuidance descent)
        {
            Predicted(operationId, descent.Prediction);
            if (descent.Reading is DescentReading reading)
            {
                Descents[operationId] = reading;
            }
        }

        /// <summary>The landing data of a loaded vessel; null for an unloaded one, whose drag cubes are not built.</summary>
        public static LandingContext? Context(Vessel vessel, LandingTarget target)
        {
            if (!vessel.loaded)
            {
                return null;
            }
            CelestialBody body = vessel.mainBody;
            double now = Planetarium.GetUniversalTime();
            LandingBody landingBody = Body(body, now);
            double dragArea = DragArea(vessel);
            (double chuteArea, double deployAltitude) = Chutes(vessel);
            double targetAltitude = body.TerrainAltitude(target.Latitude * UtilMath.Rad2Deg, target.Longitude * UtilMath.Rad2Deg, true);
            if (body.ocean)
            {
                targetAltitude = Math.Max(0, targetAltitude);
            }
            double frameLongitude = landingBody.Longitude(InertialFrame.Position(vessel), now) * UtilMath.Rad2Deg;
            PluginFiles.Log("landing " + vessel.vesselName + ": drag area " + dragArea.ToString("0.00") + " m2, chutes " + chuteArea.ToString("0") + " m2 open at " + deployAltitude.ToString("0") + " m, target altitude " + targetAltitude.ToString("0") + " m; frame longitude " + frameLongitude.ToString("0.000") + " vs game " + vessel.longitude.ToString("0.000"));
            return new LandingContext(landingBody, vessel.totalMass * 1000, dragArea, chuteArea, deployAltitude, targetAltitude, body.isHomeWorld);
        }

        public static LandingBody Body(CelestialBody body, double ut)
        {
            Core.TwoBody.Vector3d zero = InertialFrame.ToCore(body.GetWorldSurfacePosition(0, 0, 0) - body.position);
            double meridian = Math.Atan2(zero.Y, zero.X);
            Atmosphere? atmosphere = null;
            if (body.atmosphere && body.atmosphereDepth > 0)
            {
                var density = new double[DensitySamples];
                for (int i = 0; i < DensitySamples; i++)
                {
                    double altitude = body.atmosphereDepth * i / (DensitySamples - 1);
                    density[i] = body.GetDensity(body.GetPressure(altitude), body.GetTemperature(altitude));
                }
                atmosphere = new Atmosphere(body.atmosphereDepth, density);
            }
            return new LandingBody(VesselTasks.Body(body), body.rotationPeriod, meridian, ut, atmosphere);
        }

        /// <summary>
        /// Drag area times drag coefficient, m², with the air coming from below as it does on a tail-first descent.
        /// Stock drag is q · AreaDrag · DragCubeMultiplier · DragMultiplier, so this is the area the core's ½ρv²·A needs.
        /// </summary>
        private static double DragArea(Vessel vessel)
        {
            Vector3 flowFrom = vessel.transform.up;
            double sum = 0;
            foreach (Part part in vessel.parts)
            {
                if (part.DragCubes == null || part.ShieldedFromAirstream)
                {
                    continue;
                }
                Vector3 local = part.partTransform.InverseTransformDirection(flowFrom);
                part.DragCubes.SetDrag(local, DescentMach);
                sum += part.DragCubes.AreaDrag;
            }
            return sum * PhysicsGlobals.DragCubeMultiplier * PhysicsGlobals.DragMultiplier;
        }

        /// <summary>Total open-canopy drag area of the stowed or armed parachutes, and the lowest height they open at.</summary>
        private static (double Area, double DeployAltitude) Chutes(Vessel vessel)
        {
            double area = 0;
            double deploy = double.PositiveInfinity;
            foreach (Part part in vessel.parts)
            {
                ModuleParachute? chute = part.FindModuleImplementing<ModuleParachute>();
                if (chute == null || chute.deploymentState == ModuleParachute.deploymentStates.CUT || part.DragCubes == null)
                {
                    continue;
                }
                DragCube? open = part.DragCubes.Cubes.FirstOrDefault(c => c.Name == "DEPLOYED");
                if (open == null)
                {
                    continue;
                }
                double best = 0;
                for (int face = 0; face < 6; face++)
                {
                    best = Math.Max(best, open.Area[face] * open.Drag[face]);
                }
                area += best;
                deploy = Math.Min(deploy, chute.deployAltitude);
            }
            return (area * PhysicsGlobals.DragCubeMultiplier * PhysicsGlobals.DragMultiplier, double.IsInfinity(deploy) ? 1000 : deploy);
        }
    }
}
