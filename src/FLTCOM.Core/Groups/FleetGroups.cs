using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using FLTCOM.Core.Protocol;

namespace FLTCOM.Core.Groups
{
    /// <summary>A saved, named set of vessels that plans and flies as one unit.</summary>
    public sealed class FleetGroup
    {
        public FleetGroup(string id, string name, IReadOnlyList<string> vesselIds)
        {
            Id = id ?? throw new ArgumentNullException(nameof(id));
            Name = name ?? throw new ArgumentNullException(nameof(name));
            VesselIds = vesselIds ?? throw new ArgumentNullException(nameof(vesselIds));
        }

        public string Id { get; }

        public string Name { get; }

        public IReadOnlyList<string> VesselIds { get; }
    }

    /// <summary>Every group of the save. Each edit returns an error string or null.</summary>
    public sealed class FleetGroups
    {
        private readonly List<FleetGroup> _groups = new List<FleetGroup>();

        public IReadOnlyList<FleetGroup> All => _groups;

        public int Revision { get; private set; }

        public FleetGroup? Find(string? id) => _groups.FirstOrDefault(g => g.Id == id);

        public string? Create(string? name, IEnumerable<string> vesselIds, out FleetGroup? created)
        {
            created = null;
            string? error = CheckName(name, null);
            if (error != null)
            {
                return error;
            }
            created = new FleetGroup(NextId(), name!.Trim(), vesselIds.Distinct().ToList());
            _groups.Add(created);
            Revision++;
            return null;
        }

        public string? Rename(string? id, string? name)
        {
            FleetGroup? group = Find(id);
            if (group == null)
            {
                return "no group " + id;
            }
            string? error = CheckName(name, id);
            if (error != null)
            {
                return error;
            }
            Put(new FleetGroup(group.Id, name!.Trim(), group.VesselIds));
            return null;
        }

        public string? Delete(string? id)
        {
            if (_groups.RemoveAll(g => g.Id == id) == 0)
            {
                return "no group " + id;
            }
            Revision++;
            return null;
        }

        public string? Add(string? id, IEnumerable<string> vesselIds)
        {
            FleetGroup? group = Find(id);
            if (group == null)
            {
                return "no group " + id;
            }
            Put(new FleetGroup(group.Id, group.Name, group.VesselIds.Concat(vesselIds).Distinct().ToList()));
            return null;
        }

        public string? Remove(string? id, IEnumerable<string> vesselIds)
        {
            FleetGroup? group = Find(id);
            if (group == null)
            {
                return "no group " + id;
            }
            var gone = new HashSet<string>(vesselIds);
            Put(new FleetGroup(group.Id, group.Name, group.VesselIds.Where(v => !gone.Contains(v)).ToList()));
            return null;
        }

        public void Load(IEnumerable<FleetGroup> saved)
        {
            _groups.Clear();
            _groups.AddRange(saved);
            Revision++;
        }

        public static string Write(IEnumerable<FleetGroup> groups)
        {
            var w = new JsonWriter();
            w.BeginArray();
            foreach (FleetGroup g in groups)
            {
                w.BeginObject();
                w.Name("id"); w.String(g.Id);
                w.Name("name"); w.String(g.Name);
                w.Name("vessels"); w.BeginArray();
                foreach (string v in g.VesselIds)
                {
                    w.String(v);
                }
                w.EndArray();
                w.EndObject();
            }
            w.EndArray();
            return w.ToString();
        }

        public static IReadOnlyList<FleetGroup> Read(string json) =>
            JsonNode.Parse(json ?? throw new ArgumentNullException(nameof(json))).AsArray("$", (node, i) =>
            {
                string path = "$[" + i + "]";
                return new FleetGroup(
                    node.Field(path, "id").AsString(path + ".id"),
                    node.Field(path, "name").AsString(path + ".name"),
                    node.Field(path, "vessels").AsArray(path + ".vessels", (v, j) => v.AsString(path + ".vessels[" + j + "]")));
            });

        private string? CheckName(string? name, string? self)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return "a group needs a name";
            }
            return _groups.Any(g => g.Id != self && string.Equals(g.Name, name!.Trim(), StringComparison.OrdinalIgnoreCase))
                ? "a group named " + name!.Trim() + " already exists"
                : null;
        }

        private string NextId()
        {
            int max = _groups.Select(g => int.TryParse(g.Id.Replace("grp-", string.Empty), NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) ? n : 0).DefaultIfEmpty(0).Max();
            return "grp-" + (max + 1).ToString(CultureInfo.InvariantCulture);
        }

        private void Put(FleetGroup group)
        {
            _groups[_groups.FindIndex(g => g.Id == group.Id)] = group;
            Revision++;
        }
    }
}
