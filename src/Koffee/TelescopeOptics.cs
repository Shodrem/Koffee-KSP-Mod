using System;

namespace ShoAerospace.Koffee
{
    // Geometry and optical limits are independent of the PAW and rendering backend.
    public static class TelescopeOptics
    {
        public const float MinFieldOfView = 0.02f;
        public const float MaxFieldOfView = 60f;

        public static float ClampFieldOfView(float degrees) =>
            float.IsNaN(degrees) || float.IsInfinity(degrees) ? MaxFieldOfView :
            Math.Max(MinFieldOfView, Math.Min(MaxFieldOfView, degrees));

        public static double Magnification(float degrees) =>
            Math.Tan(MaxFieldOfView * Math.PI / 360) /
            Math.Tan(ClampFieldOfView(degrees) * Math.PI / 360);

        // Full disk in an inscribed circular field, centre in the inner 20% of
        // the full FOV, and at least eight projected pixels by default.
        public static bool CaptureSettingsValid(double fov, double maximum, double fraction, double pixels) =>
            Finite(fov) && fov >= MinFieldOfView && fov <= MaxFieldOfView &&
            Finite(maximum) && maximum > 0 && maximum <= 5 &&
            Finite(fraction) && fraction > 0 && fraction <= 0.5 &&
            Finite(pixels) && pixels >= 1 && pixels <= 512;

        public static double CaptureTolerance(double fov, double angularRadius, double maximum, double fraction) =>
            Math.Min(maximum, Math.Min(fov * fraction, fov / 2 - angularRadius));

        public static string CaptureFailure(double fov, double angularRadius, double error,
            double maximum, double fraction, double minimumPixels)
        {
            if (!CaptureSettingsValid(fov, maximum, fraction, minimumPixels) || !Finite(angularRadius) ||
                angularRadius <= 0 || angularRadius >= 90 || !Finite(error) || error < 0) return "Geometry";
            if (angularRadius >= fov / 2) return "DiskTooLarge";
            double diameter = 512 * Math.Tan(angularRadius * Math.PI / 180) / Math.Tan(fov * Math.PI / 360);
            if (diameter < minimumPixels) return "DiskTooSmall";
            if (error > CaptureTolerance(fov, angularRadius, maximum, fraction)) return "Pointing";
            return "";
        }

        // Conservatively reject overlap of apparent disks for a potentially
        // foreground sphere; also handles an observer inside that sphere.
        public static bool DiskOccluded(double tx, double ty, double tz, double targetRadius,
            double x, double y, double z, double radius)
        {
            double td = Math.Sqrt(tx*tx + ty*ty + tz*tz), d = Math.Sqrt(x*x + y*y + z*z);
            if (!Finite(td) || td <= targetRadius || !Finite(d) || !Finite(radius) || radius <= 0) return true;
            if (d <= radius) return true;
            if (d - radius >= td + targetRadius) return false;
            double separation = Math.Acos(Math.Max(-1, Math.Min(1, (tx*x + ty*y + tz*z) / (td*d))));
            return separation <= Math.Asin(targetRadius / td) + Math.Asin(radius / d);
        }

        private static bool Finite(double n) => !double.IsNaN(n) && !double.IsInfinity(n);

        // True when the centre ray meets a foreground sphere before the target's near surface.
        // Coordinates are relative to the optical origin, in metres, from the same frame.
        public static bool Occludes(double dx, double dy, double dz, double distance,
            double x, double y, double z, double radius)
        {
            if (distance <= 0 || radius <= 0) return false;
            double projection = x * dx + y * dy + z * dz;
            double perpendicular2 = x*x + y*y + z*z - projection*projection;
            double discriminant = radius*radius - perpendicular2;
            if (discriminant < 0) return false;
            double half = Math.Sqrt(discriminant);
            double entry = projection - half;
            double exit = projection + half;
            return exit > 0 && Math.Max(0, entry) < distance;
        }
    }
}
