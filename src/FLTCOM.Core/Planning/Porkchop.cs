using System;
using System.Collections.Generic;
using FLTCOM.Core.Lambert;
using FLTCOM.Core.TwoBody;

namespace FLTCOM.Core.Planning
{
    /// <summary>The cheapest departure and arrival found between two planets, with the hyperbolic excess velocities at both ends.</summary>
    public sealed class TransferWindow
    {
        public TransferWindow(double departureUt, double arrivalUt, Vector3d departureExcess, Vector3d arrivalExcess, double planeChange, double deltaV, Vector3d aim)
        {
            Aim = aim;
            DepartureUt = departureUt;
            ArrivalUt = arrivalUt;
            DepartureExcess = departureExcess;
            ArrivalExcess = arrivalExcess;
            PlaneChange = planeChange;
            DeltaV = deltaV;
        }

        public double DepartureUt { get; }

        public double ArrivalUt { get; }

        /// <summary>The velocity relative to the departure planet on leaving its sphere of influence.</summary>
        public Vector3d DepartureExcess { get; }

        /// <summary>The velocity relative to the arrival planet on entering its sphere of influence.</summary>
        public Vector3d ArrivalExcess { get; }

        /// <summary>The mid-course plane change, 90 degrees of travel before arrival; zero for a ballistic transfer.</summary>
        public double PlaneChange { get; }

        /// <summary>Ejection from the parking orbit, the plane change and capture into the target orbit, all impulsive.</summary>
        public double DeltaV { get; }

        public double FlightTime => ArrivalUt - DepartureUt;

        /// <summary>Where the arc from the departure planet ends, relative to the star: the arrival planet, or for a plane-change transfer its position turned into the departure plane.</summary>
        public Vector3d Aim { get; }
    }

    /// <summary>A grid of transfer costs: departure time across, time of flight down; positive infinity where no transfer was found.</summary>
    public sealed class PorkchopGrid
    {
        public PorkchopGrid(double departureStart, double departureStep, double flightStart, double flightStep, int columns, int rows, double[] deltaV, TransferWindow best)
        {
            DepartureStart = departureStart;
            DepartureStep = departureStep;
            FlightStart = flightStart;
            FlightStep = flightStep;
            Columns = columns;
            Rows = rows;
            DeltaV = deltaV;
            Best = best;
        }

        public double DepartureStart { get; }

        public double DepartureStep { get; }

        public double FlightStart { get; }

        public double FlightStep { get; }

        public int Columns { get; }

        public int Rows { get; }

        /// <summary>Row-major: index row * Columns + column.</summary>
        public IReadOnlyList<double> DeltaV { get; }

        public TransferWindow Best { get; }
    }

    /// <summary>
    /// The porkchop search between two planets of one star: Izzo's Lambert solution for every departure and flight time on a grid
    /// spanning one synodic period, costed as ejection from a circular parking orbit plus capture into a circular target orbit,
    /// then refined around the cheapest cell. Each cell also prices a transfer flown in the departure planet's plane with a
    /// plane change 90 degrees before arrival (as alexmoon's launch window planner does) and keeps the cheaper of the two, so a
    /// near-180-degree transfer to a slightly inclined planet is not priced as a steep out-of-plane arc.
    /// </summary>
    public static class Porkchop
    {
        public const int Columns = 72;
        public const int Rows = 36;

        /// <param name="parkingRadius">Radius of the circular orbit the ejection starts from.</param>
        /// <param name="captureRadius">Radius of the circular orbit the capture ends in.</param>
        /// <param name="parkingNormal">The parking orbit's angular momentum direction; null means the equator (stock planets have no axial tilt).</param>
        public static PorkchopGrid Search(SolarSystem system, string from, string to, double earliestUt, double parkingRadius, double captureRadius, Vector3d? parkingNormal = null)
        {
            Vector3d parking = (parkingNormal ?? new Vector3d(0, 0, 1)).Normalized;
            SystemBody a = system[from];
            SystemBody b = system[to];
            if (a.Parent == null || a.Parent != b.Parent)
            {
                throw new ArgumentException(from + " and " + to + " do not orbit the same star");
            }
            OrbitElements oa = a.Orbit!;
            OrbitElements ob = b.Orbit!;
            double hohmann = Math.PI * Math.Sqrt(Math.Pow((oa.SemiMajorAxis + ob.SemiMajorAxis) / 2, 3) / oa.Mu);
            double synodic = Angles.TwoPi / Math.Abs(oa.MeanMotion - ob.MeanMotion);
            double span = Math.Min(synodic, 2 * Math.Max(oa.Period, ob.Period)) * 1.1;
            double flightStart = 0.5 * hohmann;
            double flightStep = hohmann / Rows;
            double departureStep = span / Columns;

            var grid = new double[Columns * Rows];
            double bestCost = double.PositiveInfinity;
            double bestDeparture = earliestUt;
            double bestFlight = hohmann;
            for (int col = 0; col < Columns; col++)
            {
                double departure = earliestUt + col * departureStep;
                for (int row = 0; row < Rows; row++)
                {
                    double flight = flightStart + row * flightStep;
                    double cost = Cost(a, b, departure, flight, parkingRadius, captureRadius, parking);
                    grid[row * Columns + col] = cost;
                    if (cost < bestCost)
                    {
                        bestCost = cost;
                        bestDeparture = departure;
                        bestFlight = flight;
                    }
                }
            }
            if (double.IsInfinity(bestCost))
            {
                throw new InvalidOperationException("no transfer from " + from + " to " + to + " was found");
            }

            // Pattern search from the best cell down to about a minute.
            double stepT = departureStep;
            double stepF = flightStep;
            while (stepT > 60 || stepF > 60)
            {
                bool moved = false;
                foreach ((double dt, double df) in new[] { (stepT, 0.0), (-stepT, 0.0), (0.0, stepF), (0.0, -stepF) })
                {
                    double departure = Math.Max(earliestUt, bestDeparture + dt);
                    double flight = bestFlight + df;
                    if (flight <= 0)
                    {
                        continue;
                    }
                    double cost = Cost(a, b, departure, flight, parkingRadius, captureRadius, parking);
                    if (cost < bestCost)
                    {
                        bestCost = cost;
                        bestDeparture = departure;
                        bestFlight = flight;
                        moved = true;
                    }
                }
                if (!moved)
                {
                    stepT /= 2;
                    stepF /= 2;
                }
            }
            TransferWindow best = Evaluate(a, b, bestDeparture, bestFlight, parkingRadius, captureRadius, parking)!;
            return new PorkchopGrid(earliestUt, departureStep, flightStart, flightStep, Columns, Rows, grid, best);
        }

        /// <summary>
        /// The burn from a circular parking orbit of radius <paramref name="r"/> (angular momentum along <paramref name="parkingNormal"/>) onto
        /// an escape that leaves the sphere of influence at <paramref name="excess"/>. The escape's periapsis lies in the parking plane, so an
        /// excess pointing out of that plane costs a plane change at periapsis; positive infinity when no such escape exists.
        /// Ported from alexmoon's launch window planner (ejectionAngleFromPeriapsis, circularToEscapeDeltaV).
        /// </summary>
        public static double Ejection(CentralBody body, double r, Vector3d excess, Vector3d parkingNormal)
        {
            double mu = body.GravParameter;
            double soi = body.SphereOfInfluence;
            double vSoi = excess.Magnitude;
            double v0 = Math.Sqrt(mu / r);
            double vp = Math.Sqrt(vSoi * vSoi + 2 * mu / r - 2 * mu / soi);
            double e = r * vp * vp / mu - 1;
            double a = r / (1 - e);
            double atSoi = Math.Acos(Math.Max(-1, Math.Min(1, (a * (1 - e * e) - soi) / (e * soi))));
            double sweep = atSoi + Math.Asin(Math.Min(1, vp * r / (vSoi * soi)));
            double sinDeclination = Vector3d.Dot(excess, parkingNormal) / vSoi;
            if (Math.Abs(sinDeclination) > Math.Abs(Math.Sin(sweep)))
            {
                return double.PositiveInfinity;
            }
            double sinTilt = sinDeclination / Math.Sin(sweep);
            double cosTilt = Math.Sqrt(1 - sinTilt * sinTilt);
            return Math.Sqrt(v0 * v0 + vp * vp - 2 * v0 * vp * cosTilt);
        }

        /// <summary>The burn at periapsis radius <paramref name="r"/> that turns an arrival at <paramref name="vSoi"/> on the sphere of influence into a circular orbit.</summary>
        public static double Capture(CentralBody body, double r, double vSoi) =>
            Math.Sqrt(vSoi * vSoi + 2 * body.GravParameter / r - 2 * body.GravParameter / body.SphereOfInfluence) - Math.Sqrt(body.GravParameter / r);

        /// <summary>The total delta-v of leaving <paramref name="a"/> at <paramref name="departure"/> and reaching <paramref name="b"/> after <paramref name="flight"/>; positive infinity when there is no solution.</summary>
        public static double Cost(SystemBody a, SystemBody b, double departure, double flight, double parkingRadius, double captureRadius, Vector3d parkingNormal) =>
            Evaluate(a, b, departure, flight, parkingRadius, captureRadius, parkingNormal)?.DeltaV ?? double.PositiveInfinity;

        /// <summary>The cheaper of the ballistic and the plane-change transfer for one departure and flight time, or null when neither solves.</summary>
        public static TransferWindow? Evaluate(SystemBody a, SystemBody b, double departure, double flight, double parkingRadius, double captureRadius, Vector3d parkingNormal)
        {
            double mu = a.Orbit!.Mu;
            StateVector sa = a.Orbit.StateAt(departure);
            StateVector sb = b.Orbit!.StateAt(departure + flight);
            Vector3d normal = Vector3d.Cross(sa.Position, sa.Velocity).Normalized;

            TransferWindow? best = null;
            (Vector3d V1, Vector3d V2)? ballistic = Lambert(mu, sa.Position, sb.Position, flight, normal);
            if (ballistic is (Vector3d v1, Vector3d v2))
            {
                Vector3d vOut = v1 - sa.Velocity;
                Vector3d vIn = v2 - sb.Velocity;
                double cost = Ejection(a.Body, parkingRadius, vOut, parkingNormal) + Capture(b.Body, captureRadius, vIn.Magnitude);
                if (!double.IsInfinity(cost))
                {
                    best = new TransferWindow(departure, departure + flight, vOut, vIn, 0, cost, sb.Position);
                }
            }

            // Transfer in the departure plane to the arrival point turned into it, and a turn on the way that lifts the path onto the
            // real arrival point; the turn is placed where it is cheapest (alexmoon's optimalPlaneChange, two passes).
            double latitude = Math.Asin(Math.Max(-1, Math.Min(1, Vector3d.Dot(sb.Position, normal) / sb.Position.Magnitude)));
            Vector3d along = (sb.Position - normal * Vector3d.Dot(sb.Position, normal)).Normalized;
            if (Vector3d.AngleAbout(sa.Position, along, normal) > Math.PI / 2 && Math.Abs(latitude) > 1e-12)
            {
                bool outward = sb.Position.Magnitude >= sa.Position.Magnitude;
                Vector3d target = along * sb.Position.Magnitude;
                Vector3d axis = along;
                double angle = 0;
                double x = Math.PI / 2;
                (Vector3d V1, Vector3d V2)? planar = null;
                for (int pass = 0; pass < 3; pass++)
                {
                    if (pass > 0)
                    {
                        angle = Math.Atan2(Math.Tan(latitude), Math.Sin(x));
                        axis = Vector3d.Rotate(along, normal, -x);
                        Vector3d up = Vector3d.Rotate(sb.Position, axis, angle);
                        Vector3d down = Vector3d.Rotate(sb.Position, axis, -angle);
                        if (Math.Abs(Vector3d.Dot(up, normal)) < Math.Abs(Vector3d.Dot(down, normal)))
                        {
                            target = up;
                        }
                        else
                        {
                            target = down;
                            angle = -angle;
                        }
                    }
                    planar = Lambert(mu, sa.Position, target, flight, normal);
                    if (planar == null)
                    {
                        break;
                    }
                    if (pass < 2)
                    {
                        var start = new StateVector(sa.Position, planar.Value.V1);
                        Vector3d to = target.Normalized;
                        x = Golden(outward ? 0 : Math.PI / 2, outward ? Math.PI / 2 : Math.PI, s => Turn(mu, start, to, normal, s, latitude));
                    }
                }
                if (planar is (Vector3d p1, Vector3d p2))
                {
                    double turn = Turn(mu, new StateVector(sa.Position, p1), target.Normalized, normal, x, latitude);
                    Vector3d vOut = p1 - sa.Velocity;
                    Vector3d vIn = Vector3d.Rotate(p2, axis, -angle) - sb.Velocity;
                    double cost = Ejection(a.Body, parkingRadius, vOut, parkingNormal) + turn + Capture(b.Body, captureRadius, vIn.Magnitude);
                    if (!double.IsInfinity(cost) && (best == null || cost < best.DeltaV))
                    {
                        best = new TransferWindow(departure, departure + flight, vOut, vIn, turn, cost, target);
                    }
                }
            }
            return best;
        }

        /// <summary>The plane change that lifts a path by <paramref name="latitude"/> at its arrival, made <paramref name="x"/> radians of travel before arriving at <paramref name="arrival"/>.</summary>
        private static double Turn(double mu, StateVector start, Vector3d arrival, Vector3d normal, double x, double latitude)
        {
            double angle = Math.Atan2(Math.Tan(latitude), Math.Sin(x));
            Vector3d at = Vector3d.Rotate(arrival, normal, -x);
            Vector3d h = Vector3d.Cross(start.Position, start.Velocity);
            double radius = Vector3d.Dot(h, h) / mu / (1 + Vector3d.Dot(Kepler.EccentricityVector(mu, start), at));
            double speed = Math.Sqrt(Math.Max(0, mu * (2 / radius - 1 / Kepler.SemiMajorAxis(mu, start))));
            return Math.Abs(2 * speed * Math.Sin(0.5 * angle));
        }

        private static double Golden(double lo, double hi, Func<double, double> f)
        {
            const double R = 0.6180339887498949;
            double c = hi - R * (hi - lo);
            double d = lo + R * (hi - lo);
            while (hi - lo > 1e-2)
            {
                if (f(c) < f(d))
                {
                    hi = d;
                }
                else
                {
                    lo = c;
                }
                c = hi - R * (hi - lo);
                d = lo + R * (hi - lo);
            }
            return (lo + hi) / 2;
        }

        internal static (Vector3d V1, Vector3d V2)? Lambert(double mu, Vector3d r1, Vector3d r2, double flight, Vector3d normal)
        {
            try
            {
                return Izzo.Solve(mu, r1, r2, flight, TransferGeometry.Prograde, 0, normal);
            }
            catch (InvalidOperationException)
            {
                return null;
            }
            catch (ArgumentException)
            {
                return null;
            }
        }
    }
}
