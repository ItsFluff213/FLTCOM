using System;
using FLTCOM.Core.Landing;
using FLTCOM.Core.Planning;
using FLTCOM.Core.Protocol;
using FLTCOM.Core.TwoBody;
using EndOfLife = FLTCOM.Core.Planning.EndOfLife;
using LandingTarget = FLTCOM.Core.Landing.LandingTarget;

namespace FLTCOM.Core.Missions
{
    /// <summary>Saves and restores missions with their state and burn progress, so an executing step survives a scene reload.</summary>
    public static class MissionJson
    {
        public static string Write(Mission mission)
        {
            var w = new JsonWriter();
            w.BeginObject();
            w.Name("id"); w.String(mission.Id);
            w.Name("name"); w.String(mission.Name);
            w.Name("operations"); w.BeginArray();
            foreach (Operation op in mission.Operations)
            {
                WriteOperation(w, op);
            }
            w.EndArray();
            w.EndObject();
            return w.ToString();
        }

        public static Mission Read(string json)
        {
            JsonNode root = JsonNode.Parse(json ?? throw new ArgumentNullException(nameof(json)));
            return new Mission(
                root.Field("$", "id").AsString("$.id"),
                root.Field("$", "name").AsString("$.name"),
                root.Field("$", "operations").AsArray("$.operations", (node, i) => ReadOperation(node, "$.operations[" + i + "]")));
        }

        private static void WriteOperation(JsonWriter w, Operation op)
        {
            w.BeginObject();
            w.Name("id"); w.String(op.Id);
            w.Name("codename"); w.String(op.Codename);
            w.Name("change"); w.String(op.Change.Kind.ToString());
            w.Name("target"); w.Number(op.Change.Target);
            if (!op.Change.AutoSystems)
            {
                w.Name("autoSystems"); w.Bool(false);
            }
            if (op.Change.Destination != null)
            {
                w.Name("destination"); w.String(op.Change.Destination);
                if (op.Change.ArrivalInclination is double inclination)
                {
                    w.Name("arrivalInclination"); w.Number(inclination);
                }
                if (op.Change.ThenLand)
                {
                    w.Name("thenLand"); w.Bool(true);
                }
            }
            if (op.Change.Ring is { IsEquatorial: false } ring)
            {
                w.Name("ring"); w.BeginArray(); w.Number(ring.Inclination); w.Number(ring.AscendingNode); w.EndArray();
            }
            if (op.Change.EndOfLife != EndOfLife.Auto)
            {
                w.Name("endOfLife"); w.String(op.Change.EndOfLife.ToString());
            }
            if (op.Change.LaunchTarget is LaunchTarget launch)
            {
                w.Name("launch"); w.BeginArray();
                foreach (double? x in new[] { launch.Apoapsis, launch.Periapsis, launch.Inclination, launch.Lan })
                {
                    if (x is double value)
                    {
                        w.Number(value);
                    }
                    else
                    {
                        w.Null();
                    }
                }
                w.EndArray();
            }
            if (op.Change.Rendezvous is RendezvousTarget meet)
            {
                w.Name("rendezvous"); w.BeginObject();
                w.Name("vessel"); w.String(meet.VesselId);
                w.Name("name"); w.String(meet.Name);
                w.Name("port");
                if (meet.PortId is uint port)
                {
                    w.Number((long)port);
                }
                else
                {
                    w.Null();
                }
                w.EndObject();
            }
            if (op.Change.Landing is LandingTarget landing)
            {
                w.Name("landing"); w.BeginObject();
                w.Name("site"); w.String(landing.Site.ToString());
                w.Name("latitude"); w.Number(landing.Latitude);
                w.Name("longitude"); w.Number(landing.Longitude);
                w.EndObject();
            }
            w.Name("state"); w.String(op.State.ToString());
            w.Name("vessels"); w.BeginArray();
            foreach (string vessel in op.VesselIds)
            {
                w.String(vessel);
            }
            w.EndArray();
            w.Name("burns"); w.BeginArray();
            foreach (PlannedBurn b in op.Burns)
            {
                w.BeginObject();
                w.Name("vessel"); w.String(b.VesselId);
                w.Name("label"); w.String(b.Label);
                w.Name("nodeUt"); w.Number(b.NodeUt);
                w.Name("startUt"); w.Number(b.StartUt);
                w.Name("duration"); w.Number(b.Duration);
                w.Name("prograde"); w.Number(b.Prograde);
                w.Name("normal"); w.Number(b.Normal);
                w.Name("radial"); w.Number(b.Radial);
                w.Name("pinnedStage");
                if (b.PinnedStage is int stage)
                {
                    w.Number(stage);
                }
                else
                {
                    w.Null();
                }
                if (b.OrbitAfter is OrbitElements after)
                {
                    w.Name("after");
                    w.BeginArray();
                    foreach (double x in new[] { after.Mu, after.SemiMajorAxis, after.Eccentricity, after.Inclination, after.LongitudeOfAscendingNode, after.ArgumentOfPeriapsis, after.MeanAnomalyAtEpoch, after.Epoch })
                    {
                        w.Number(x);
                    }
                    w.EndArray();
                }
                if (b.Body != null)
                {
                    w.Name("body"); w.String(b.Body);
                }
                if (b.ReleasePart is uint part)
                {
                    w.Name("releasePart"); w.Number(part);
                }
                w.EndObject();
            }
            w.EndArray();
            w.Name("progress");
            if (op.Progress is BurnProgress p)
            {
                w.BeginObject();
                w.Name("burnIndex"); w.Number(p.BurnIndex);
                w.Name("deltaVDone"); w.Number(p.DeltaVDone);
                w.EndObject();
            }
            else
            {
                w.Null();
            }
            w.EndObject();
        }

        private static Operation ReadOperation(JsonNode node, string path)
        {
            string kindText = node.Field(path, "change").AsString(path + ".change");
            if (!Enum.TryParse(kindText, out OrbitChangeKind kind) || !Enum.IsDefined(typeof(OrbitChangeKind), kind))
            {
                throw new ProtocolException(path + ".change: unknown orbit change " + kindText);
            }
            JsonNode progress = node.Field(path, "progress");
            return new Operation(
                node.Field(path, "id").AsString(path + ".id"),
                node.Field(path, "codename").AsString(path + ".codename"),
                ReadChange(node, path, kind).WithAutoSystems(node.TryField(path, "autoSystems")?.AsBool(path + ".autoSystems") ?? true),
                node.Field(path, "vessels").AsArray(path + ".vessels", (v, i) => v.AsString(path + ".vessels[" + i + "]")),
                node.Field(path, "burns").AsArray(path + ".burns", (b, i) => ReadBurn(b, path + ".burns[" + i + "]")),
                StepMachine.Parse(node.Field(path, "state").AsString(path + ".state")),
                progress.Kind == JsonKind.Null
                    ? null
                    : new BurnProgress(progress.Field(path, "burnIndex").AsInt(path + ".progress.burnIndex"), progress.Field(path, "deltaVDone").AsNumber(path + ".progress.deltaVDone")));
        }

        private static OrbitChange ReadChange(JsonNode node, string path, OrbitChangeKind kind)
        {
            if (kind == OrbitChangeKind.Transfer)
            {
                JsonNode destination = node.TryField(path, "destination") ?? throw new ProtocolException(path + ".destination: a transfer needs its body");
                return new OrbitChange(OrbitChangeKind.Transfer, node.Field(path, "target").AsNumber(path + ".target"), destination: destination.AsString(path + ".destination"), arrivalInclination: node.TryField(path, "arrivalInclination")?.AsNumber(path + ".arrivalInclination"), thenLand: node.TryField(path, "thenLand")?.AsBool(path + ".thenLand") ?? false);
            }
            if (OrbitChange.Meets(kind))
            {
                JsonNode meet = node.TryField(path, "rendezvous") ?? throw new ProtocolException(path + ".rendezvous: a rendezvous needs its target vessel");
                JsonNode port = meet.Field(path + ".rendezvous", "port");
                return new OrbitChange(kind, rendezvous: new RendezvousTarget(
                    meet.Field(path + ".rendezvous", "vessel").AsString(path + ".rendezvous.vessel"),
                    meet.Field(path + ".rendezvous", "name").AsString(path + ".rendezvous.name"),
                    port.Kind == JsonKind.Null ? null : (uint?)port.AsNumber(path + ".rendezvous.port")));
            }
            if (kind == OrbitChangeKind.Land)
            {
                JsonNode landing = node.TryField(path, "landing") ?? throw new ProtocolException(path + ".landing: a landing needs its target");
                string siteText = landing.Field(path + ".landing", "site").AsString(path + ".landing.site");
                if (!Enum.TryParse(siteText, out LandingSiteKind site))
                {
                    throw new ProtocolException(path + ".landing.site: unknown site " + siteText);
                }
                return OrbitChange.Land(LandingTarget.Of(
                    site,
                    landing.Field(path + ".landing", "latitude").AsNumber(path + ".landing.latitude"),
                    landing.Field(path + ".landing", "longitude").AsNumber(path + ".landing.longitude")));
            }
            if (kind == OrbitChangeKind.DeployConstellation)
            {
                string endText = node.TryField(path, "endOfLife")?.AsString(path + ".endOfLife") ?? nameof(EndOfLife.Auto);
                if (!Enum.TryParse(endText, out EndOfLife end))
                {
                    throw new ProtocolException(path + ".endOfLife: unknown end of life " + endText);
                }
                RingPlane? ring = null;
                if (node.TryField(path, "ring") is JsonNode ringNode)
                {
                    var plane = ringNode.AsArray(path + ".ring", (n, i) => n.AsNumber(path + ".ring[" + i + "]"));
                    if (plane.Count != 2)
                    {
                        throw new ProtocolException(path + ".ring: expected an inclination and an ascending node");
                    }
                    ring = new RingPlane(plane[0], plane[1]);
                }
                return OrbitChange.Deploy(node.Field(path, "target").AsNumber(path + ".target"), ring, end);
            }
            if (kind != OrbitChangeKind.Launch)
            {
                return new OrbitChange(kind, node.Field(path, "target").AsNumber(path + ".target"));
            }
            JsonNode launch = node.TryField(path, "launch") ?? throw new ProtocolException(path + ".launch: a launch needs its target orbit");
            var x = launch.AsArray(path + ".launch", (n, i) => n.Kind == JsonKind.Null ? (double?)null : n.AsNumber(path + ".launch[" + i + "]"));
            if (x.Count != 4)
            {
                throw new ProtocolException(path + ".launch: expected apoapsis, periapsis, inclination and LAN");
            }
            if (x[0] is not double apoapsis || x[1] is not double periapsis)
            {
                throw new ProtocolException(path + ".launch: apoapsis and periapsis are required");
            }
            return OrbitChange.Launch(new LaunchTarget(apoapsis, periapsis, x[2], x[3]));
        }

        private static PlannedBurn ReadBurn(JsonNode b, string path)
        {
            JsonNode pinned = b.Field(path, "pinnedStage");
            JsonNode? afterNode = b.TryField(path, "after");
            OrbitElements? after = null;
            if (afterNode != null)
            {
                var x = afterNode.AsArray(path + ".after", (n, i) => n.AsNumber(path + ".after[" + i + "]"));
                if (x.Count != 8)
                {
                    throw new ProtocolException(path + ".after: expected 8 orbital elements");
                }
                after = new OrbitElements(x[0], x[1], x[2], x[3], x[4], x[5], x[6], x[7]);
            }
            return new PlannedBurn(
                b.Field(path, "vessel").AsString(path + ".vessel"),
                b.Field(path, "label").AsString(path + ".label"),
                b.Field(path, "nodeUt").AsNumber(path + ".nodeUt"),
                b.Field(path, "startUt").AsNumber(path + ".startUt"),
                b.Field(path, "duration").AsNumber(path + ".duration"),
                b.Field(path, "prograde").AsNumber(path + ".prograde"),
                b.Field(path, "normal").AsNumber(path + ".normal"),
                b.Field(path, "radial").AsNumber(path + ".radial"),
                pinned.Kind == JsonKind.Null ? (int?)null : pinned.AsInt(path + ".pinnedStage"),
                after,
                b.TryField(path, "body")?.AsString(path + ".body"),
                null,
                b.TryField(path, "releasePart") is JsonNode part ? (uint)part.AsNumber(path + ".releasePart") : (uint?)null);
        }
    }
}
