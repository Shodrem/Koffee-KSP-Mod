using System;
using System.Linq;

namespace ShoAerospace.Koffee
{
    // One observed frame. Optical coordinates in metres; FOV in degrees, other angles in radians.
    internal sealed class TelescopePairSample
    {
        internal double UT { get; }
        internal double PX { get; }
        internal double PY { get; }
        internal double PZ { get; }
        internal double MX { get; }
        internal double MY { get; }
        internal double MZ { get; }
        internal double PlanetRadius { get; }
        internal double MoonRadius { get; }
        internal double Fov { get; }
        internal double PlanetAngle => Math.Asin(PlanetRadius / Norm(PX,PY,PZ));
        internal double MoonAngle => Math.Asin(MoonRadius / Norm(MX,MY,MZ));
        internal double Separation => Math.Atan2(Norm(PY*MZ-PZ*MY,PZ*MX-PX*MZ,PX*MY-PY*MX),PX*MX+PY*MY+PZ*MZ);
        internal double PlanetPixels => 512*Math.Tan(PlanetAngle)/Math.Tan(Fov*Math.PI/360);
        internal double MoonPixels => 512*Math.Tan(MoonAngle)/Math.Tan(Fov*Math.PI/360);

        internal TelescopePairSample(double ut,double px,double py,double pz,double mx,double my,double mz,
            double planetRadius,double moonRadius,double fov)
        {
            if (new[] {ut,px,py,pz,mx,my,mz,planetRadius,moonRadius,fov}.Any(n => !Finite(n)) ||
                ut < 0 || planetRadius <= 0 || moonRadius <= 0 || fov < TelescopeOptics.MinFieldOfView || fov > 60 ||
                !Finite(Norm(px,py,pz)) || !Finite(Norm(mx,my,mz)) ||
                Norm(px,py,pz) <= planetRadius || Norm(mx,my,mz) <= moonRadius)
                throw new ArgumentException("Invalid planet/moon frame.");
            UT=ut; PX=px; PY=py; PZ=pz; MX=mx; MY=my; MZ=mz;
            PlanetRadius=planetRadius; MoonRadius=moonRadius; Fov=fov;
        }
        internal string Failure(double maximumSeparationRadii,double minimumPlanetPixels,double minimumMoonPixels)
        {
            if (!SettingsValid(maximumSeparationRadii,minimumPlanetPixels,minimumMoonPixels)) return "Geometry";
            if (!Fits(PX,PY,PZ,PlanetRadius) || !Fits(MX,MY,MZ,MoonRadius)) return "Field";
            // External tangency is also rejected: the two complete disks must be visibly separate.
            if (Separation <= PlanetAngle+MoonAngle+1e-10) return "Overlap";
            if (PlanetPixels < minimumPlanetPixels || MoonPixels < minimumMoonPixels) return "Small";
            if (Separation > maximumSeparationRadii*PlanetAngle) return "Far";
            return "";
        }
        private bool Fits(double x,double y,double z,double radius)
        {
            // Sphere against the four planes of the actual square perspective frustum.
            // One percent angular margin avoids accepting a disk exactly on a frame edge.
            double half=Fov*Math.PI/360*.99;
            return z > 0 && z*Math.Sin(half)-Math.Abs(x)*Math.Cos(half) >= radius &&
                z*Math.Sin(half)-Math.Abs(y)*Math.Cos(half) >= radius;
        }
        internal static bool SettingsValid(double separation,double planetPixels,double moonPixels) =>
            Finite(separation) && separation >= 1 && separation <= 256 &&
            Finite(planetPixels) && planetPixels >= 1 && planetPixels <= 512 &&
            Finite(moonPixels) && moonPixels >= 1 && moonPixels <= 512;
        private static double Norm(double x,double y,double z) => Math.Sqrt(x*x+y*y+z*z);
        private static bool Finite(double n) => !double.IsNaN(n) && !double.IsInfinity(n);
    }
}
