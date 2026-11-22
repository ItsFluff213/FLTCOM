using System;
using System.Collections.Generic;
using FLTCOM.Art;
using FLTCOM.Core.Telemetry;
using UnityEngine;
using Body = FLTCOM.Core.Protocol.Body;
using WireOrbit = FLTCOM.Core.Protocol.Orbit;

namespace FLTCOM.Snapshot
{
    internal static class BodyReader
    {
        public static List<Body> ReadAll(double ut, BodyTextures textures)
        {
            var bodies = new List<Body>(FlightGlobals.Bodies.Count);
            foreach (CelestialBody body in FlightGlobals.Bodies)
            {
                bodies.Add(Read(body, ut, textures));
            }
            return bodies;
        }

        public static WireOrbit WireOrbit(global::Orbit orbit, double ut) => OrbitFrame.AtTime(
            orbit.referenceBody.bodyName,
            orbit.referenceBody.gravParameter,
            orbit.semiMajorAxis,
            orbit.eccentricity,
            orbit.inclination,
            orbit.LAN,
            orbit.argumentOfPeriapsis,
            orbit.meanAnomalyAtEpoch,
            orbit.epoch,
            ut);

        /// <summary>The body's turn about its axis in radians: longitude 0 points this far from the orbit frame's x axis.</summary>
        public static double RotationAngle(CelestialBody body, double ut) => OrbitFrame.Radians(
            body.rotates && body.rotationPeriod > 0 ? body.initialRotation + 360 * ut / body.rotationPeriod : body.initialRotation);

        private static Body Read(CelestialBody body, double ut, BodyTextures textures)
        {
            bool isRoot = body.orbitDriver == null || body.orbit == null;
            return new Body(
                body.bodyName,
                isRoot ? null : WireOrbit(body.orbit!, ut),
                body.Radius,
                body.gravParameter,
                isRoot || double.IsInfinity(body.sphereOfInfluence) ? null : body.sphereOfInfluence,
                body.atmosphere ? body.atmosphereDepth : 0,
                body.rotationPeriod,
                RotationAngle(body, ut),
                body.pqsController != null ? Math.Max(0, body.pqsController.radiusMax - body.Radius) : 0,
                Hex(isRoot ? new Color(1f, 0.85f, 0.3f) : body.orbitDriver!.orbitColor),
                textures.PathOf(body));
        }

        private static string Hex(Color color) => "#" + ColorUtility.ToHtmlStringRGB(color).ToLowerInvariant();
    }
}
