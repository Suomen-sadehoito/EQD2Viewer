using VMS.TPS.Common.Model.Types;

namespace ESAPI_IsodoseViewer.Core.Extensions
{
    public static class VVectorExtensions
    {
        // Calculates the dot product of two VVectors
        public static double Dot(this VVector vector1, VVector vector2)
        {
            return (vector1.x * vector2.x) + (vector1.y * vector2.y) + (vector1.z * vector2.z);
        }

        // Calculates the cross product of two VVectors
        public static VVector Cross(this VVector vector1, VVector vector2)
        {
            return new VVector(
                (vector1.y * vector2.z) - (vector1.z * vector2.y),
                (vector1.z * vector2.x) - (vector1.x * vector2.z),
                (vector1.x * vector2.y) - (vector1.y * vector2.x)
            );
        }
    }
}