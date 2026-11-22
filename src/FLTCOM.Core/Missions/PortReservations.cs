using System.Collections.Generic;
using System.Linq;
using FLTCOM.Core.Planning;

namespace FLTCOM.Core.Missions
{
    /// <summary>
    /// An approved rendezvous holds its target's port until it ends. A rendezvous without a port holds the whole vessel:
    /// it clashes with any other rendezvous to that vessel.
    /// </summary>
    public static class PortReservations
    {
        public static bool Holds(Operation operation) =>
            operation.Change.Rendezvous != null && !StepMachine.IsTerminal(operation.State) && !StepMachine.IsEditable(operation.State);

        /// <summary>The live operation already holding what <paramref name="wanted"/> asks for, or null.</summary>
        public static Operation? Holder(IEnumerable<Operation> operations, RendezvousTarget wanted, string? exceptOperationId = null) =>
            operations.FirstOrDefault(o => o.Id != exceptOperationId && Holds(o) && Clash(o.Change.Rendezvous!, wanted));

        public static string Refusal(Operation holder, RendezvousTarget wanted) =>
            (wanted.PortId == null ? wanted.Name : "that port on " + wanted.Name) + " is reserved by operation " + holder.Codename;

        private static bool Clash(RendezvousTarget held, RendezvousTarget wanted) =>
            held.VesselId == wanted.VesselId && (held.PortId == null || wanted.PortId == null || held.PortId == wanted.PortId);
    }
}
